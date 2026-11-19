using Aether.Contracts.Models;
using Aether.Testing;
using FlaUI.Core.AutomationElements;
using FlaUI.UIA3;
using Xunit;
using Xunit.Sdk;

namespace Aether.Desktop.E2E.Tests;

/// <summary>
/// v1.5.1 reference-desktop proof for the AI Command Center / WebView2 demo preset. The test
/// drives the real WPF shell over named pipes, chooses the preset through UIA, runs the validated
/// plan and asserts that both WPF sample windows and the independent WebView2 chart appeared.
/// A missing WebView2 Runtime is a fallback, not a bridge PASS; physical R1 evidence still owns the
/// real-bridge assertion.
/// </summary>
[Trait("Category", "ReferenceDesktop")]
[Collection("Desktop GUI")]
public sealed class CommandCenterDesktopTests
{
    private static readonly TimeSpan ElementTimeout = TimeSpan.FromSeconds(20);
    private static readonly TimeSpan WindowTimeout = TimeSpan.FromSeconds(45);

    [Fact(Timeout = 360_000)]
    public async Task WebView2DemoPresetRunsStrictValidatedWorkspaceThroughTheShell()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        await using var harness = await NamedPipeBrokerHarness.StartAsync(
            "v151-command-center",
            useWindowsRegistry: true,
            wpfSamplesExe: DesktopExecutableLocator.ResolveWpfSampleApps(),
            webView2ChartExe: DesktopExecutableLocator.ResolveWebView2Chart());
        using var shell = ShellWindowSession.Launch(harness.EndpointFile);
        _ = shell.WaitForBackendModeContaining(Aether.Contracts.Broker.BrokerTransports.NamedPipe, TimeSpan.FromSeconds(45));

        var registration = shell.WaitForStatus(
            text => text.Contains("Registered with broker", StringComparison.Ordinal),
            ShellWindowSession.RegistrationTimeout,
            "broker registration");
        Assert.Contains("Registered with broker", registration, StringComparison.Ordinal);

        // Wait until the catalog is populated before clicking the preset; otherwise there are no rows
        // to select and the preset would silently apply to an empty set.
        _ = shell.WaitForStatus(
            text => text.Contains("Application catalog refreshed", StringComparison.Ordinal),
            ElementTimeout,
            "application catalog");
        shell.ClickWebView2DemoPreset();
        Assert.Equal("Strict", shell.ReadSelectionMode());

        shell.ClickRun();
        var status = shell.WaitForStatus(
            text => text.Contains("Workflow succeeded", StringComparison.Ordinal)
                    || text.Contains("Workflow denied", StringComparison.OrdinalIgnoreCase)
                    || text.Contains("Workflow failed", StringComparison.OrdinalIgnoreCase),
            ShellWindowSession.WorkflowTimeout,
            "demo-preset workflow result");
        Assert.True(
            status.Contains("Workflow succeeded", StringComparison.Ordinal),
            "The WebView2 demo preset did not complete successfully." + Environment.NewLine + status);

        // The WPF shell remains attached and the workflow's validated plan launched the WebView2
        // variant, not the default WPF chart. Physical desktop inspection is still required for the
        // real JS bridge; this test owns process/window presence through UIA.
        using var automation = new UIA3Automation();
        WaitForDesktopWindow(automation, "Synthetic Watchlist");
        WaitForDesktopWindow(automation, "Synthetic Risk View");
        WaitForDesktopWindow(automation, "Synthetic Bond Chart (WebView2)");
    }

    private static AutomationElement WaitForDesktopWindow(UIA3Automation automation, string title)
    {
        var deadline = DateTime.UtcNow + WindowTimeout;
        while (DateTime.UtcNow < deadline)
        {
            var windows = automation.GetDesktop().FindAllChildren(criteria => criteria.ByControlType(FlaUI.Core.Definitions.ControlType.Window));
            var window = windows.FirstOrDefault(candidate => candidate.Name.Contains(title, StringComparison.Ordinal));
            if (window is not null)
            {
                return window;
            }

            Thread.Sleep(250);
        }

        throw new XunitException($"Desktop window containing '{title}' did not appear within {WindowTimeout.TotalSeconds:0}s.");
    }
}
