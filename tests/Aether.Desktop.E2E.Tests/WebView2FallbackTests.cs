using Aether.Broker.Client;
using Aether.Contracts;
using Aether.Testing;
using FlaUI.Core.AutomationElements;
using FlaUI.UIA3;
using Xunit;
using Xunit.Abstractions;
using Xunit.Sdk;
using Application = FlaUI.Core.Application;

namespace Aether.Desktop.E2E.Tests;

/// <summary>
/// v1.2 AW-057 fallback proof: when the WebView2 Runtime is unavailable (forced here with
/// <c>AETHER_WEBVIEW2_FORCE_FALLBACK=1</c> so CI can test the path without uninstalling the
/// runtime), the independent host stays alive, keeps its broker session, and shows a readable
/// dependency/fallback message instead of crashing.
/// </summary>
[Trait("Category", "ReferenceDesktop")]
[Trait("Category", "WebView2")]
public sealed class WebView2FallbackTests
{
    private readonly ITestOutputHelper _output;

    public WebView2FallbackTests(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact(Timeout = 240_000)]
    public async Task MissingRuntimeShowsReadableFallbackAndKeepsTheHostAlive()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var previous = Environment.GetEnvironmentVariable("AETHER_WEBVIEW2_FORCE_FALLBACK");
        Environment.SetEnvironmentVariable("AETHER_WEBVIEW2_FORCE_FALLBACK", "1");
        try
        {
            await using var harness = await NamedPipeBrokerHarness.StartAsync(
                "webview2-fallback",
                useWindowsRegistry: true,
                wpfSamplesExe: DesktopExecutableLocator.ResolveWpfSampleApps(),
                webView2ChartExe: DesktopExecutableLocator.ResolveWebView2Chart());
            await using var client = await harness.CreateOperatorClientAsync();

            var launch = await client.LaunchAppAsync(TestData.WebView2ChartAppId, TraceIdGenerator.NewTraceId());
            Assert.True(launch.Accepted, $"WebView2 chart launch was denied: {launch.DenialCode} {launch.DenialMessage}");

            using var automation = new UIA3Automation();
            var window = WaitForWindow(automation, launch.ProcessId);
            var fallback = WaitForFallbackText(window, out var text);
            Assert.True(fallback, "The forced WebView2 fallback was not visible.");
            Assert.Contains("WebView2 Runtime", text, StringComparison.Ordinal);
            Assert.Contains("install", text, StringComparison.OrdinalIgnoreCase);

            var process = System.Diagnostics.Process.GetProcessById(launch.ProcessId);
            Assert.False(process.HasExited, "The WebView2 host process must stay alive in fallback mode.");
            _output.WriteLine("Fallback path verified: host alive, readable runtime guidance, broker session kept.");
        }
        finally
        {
            Environment.SetEnvironmentVariable("AETHER_WEBVIEW2_FORCE_FALLBACK", previous);
        }
    }

    private static Window WaitForWindow(UIA3Automation automation, int processId)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(35);
        while (DateTime.UtcNow < deadline)
        {
            try
            {
                var window = Application.Attach(processId)
                    .GetAllTopLevelWindows(automation)
                    .FirstOrDefault(candidate => candidate.Title.Contains("Synthetic Bond Chart (WebView2)", StringComparison.Ordinal));
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

        throw new XunitException($"WebView2 fallback window (pid {processId}) did not appear.");
    }

    private static bool WaitForFallbackText(Window window, out string text)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(25);
        text = "";
        while (DateTime.UtcNow < deadline)
        {
            var element = window.FindFirstDescendant(criteria => criteria.ByAutomationId("WebView2FallbackStatus"));
            if (element is not null && !element.IsOffscreen)
            {
                text = element.Name;
                return text.Contains("WebView2 Runtime", StringComparison.Ordinal);
            }

            Thread.Sleep(200);
        }

        return false;
    }
}
