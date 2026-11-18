using System.Diagnostics;
using Aether.Broker.Client;
using Aether.Contracts;
using Aether.Contracts.Models;
using Aether.Testing;
using FlaUI.Core.AutomationElements;
using FlaUI.UIA3;
using Xunit;
using Xunit.Abstractions;
using Xunit.Sdk;
using Application = FlaUI.Core.Application;

namespace Aether.Desktop.E2E.Tests;

/// <summary>
/// v1.2 AW-057/AW-058 Windows reference-desktop proof: the independently launched WebView2
/// chart host is a real separate process, receives broker-approved typed contexts through
/// .NET→JavaScript, renders each demo ticker locally and acknowledges the render back through
/// the host WPF automation surface. A missing WebView2 Runtime is a fallback failure, not a
/// silent skip: the physical/CI profile must have the Evergreen runtime installed.
/// </summary>
[Trait("Category", "ReferenceDesktop")]
[Trait("Category", "WebView2")]
public sealed class WebView2ChartBridgeTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(45);
    private static readonly (string ContextRef, DataClassification Classification, string Ticker, string TraceId)[] Contexts =
    [
        ("synthetic.instrument.northstar", DataClassification.Restricted, "NRST", "trace-webview2-nrst"),
        ("synthetic.instrument.vega", DataClassification.Public, "VEGA", "trace-webview2-vega"),
        ("synthetic.instrument.orin", DataClassification.Internal, "ORIN", "trace-webview2-orin"),
    ];

    private readonly ITestOutputHelper _output;

    public WebView2ChartBridgeTests(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact(Timeout = 360_000)]
    public async Task WebView2ChartHostReceivesAllTypedContextsThroughTheJsBridge()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var chartExecutable = DesktopExecutableLocator.ResolveWebView2Chart();
        var wpfSamples = DesktopExecutableLocator.ResolveWpfSampleApps();
        await using var harness = await NamedPipeBrokerHarness.StartAsync(
            "webview2-chart-bridge",
            useWindowsRegistry: true,
            wpfSamplesExe: wpfSamples,
            webView2ChartExe: chartExecutable);
        await using var client = await harness.CreateOperatorClientAsync();

        var launch = await client.LaunchAppAsync(TestData.WebView2ChartAppId, TraceIdGenerator.NewTraceId());
        Assert.True(launch.Accepted, $"WebView2 chart launch was denied: {launch.DenialCode} {launch.DenialMessage}");
        Assert.True(launch.ProcessId > 0);

        using var automation = new UIA3Automation();
        var window = WaitForWindow(automation, launch.ProcessId, "Synthetic Bond Chart (WebView2)", harness);

        WaitForText(
            window,
            "WebView2ChartProcessBadge",
            text => text.Contains("PID ", StringComparison.Ordinal)
                    && text.Contains(TestData.WebView2ChartAppId, StringComparison.Ordinal)
                    && text.Contains("gRPC", StringComparison.Ordinal),
            "real WebView2 host process badge",
            harness);

        AssertFallbackNotActive(window, harness);

        WaitForText(
            window,
            "WebView2BridgeStatus",
            text => text.Contains("JS bridge ready", StringComparison.Ordinal),
            "JavaScript bridge ready",
            harness);

        foreach (var context in Contexts)
        {
            var published = await client.PublishContextAsync(context.ContextRef, context.Classification, context.TraceId);
            Assert.True(published.Accepted, $"Context publish {context.ContextRef} was denied: {published.DenialCode}");

            WaitForText(
                window,
                "WebView2BridgeStatus",
                text => text.Contains("ack " + context.Ticker, StringComparison.Ordinal)
                        && text.Contains("trace " + context.TraceId, StringComparison.Ordinal)
                        && text.Contains("JS points ", StringComparison.Ordinal),
                $"JS render acknowledgement for {context.Ticker}",
                harness);
            _output.WriteLine($"{context.Ticker}: host/JS bridge acknowledged the approved typed context.");
        }

        await Task.Delay(TimeSpan.FromSeconds(1));
        var auditFile = harness.BrokerAuditFile;
        var publishCount = File.Exists(auditFile)
            ? File.ReadAllLines(auditFile).Count(line => line.Contains("\"context.publish\"", StringComparison.Ordinal))
            : 0;
        Assert.Equal(Contexts.Length, publishCount);

        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        try
        {
            Process.GetProcessById(launch.ProcessId);
        }
        catch (ArgumentException)
        {
            Assert.Fail("The independent WebView2 host process exited unexpectedly.");
        }
    }

    private static void AssertFallbackNotActive(Window window, NamedPipeBrokerHarness harness)
    {
        var fallback = window.FindFirstDescendant(criteria => criteria.ByAutomationId("WebView2FallbackStatus"));
        if (fallback is not null && !fallback.IsOffscreen)
        {
            Assert.Fail(
                "WebView2 Runtime is unavailable, so the renderer fell back instead of proving the bridge. "
                + "Install the Evergreen Microsoft Edge WebView2 Runtime on this machine."
                + Environment.NewLine
                + fallback.Name
                + Environment.NewLine
                + ReadDiagnostics(harness, "webview2-fallback"));
        }
    }

    private static Window WaitForWindow(UIA3Automation automation, int processId, string title, NamedPipeBrokerHarness harness)
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

        throw new XunitException(
            $"WebView2 window '{title}' (pid {processId}) did not appear within {Timeout.TotalSeconds:0}s."
            + Environment.NewLine
            + ReadDiagnostics(harness, "webview2-window"));
    }

    private static void WaitForText(
        Window window,
        string automationId,
        Func<string, bool> predicate,
        string description,
        NamedPipeBrokerHarness harness)
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

            Thread.Sleep(250);
        }

        throw new XunitException(
            $"Timed out waiting for {description} (AutomationId '{automationId}'). Last value: '{last}'."
            + Environment.NewLine
            + ReadDiagnostics(harness, automationId));
    }

    private static string ReadDiagnostics(NamedPipeBrokerHarness harness, string label)
    {
        var lines = new List<string> { "--- " + label + " diagnostics ---" };
        if (Directory.Exists(harness.ChildLogDirectory))
        {
            foreach (var log in Directory.GetFiles(harness.ChildLogDirectory, "*.log").OrderBy(path => path, StringComparer.Ordinal))
            {
                lines.Add($"--- {Path.GetFileName(log)} ---");
                lines.AddRange(File.ReadAllLines(log).TakeLast(40));
            }
        }

        lines.Add($"--- broker stderr tail ({harness.BrokerStdErrFile}) ---");
        if (File.Exists(harness.BrokerStdErrFile))
        {
            lines.AddRange(File.ReadAllLines(harness.BrokerStdErrFile).TakeLast(30));
        }

        return string.Join(Environment.NewLine, lines);
    }
}
