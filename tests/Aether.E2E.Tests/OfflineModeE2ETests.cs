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
