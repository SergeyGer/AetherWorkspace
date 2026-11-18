using Aether.Testing;
using Xunit;

namespace Aether.Contracts.Tests;

/// <summary>
/// Guards the v1.0.1 packaging fix (G-01): the release pipeline must build a Windows desktop
/// bundle and the packaged launcher must stay complete enough for a clean-machine run.
/// </summary>
public class ReleasePackagingTests
{
    [Fact]
    public void ReleaseWorkflowBuildsWindowsDesktopBundleBeforePublishing()
    {
        var workflow = Read(".github", "workflows", "release.yml");

        Assert.Contains("windows-desktop:", workflow, StringComparison.Ordinal);
        Assert.Contains("windows-x64-desktop.zip", workflow, StringComparison.Ordinal);
        Assert.Contains("needs: [portable, windows-desktop]", workflow, StringComparison.Ordinal);
        Assert.Contains("Aether.Shell.exe", workflow, StringComparison.Ordinal);
        Assert.Contains("Aether.SampleApps.exe", workflow, StringComparison.Ordinal);
        Assert.Contains("Aether.SampleApps.WebView2.exe", workflow, StringComparison.Ordinal);
        Assert.Contains("Aether.BrokerApp.exe", workflow, StringComparison.Ordinal);
        Assert.Contains("headless-binaries.zip", workflow, StringComparison.Ordinal);
        Assert.Contains("shell-headless", workflow, StringComparison.Ordinal);
        Assert.Contains("Test-AetherWorkflow.ps1", workflow, StringComparison.Ordinal);
        Assert.Contains("Collect-Diagnostics.ps1", workflow, StringComparison.Ordinal);
        Assert.Contains("packaging/windows/*.ps1", workflow, StringComparison.Ordinal);
        Assert.Contains("Invoke-PackagedBundleSmoke.ps1", workflow, StringComparison.Ordinal);
        Assert.Contains("windows-desktop-smoke", workflow, StringComparison.Ordinal);
    }

    [Fact]
    public void PackagedBundleSmokeVerifiesFreshExtractionWorkflowAndCleanup()
    {
        var script = Read("tools", "windows-test", "Invoke-PackagedBundleSmoke.ps1");

        Assert.Contains("Expand-Archive", script, StringComparison.Ordinal);
        Assert.Contains("Test-AetherWorkflow.ps1", script, StringComparison.Ordinal);
        Assert.Contains("bundleSha256", script, StringComparison.Ordinal);
        Assert.Contains("workflowStatus", script, StringComparison.Ordinal);
        Assert.Contains("Aether.SampleApps", script, StringComparison.Ordinal);
        Assert.Contains("PACKAGED SMOKE PASS", script, StringComparison.Ordinal);
    }

    [Fact]
    public void PackagedLauncherContainsBrokerHandshakeAndDesktopWiring()
    {
        var launcher = Read("packaging", "windows", "Start-AetherWorkspace.ps1");

        Assert.Contains("Aether.BrokerApp.exe", launcher, StringComparison.Ordinal);
        Assert.Contains("Aether.Shell.exe", launcher, StringComparison.Ordinal);
        Assert.Contains("Aether.SampleApps.exe", launcher, StringComparison.Ordinal);
        Assert.Contains("Aether.SampleApps.WebView2.exe", launcher, StringComparison.Ordinal);
        Assert.Contains("WEBVIEW2_CHART_EXE=", launcher, StringComparison.Ordinal);
        Assert.Contains("WPF_SAMPLES_EXE=", launcher, StringComparison.Ordinal);
        Assert.Contains("--transport", launcher, StringComparison.Ordinal);
        Assert.Contains("--endpoint-file", launcher, StringComparison.Ordinal);
        Assert.Contains("--layout-store", launcher, StringComparison.Ordinal);
        Assert.Contains("apps.windows.json", launcher, StringComparison.Ordinal);
        Assert.Contains("AETHER_ENDPOINT_FILE", launcher, StringComparison.Ordinal);
        Assert.Contains("--audit-file", launcher, StringComparison.Ordinal);
        Assert.Contains("Collect-Diagnostics.ps1", launcher, StringComparison.Ordinal);
    }

    [Fact]
    public void PackagedSelfTestRunsHeadlessWorkflowAndExportsDiagnostics()
    {
        var script = Read("packaging", "windows", "Test-AetherWorkflow.ps1");

        Assert.Contains("Aether.Shell.Headless.exe", script, StringComparison.Ordinal);
        Assert.Contains("--export-diagnostics", script, StringComparison.Ordinal);
        Assert.Contains("apps.windows.json", script, StringComparison.Ordinal);
        Assert.Contains("WPF_SAMPLES_EXE=", script, StringComparison.Ordinal);
        Assert.Contains("workflow-result", script, StringComparison.Ordinal);
        Assert.Contains("Get-Process Aether.SampleApps", script, StringComparison.Ordinal);
    }

    [Fact]
    public void DiagnosticsCollectorExcludesEndpointToken()
    {
        var script = Read("packaging", "windows", "Collect-Diagnostics.ps1");

        Assert.Contains("endpoint.json", script, StringComparison.Ordinal);
        Assert.Contains("excluded", script, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Compress-Archive", script, StringComparison.Ordinal);
    }

    [Fact]
    public void PackagedReadmeStatesPrerequisitesAndUnsignedBoundary()
    {
        var readme = Read("packaging", "windows", "README.txt");

        Assert.Contains(".NET 10 Desktop Runtime", readme, StringComparison.Ordinal);
        Assert.Contains("UNSIGNED", readme, StringComparison.Ordinal);
        Assert.Contains("SmartScreen", readme, StringComparison.Ordinal);
        Assert.Contains("No telemetry", readme, StringComparison.Ordinal);
    }

    [Fact]
    public void ReleaseVerifierSupportsExplicitPartialDownloadMode()
    {
        var verifier = Read("tools", "release", "Verify-Release.ps1");

        Assert.Contains("AllowPartialDownload", verifier, StringComparison.Ordinal);
        Assert.Contains("PARTIAL VERIFY PASS", verifier, StringComparison.Ordinal);
        Assert.Contains("full verification requires all release assets", verifier, StringComparison.Ordinal);
    }

    private static string Read(params string[] parts) => File.ReadAllText(TestRepo.PathTo(parts));
}
