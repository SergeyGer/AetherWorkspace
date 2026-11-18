using Aether.Contracts;
using Aether.Contracts.Models;
using Aether.Contracts.Json;
using Aether.Testing;
using Xunit;

namespace Aether.Contracts.Tests;

/// <summary>
/// v1.2 AW-057/AW-058 packaging and registry guards: the WebView2 host must be a declared,
/// separately launched app with a bounded context type, and the release/CI/packaging scripts must
/// ship it with explicit runtime/fallback instructions.
/// </summary>
public sealed class WebView2PackagingContractTests
{
    [Fact]
    public void WindowsRegistryDeclaresTheWebView2ChartAsASeparateBoundedApp()
    {
        var manifest = AetherJson.DeserializeStrict<AppManifestDocument>(
            File.ReadAllText(TestRepo.PathTo("samples", "registry", "apps.windows.json")));
        var app = Assert.Single(manifest.Apps, entry => entry.AppId == TestData.WebView2ChartAppId);

        Assert.True(app.Launchable);
        Assert.Equal("{WEBVIEW2_CHART_EXE}", app.Executable);
        Assert.Contains("webview2-chart", app.Arguments, StringComparer.Ordinal);
        Assert.Equal(["fdc3.instrument"], app.AllowedContextTypes);
        Assert.Contains("ViewChart", app.Intents, StringComparer.Ordinal);
        Assert.Equal(60, app.LaunchTimeoutSeconds);
    }

    [Fact]
    public void WindowsRegistryKeepsTheWebView2AppOutOfThePortableManifest()
    {
        var portable = AetherJson.DeserializeStrict<AppManifestDocument>(
            File.ReadAllText(TestRepo.PathTo("samples", "registry", "apps.json")));

        Assert.DoesNotContain(portable.Apps, entry => entry.AppId == TestData.WebView2ChartAppId);
    }

    [Fact]
    public void ReleaseWorkflowPublishesTheWebView2HostAndRuntimeFallbackIsDocumented()
    {
        var workflow = Read(".github", "workflows", "release.yml");
        Assert.Contains("src/Aether.SampleApps.WebView2", workflow, StringComparison.Ordinal);
        Assert.Contains("samples/webview2", workflow, StringComparison.Ordinal);

        var readme = Read("packaging", "windows", "README.txt");
        Assert.Contains("WebView2", readme, StringComparison.Ordinal);
        Assert.Contains("Evergreen Runtime", readme, StringComparison.Ordinal);
        Assert.Contains("fallback", readme, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("no remote scripts", readme, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void WindowsUiCiInstallsTheWebView2RuntimeBeforeInteropTests()
    {
        var workflow = Read(".github", "workflows", "ci.yml");
        Assert.Contains("Microsoft Edge WebView2 Runtime", workflow, StringComparison.Ordinal);
        Assert.Contains("MicrosoftEdgeWebView2Setup.exe", workflow, StringComparison.Ordinal);
        Assert.Contains("Category=ReferenceDesktop", workflow, StringComparison.Ordinal);
    }

    [Fact]
    public void LocalLaunchersPassTheWebView2ChartExecutableToken()
    {
        var start = Read("packaging", "windows", "Start-AetherWorkspace.ps1");
        var selfTest = Read("packaging", "windows", "Test-AetherWorkflow.ps1");

        Assert.Contains("WEBVIEW2_CHART_EXE=", start, StringComparison.Ordinal);
        Assert.Contains("Aether.SampleApps.WebView2.exe", start, StringComparison.Ordinal);
        Assert.Contains("WEBVIEW2_CHART_EXE=", selfTest, StringComparison.Ordinal);
        Assert.Contains("Aether.SampleApps.WebView2.exe", selfTest, StringComparison.Ordinal);
    }

    private static string Read(params string[] parts) => File.ReadAllText(TestRepo.PathTo(parts));
}
