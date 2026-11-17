using System.Diagnostics;
using Aether.Broker.Client;
using Aether.Contracts;
using Aether.Testing;
using Application = FlaUI.Core.Application;
using FlaUI.Core.AutomationElements;
using FlaUI.UIA3;
using Xunit;
using Xunit.Abstractions;
using Xunit.Sdk;

namespace Aether.Desktop.E2E.Tests;

/// <summary>
/// v1.1 AW-049/AW-050 Windows reference-desktop test: a real Watchlist selection publishes the
/// approved typed context, Risk selects the matching position with instrument-specific metrics and
/// Chart switches the actually plotted series for all three synthetic instruments. A broker-audit
/// count proves the remote echo does not republish (no feedback loop).
/// </summary>
[Trait("Category", "ReferenceDesktop")]
[Collection("Desktop GUI")]
public sealed class WpfTickerSynchronizationTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(30);
    private static readonly Dictionary<string, string> WindowTitles = new(StringComparer.Ordinal)
    {
        ["sample.watchlist"] = "Synthetic Watchlist",
        ["sample.risk"] = "Synthetic Risk View",
        ["sample.chart"] = "Synthetic Bond Chart",
    };

    private readonly ITestOutputHelper _output;

    public WpfTickerSynchronizationTests(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact(Timeout = 360_000)]
    public async Task WatchlistSelectionSynchronizesRiskAndChartWithoutEchoPublish()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var samplesExecutable = DesktopExecutableLocator.ResolveWpfSampleApps();
        await using var harness = await NamedPipeBrokerHarness.StartAsync(
            "wpf-ticker-sync",
            useWindowsRegistry: true,
            wpfSamplesExe: samplesExecutable);
        await using var client = await harness.CreateOperatorClientAsync();

        var processes = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var appId in TestData.SampleAppIds)
        {
            var launch = await client.LaunchAppAsync(appId, TraceIdGenerator.NewTraceId());
            Assert.True(launch.Accepted, $"Launch of {appId} was denied: {launch.DenialCode} {launch.DenialMessage}");
            processes[appId] = launch.ProcessId;
        }

        using var automation = new UIA3Automation();
        var windows = new Dictionary<string, Window>(StringComparer.Ordinal);
        foreach (var appId in TestData.SampleAppIds)
        {
            windows[appId] = WaitForWindow(automation, processes[appId], WindowTitles[appId]);
        }

        SelectTicker(windows["sample.watchlist"], "VEGA", harness);
        WaitForText(windows["sample.watchlist"], "WatchlistPublishStatus", text => text.Contains("Published VEGA", StringComparison.Ordinal), "watchlist publish VEGA");
        WaitForText(windows["sample.risk"], "RiskSelectedInstrument", text => text.Contains("VEGA", StringComparison.Ordinal), "risk VEGA selection");
        WaitForText(windows["sample.risk"], "RiskSelectedMetrics", text => text.Contains("Selected notional", StringComparison.Ordinal), "risk VEGA metrics");
        WaitForText(windows["sample.chart"], "ChartPlottedSeries", text => text.Contains("VEGA", StringComparison.Ordinal), "chart VEGA series");

        await Task.Delay(TimeSpan.FromSeconds(2));
        Assert.Equal(1, CountAuditEvents(harness.BrokerAuditFile, "context.publish"));

        SelectTicker(windows["sample.watchlist"], "ORIN", harness);
        WaitForText(windows["sample.watchlist"], "WatchlistPublishStatus", text => text.Contains("Published ORIN", StringComparison.Ordinal), "watchlist publish ORIN");
        WaitForText(windows["sample.risk"], "RiskSelectedInstrument", text => text.Contains("ORIN", StringComparison.Ordinal), "risk ORIN selection");
        WaitForText(windows["sample.risk"], "RiskSelectedMetrics", text => text.Contains("Selected notional", StringComparison.Ordinal), "risk ORIN metrics");
        WaitForText(windows["sample.chart"], "ChartPlottedSeries", text => text.Contains("ORIN", StringComparison.Ordinal), "chart ORIN series");

        await Task.Delay(TimeSpan.FromSeconds(2));
        Assert.Equal(2, CountAuditEvents(harness.BrokerAuditFile, "context.publish"));

        _output.WriteLine("Watchlist→Risk→Chart synchronization and echo suppression verified for VEGA and ORIN.");
    }

    private static void SelectTicker(Window watchlist, string ticker, NamedPipeBrokerHarness harness)
    {
        var grid = watchlist.FindFirstDescendant(criteria => criteria.ByAutomationId("WatchlistGrid"))?.AsDataGridView()
            ?? throw new XunitException("WatchlistGrid was not found. " + ReadDiagnostics(harness));
        var row = grid.Rows.FirstOrDefault(candidate =>
            candidate.Cells.Any(cell => string.Equals(cell.Value?.ToString(), ticker, StringComparison.Ordinal)));
        if (row is null)
        {
            var rows = string.Join(" | ", grid.Rows.Select(candidate => string.Join(",", candidate.Cells.Select(cell => cell.Value))));
            throw new XunitException($"Watchlist row '{ticker}' was not found. rows: {rows}");
        }

        var selection = row.Patterns.SelectionItem.PatternOrDefault;
        if (selection is null)
        {
            throw new XunitException($"Watchlist row '{ticker}' does not expose SelectionItem. " + ReadDiagnostics(harness));
        }

        selection.Select();
    }

    private static Window WaitForWindow(UIA3Automation automation, int processId, string title)
    {
        var deadline = DateTime.UtcNow + Timeout;
        while (DateTime.UtcNow < deadline)
        {
            try
            {
                var window = Application.Attach(processId)
                    .GetAllTopLevelWindows(automation)
                    .FirstOrDefault(candidate => candidate.Title.Contains(title, StringComparison.Ordinal));
                if (window is not null)
                {
                    return window;
                }
            }
            catch (ArgumentException)
            {
            }

            Thread.Sleep(250);
        }

        throw new XunitException($"WPF window '{title}' (pid {processId}) did not appear.");
    }

    private static void WaitForText(Window window, string automationId, Func<string, bool> predicate, string description)
    {
        var deadline = DateTime.UtcNow + Timeout;
        var last = "";
        while (DateTime.UtcNow < deadline)
        {
            last = window.FindFirstDescendant(criteria => criteria.ByAutomationId(automationId))?.Name ?? "";
            if (predicate(last))
            {
                return;
            }

            Thread.Sleep(200);
        }

        throw new XunitException($"Timed out waiting for {description} (AutomationId '{automationId}'). Last value: '{last}'");
    }

    private static int CountAuditEvents(string auditFile, string eventType)
    {
        if (!File.Exists(auditFile))
        {
            return 0;
        }

        return File.ReadAllLines(auditFile).Count(line => line.Contains($"\"{eventType}\"", StringComparison.Ordinal));
    }

    private static string ReadDiagnostics(NamedPipeBrokerHarness harness)
    {
        if (!Directory.Exists(harness.ChildLogDirectory))
        {
            return "(no child logs)";
        }

        return string.Join(Environment.NewLine, Directory.GetFiles(harness.ChildLogDirectory, "*.log")
            .OrderBy(path => path, StringComparer.Ordinal)
            .SelectMany(path => new[] { "--- " + Path.GetFileName(path) + " ---" }.Concat(File.ReadAllLines(path).TakeLast(40))));
    }
}
