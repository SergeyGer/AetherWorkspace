using System.Diagnostics;
using Aether.Broker.Client;
using Aether.Contracts;
using Aether.Contracts.Models;
using Aether.Testing;
using Application = FlaUI.Core.Application;
using FlaUI.UIA3;
using Xunit;
using Xunit.Abstractions;

namespace Aether.Desktop.E2E.Tests;

/// <summary>
/// Reproduces the clean-machine desktop scenario that no other test covered: the broker launches
/// the three real WPF sample applications (from <c>apps.windows.json</c>) and publishes a typed
/// context; the windows must remain open and receive the context instead of disappearing.
/// Windows-only; the headless E2E tests cannot catch WPF process/window lifetime defects.
/// </summary>
[Trait("Category", "ReferenceDesktop")]
public sealed class WpfSampleAppPersistenceTests
{
    private readonly ITestOutputHelper _output;

    public WpfSampleAppPersistenceTests(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact(Timeout = 240_000)]
    public async Task RealWpfSampleAppsStayOpenAndReceiveContext()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var samplesExecutable = DesktopExecutableLocator.ResolveWpfSampleApps();
        await using var harness = await NamedPipeBrokerHarness.StartAsync(
            "wpf-sample-persistence",
            useWindowsRegistry: true,
            wpfSamplesExe: samplesExecutable);
        await using var client = await harness.CreateOperatorClientAsync();

        var processes = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var appId in TestData.SampleAppIds)
        {
            var launch = await client.LaunchAppAsync(appId, TraceIdGenerator.NewTraceId());
            Assert.True(launch.Accepted, $"Launch of {appId} was denied: {launch.DenialCode} {launch.DenialMessage}");
            Assert.True(launch.ProcessId > 0);
            processes[appId] = launch.ProcessId;
        }

        using var automation = new UIA3Automation();
        foreach (var (appId, processId) in processes)
        {
            WaitForTopLevelWindow(automation, appId, processId, TimeSpan.FromSeconds(25), harness);
        }

        var published = await client.PublishContextAsync(
            "synthetic.instrument.northstar",
            DataClassification.Restricted,
            TraceIdGenerator.NewTraceId());
        Assert.True(published.Accepted, "The typed context publish was denied: " + published.DenialCode);

        await Task.Delay(TimeSpan.FromSeconds(6));

        var failures = new List<string>();
        foreach (var (appId, processId) in processes)
        {
            Process process;
            try
            {
                process = Process.GetProcessById(processId);
            }
            catch (ArgumentException)
            {
                failures.Add($"{appId}: process {processId} exited");
                continue;
            }

            if (process.HasExited)
            {
                failures.Add($"{appId}: process {processId} exited with code {process.ExitCode}");
                continue;
            }

            var windows = Application.Attach(processId).GetAllTopLevelWindows(automation);
            if (windows.Length == 0)
            {
                failures.Add($"{appId}: process {processId} is alive but has no top-level window");
            }

            _output.WriteLine($"{appId}: process {processId} alive, windows={windows.Length}");
        }

        if (failures.Count > 0)
        {
            Assert.Fail(string.Join(Environment.NewLine, failures) + Environment.NewLine + ReadDiagnostics(harness, processes));
        }
    }

    private static void WaitForTopLevelWindow(
        UIA3Automation automation,
        string appId,
        int processId,
        TimeSpan timeout,
        NamedPipeBrokerHarness harness)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            try
            {
                var application = Application.Attach(processId);
                if (application.GetAllTopLevelWindows(automation).Length > 0)
                {
                    return;
                }
            }
            catch (ArgumentException)
            {
                throw new Xunit.Sdk.XunitException(
                    $"{appId}: process {processId} exited before showing a window." + Environment.NewLine
                    + ReadDiagnostics(harness, new Dictionary<string, int> { [appId] = processId }));
            }

            Thread.Sleep(250);
        }

        throw new Xunit.Sdk.XunitException(
            $"{appId}: no top-level window appeared within {timeout.TotalSeconds:0}s."
            + Environment.NewLine
            + ReadDiagnostics(harness, new Dictionary<string, int> { [appId] = processId }));
    }

    private static string ReadDiagnostics(NamedPipeBrokerHarness harness, IReadOnlyDictionary<string, int> processes)
    {
        var lines = new List<string> { "--- reference-desktop WPF sample diagnostics ---" };
        foreach (var (appId, processId) in processes)
        {
            try
            {
                var process = Process.GetProcessById(processId);
                lines.Add($"{appId}: pid={processId} exited={process.HasExited} mainWindowHandle={process.MainWindowHandle}");
            }
            catch (ArgumentException)
            {
                lines.Add($"{appId}: pid={processId} not running");
            }
        }

        if (Directory.Exists(harness.ChildLogDirectory))
        {
            foreach (var log in Directory.GetFiles(harness.ChildLogDirectory, "*.log").OrderBy(path => path, StringComparer.Ordinal))
            {
                lines.Add($"--- {Path.GetFileName(log)} ---");
                lines.AddRange(File.ReadAllLines(log).TakeLast(60));
            }
        }
        else
        {
            lines.Add($"child log directory missing: {harness.ChildLogDirectory}");
        }

        lines.Add($"--- broker stderr tail ({harness.BrokerStdErrFile}) ---");
        if (File.Exists(harness.BrokerStdErrFile))
        {
            lines.AddRange(File.ReadAllLines(harness.BrokerStdErrFile).TakeLast(40));
        }

        return string.Join(Environment.NewLine, lines);
    }
}
