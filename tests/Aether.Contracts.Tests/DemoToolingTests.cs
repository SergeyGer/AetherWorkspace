using Aether.Testing;
using Xunit;

namespace Aether.Contracts.Tests;

/// <summary>Guards the local Windows demo production pipeline contracts (AW-055/AW-056).</summary>
public class DemoToolingTests
{
    [Fact]
    public void DemoScriptsAndScenesExist()
    {
        foreach (var file in new[]
                 {
                     "Prepare-Demo.ps1", "Obs-Control.ps1", "Record-Demo.ps1",
                     "Build-Video.ps1", "Validate-Video.ps1", "demo-scenes.json", "obs-profile.example.json",
                 })
        {
            Assert.True(File.Exists(TestRepo.PathTo("tools", "demo", file)), $"tools/demo/{file} is missing.");
        }

        var scenes = File.ReadAllText(TestRepo.PathTo("tools", "demo", "demo-scenes.json"));
        Assert.Contains("TrackB", scenes, StringComparison.Ordinal);
        Assert.Contains("TrackA", scenes, StringComparison.Ordinal);
        Assert.Contains("workflow-succeeded", scenes, StringComparison.Ordinal);

        var obs = File.ReadAllText(TestRepo.PathTo("tools", "demo", "obs-profile.example.json"));
        Assert.Contains("127.0.0.1", obs, StringComparison.Ordinal);
        Assert.DoesNotContain("password\":", obs, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void CaptureHarnessEnforcesTrustPreflightAndLocalObs()
    {
        var prepare = File.ReadAllText(TestRepo.PathTo("tools", "demo", "Prepare-Demo.ps1"));
        var record = File.ReadAllText(TestRepo.PathTo("tools", "demo", "Record-Demo.ps1"));
        var obs = File.ReadAllText(TestRepo.PathTo("tools", "demo", "Obs-Control.ps1"));
        var capture = File.ReadAllText(TestRepo.PathTo("tests", "Aether.Desktop.E2E.Tests", "DesktopGuiE2ETests.cs"));

        Assert.Contains("SKIPPED_UNAPPROVED_SHA", prepare, StringComparison.Ordinal);
        Assert.Contains("worktree add --detach", prepare, StringComparison.Ordinal);
        Assert.Contains("SKIPPED_ENVIRONMENT", prepare, StringComparison.Ordinal);
        Assert.Contains("AETHER_OBS_WEBSOCKET_PASSWORD", obs, StringComparison.Ordinal);
        Assert.Contains("Connect-Obs", record, StringComparison.Ordinal);
        Assert.Contains("Category=DemoCapture", record, StringComparison.Ordinal);
        Assert.Contains("DemoCapture", capture, StringComparison.Ordinal);
        Assert.Contains("AETHER_DEMO_EVENTS_OUT", capture, StringComparison.Ordinal);
    }

    [Fact]
    public void PhysicalGateRunnerChainsTrustedMeasurementsAndCapture()
    {
        var runner = File.ReadAllText(TestRepo.PathTo("tools", "windows-test", "Run-PhysicalReleaseGate.ps1"));

        Assert.Contains("SKIPPED_UNAPPROVED_SHA", runner, StringComparison.Ordinal);
        Assert.Contains("Run-AetherTests.ps1", runner, StringComparison.Ordinal);
        Assert.Contains("Export-WtLedger.ps1", runner, StringComparison.Ordinal);
        Assert.Contains("Measure-UiResponsiveness.ps1", runner, StringComparison.Ordinal);
        Assert.Contains("Prepare-Demo.ps1", runner, StringComparison.Ordinal);
        Assert.Contains("Record-Demo.ps1", runner, StringComparison.Ordinal);
        Assert.Contains("Build-Video.ps1", runner, StringComparison.Ordinal);
        Assert.Contains("Validate-Video.ps1", runner, StringComparison.Ordinal);
        Assert.Contains("gate-summary.json", runner, StringComparison.Ordinal);
        Assert.Contains("physical release gate", runner, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void VideoPipelineValidatesCodecDurationAndPrivacy()
    {
        var build = File.ReadAllText(TestRepo.PathTo("tools", "demo", "Build-Video.ps1"));
        var validate = File.ReadAllText(TestRepo.PathTo("tools", "demo", "Validate-Video.ps1"));
        var runbook = File.ReadAllText(TestRepo.PathTo("docs", "demo", "recording-runbook.md"));

        Assert.Contains("demo-hero.mp4", build, StringComparison.Ordinal);
        Assert.Contains("demo-preview.gif", build, StringComparison.Ordinal);
        Assert.Contains("captions.srt", build, StringComparison.Ordinal);
        Assert.Contains("yuv420p", validate, StringComparison.Ordinal);
        Assert.Contains("80-95", validate, StringComparison.Ordinal);
        Assert.Contains("blackdetect", validate, StringComparison.Ordinal);
        Assert.Contains("humanReviewRequired", validate, StringComparison.Ordinal);
        Assert.Contains("AETHER_OBS_WEBSOCKET_PASSWORD", runbook, StringComparison.Ordinal);
        Assert.Contains("Track B", runbook, StringComparison.Ordinal);
    }
}
