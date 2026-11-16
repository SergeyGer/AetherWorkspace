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
        Assert.Contains("Aether.BrokerApp.exe", workflow, StringComparison.Ordinal);
        Assert.Contains("headless-binaries.zip", workflow, StringComparison.Ordinal);
    }

    [Fact]
    public void PackagedLauncherContainsBrokerHandshakeAndDesktopWiring()
    {
        var launcher = Read("packaging", "windows", "Start-AetherWorkspace.ps1");

        Assert.Contains("Aether.BrokerApp.exe", launcher, StringComparison.Ordinal);
        Assert.Contains("Aether.Shell.exe", launcher, StringComparison.Ordinal);
        Assert.Contains("Aether.SampleApps.exe", launcher, StringComparison.Ordinal);
        Assert.Contains("WPF_SAMPLES_EXE=", launcher, StringComparison.Ordinal);
        Assert.Contains("--transport", launcher, StringComparison.Ordinal);
        Assert.Contains("--endpoint-file", launcher, StringComparison.Ordinal);
        Assert.Contains("--layout-store", launcher, StringComparison.Ordinal);
        Assert.Contains("apps.windows.json", launcher, StringComparison.Ordinal);
        Assert.Contains("AETHER_ENDPOINT_FILE", launcher, StringComparison.Ordinal);
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
