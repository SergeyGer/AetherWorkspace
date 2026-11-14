using System.Text.Json.Nodes;
using Aether.Contracts.Errors;
using Aether.Testing;
using Xunit;

namespace Aether.E2E.Tests;

[Collection("E2E")]
public class OfflineModeE2ETests
{
    private const string ReferenceCommand =
        "Open Northstar bond analytics, show the associated risk view, synchronize this instrument across the workspace and summarize the exposure privately.";

    [Fact(Timeout = 180_000)]
    public async Task OfflineModeRunsDeterministicWorkflowAndReportsProviderHealth()
    {
        await using var harness = await EndToEndHarness.StartAsync("offline-mode");

        var run = await harness.RunShellAsync(
        [
            "--classification", "RESTRICTED",
            "--command", ReferenceCommand,
            "--offline",
        ]);

        Assert.Equal(0, run.ExitCode);
        var result = JsonNode.Parse(run.ResultJson!)!;
        Assert.True(result["success"]!.GetValue<bool>());
        Assert.Equal("offline", result["providers"]!["mode"]!.GetValue<string>());
        Assert.True(result["providers"]!["deterministicPlanner"]!.GetValue<bool>());
        Assert.True(result["providers"]!["localDeterministicSummary"]!.GetValue<bool>());
        Assert.False(result["providers"]!["cloudEnabled"]!.GetValue<bool>());
        Assert.Equal(0, result["egress"]!["externalAttempts"]!.GetValue<long>());
        Assert.True(result["audit"]!["chainValid"]!.GetValue<bool>());
    }

    [Fact(Timeout = 60_000)]
    public async Task OfflineModeRejectsCloudPlannerAsUsageError()
    {
        await using var harness = await EndToEndHarness.StartAsync("offline-cloud");

        var run = await harness.RunShellAsync(
        [
            "--classification", "RESTRICTED",
            "--command", ReferenceCommand,
            "--offline",
            "--planner", "cloud",
        ]);

        Assert.Equal(2, run.ExitCode);
        Assert.Contains("offline cannot be combined", run.StdErr, StringComparison.OrdinalIgnoreCase);
        Assert.Null(run.ResultJson);
    }

    [Fact(Timeout = 180_000)]
    public async Task ConfiguredCloudEndpointIsReportedWithoutNetworkCallInDeterministicMode()
    {
        await using var harness = await EndToEndHarness.StartAsync("provider-health");

        var run = await harness.RunShellAsync(
        [
            "--classification", "RESTRICTED",
            "--command", ReferenceCommand,
        ], environment: new Dictionary<string, string>
        {
            ["AETHER_CLOUD_ENDPOINT"] = "http://127.0.0.1:9/v1/chat/completions",
        });

        Assert.Equal(0, run.ExitCode);
        var result = JsonNode.Parse(run.ResultJson!)!;
        Assert.True(result["success"]!.GetValue<bool>());
        Assert.True(result["providers"]!["cloudAdapterConfigured"]!.GetValue<bool>());
        Assert.Equal("local-private", result["providers"]!["mode"]!.GetValue<string>());
        Assert.Equal(0, result["egress"]!["externalAttempts"]!.GetValue<long>());
    }

    [Fact(Timeout = 180_000)]
    public async Task ShellAuditRotatesAndTheRetainedChainVerifies()
    {
        await using var harness = await EndToEndHarness.StartAsync("shell-audit-rotate");
        var common = new[]
        {
            "--classification", "RESTRICTED",
            "--command", ReferenceCommand,
            "--audit-file", harness.ShellAuditFile,
            "--audit-max-bytes", "4096",
            "--audit-files", "3",
        };

        var first = await harness.RunShellAsync(common);
        Assert.Equal(0, first.ExitCode);
        var firstResult = JsonNode.Parse(first.ResultJson!)!;
        Assert.True(firstResult["audit"]!["chainValid"]!.GetValue<bool>());
        Assert.True(firstResult["audit"]!["fileChainValid"]!.GetValue<bool>());

        var second = await harness.RunShellAsync(common);
        Assert.Equal(0, second.ExitCode);
        var secondResult = JsonNode.Parse(second.ResultJson!)!;
        Assert.True(secondResult["success"]!.GetValue<bool>());
        Assert.True(secondResult["audit"]!["fileChainValid"]!.GetValue<bool>());
        Assert.True(secondResult["audit"]!["rotatedFiles"]!.GetValue<int>() >= 1,
            "two workflow runs should exceed the 4 KiB rotation threshold: " + second.ResultJson);
    }

    [Fact(Timeout = 180_000)]
    public async Task MissingLocalModelYieldsSafeErrorWithoutCloudFallback()
    {
        await using var harness = await EndToEndHarness.StartAsync("offline");

        var run = await harness.RunShellAsync(
        [
            "--classification", "RESTRICTED",
            "--command", ReferenceCommand,
            "--use-local-model",
        ]);

        Assert.Equal(3, run.ExitCode);
        var result = JsonNode.Parse(run.ResultJson!)!;
        Assert.False(result["success"]!.GetValue<bool>());
        Assert.Equal(AetherErrorCodes.AiProviderMissing, result["denialCode"]!.GetValue<string>());
        Assert.Equal(0, result["egress"]!["externalAttempts"]!.GetValue<long>());
        Assert.Null(result["summary"]);
    }

    [Fact(Timeout = 180_000)]
    public async Task DeterministicPrivatePathWorksWithoutModelOrCloudConfiguration()
    {
        await using var harness = await EndToEndHarness.StartAsync("private-local");

        var run = await harness.RunShellAsync(
        [
            "--classification", "RESTRICTED",
            "--command", ReferenceCommand,
        ]);

        Assert.Equal(0, run.ExitCode);
        var result = JsonNode.Parse(run.ResultJson!)!;
        Assert.True(result["success"]!.GetValue<bool>());
        Assert.Equal("local-deterministic", result["summary"]!["provider"]!.GetValue<string>());
        Assert.Equal(0, result["egress"]!["externalAttempts"]!.GetValue<long>());
        Assert.Equal(0, result["egress"]!["denied"]!.GetValue<long>());
    }
}
