using System.Text.Json.Nodes;
using Aether.Contracts;
using Aether.Testing;
using Xunit;

namespace Aether.E2E.Tests;

/// <summary>v1.3 AW-059: invalid per-capability provider configuration fails closed and never gets relabelled.</summary>
[Collection("E2E")]
public sealed class ProviderConfigurationE2ETests
{
    private const string ReferenceCommand =
        "Open Northstar bond analytics, show the associated risk view, synchronize this instrument across the workspace and summarize the exposure privately.";

    [Fact(Timeout = 180_000)]
    public async Task InvalidPlanningProviderFallsBackDeterministicallyAndReportsTheConfigurationError()
    {
        await using var harness = await EndToEndHarness.StartAsync("provider-invalid");

        var run = await harness.RunShellAsync(
        [
            "--classification", "RESTRICTED",
            "--command", ReferenceCommand,
            "--planner", "cloud",
            "--cloud-enabled",
            "--cloud-consent",
        ], environment: new Dictionary<string, string>
        {
            ["AETHER_PLANNER_PROVIDER"] = "some-unsupported-brand",
            ["AETHER_PLANNER_PROVIDER_EXTRA"] = "ignored",
        });

        if (run.ExitCode != 0) { Assert.Fail("exit=" + run.ExitCode + " stderr=" + run.StdErr + " json=" + run.ResultJson); }
        Assert.Equal(0, run.ExitCode);
        var result = JsonNode.Parse(run.ResultJson!)!;
        Assert.True(result["success"]!.GetValue<bool>());
        Assert.Equal("deterministic-grammar", result["planner"]!.GetValue<string>());
        Assert.False(result["usedModel"]!.GetValue<bool>());
        var providers = result["providers"]!;
        Assert.Equal("some-unsupported-brand", providers["plannerProvider"]!.GetValue<string>());
        Assert.Contains("Unsupported provider", providers["providerConfigurationError"]!.GetValue<string>(), StringComparison.Ordinal);
        Assert.Equal("provider.config_invalid", providers["routeReason"]!.GetValue<string>());
        Assert.Equal(0, result["egress"]!["externalAttempts"]!.GetValue<long>());
    }

    [Fact(Timeout = 180_000)]
    public async Task PerCapabilityProviderHealthIsReportedWithoutNetworkCalls()
    {
        await using var harness = await EndToEndHarness.StartAsync("provider-health-v13");

        var run = await harness.RunShellAsync(
        [
            "--classification", "RESTRICTED",
            "--command", ReferenceCommand,
        ], environment: new Dictionary<string, string>
        {
            ["AETHER_PLANNER_PROVIDER"] = "deterministic",
            ["AETHER_SUMMARY_PROVIDER"] = "local",
            ["AETHER_LOCAL_MODEL_ENDPOINT"] = "http://127.0.0.1:9/v1/chat/completions",
            ["AETHER_LOCAL_MODEL"] = "qwen2.5:7b",
        });

        Assert.Equal(0, run.ExitCode);
        var result = JsonNode.Parse(run.ResultJson!)!;
        var providers = result["providers"]!;
        Assert.Equal("deterministic", providers["plannerProvider"]!.GetValue<string>());
        Assert.Equal("local", providers["summaryProvider"]!.GetValue<string>());
        Assert.Null(providers["providerConfigurationError"]);
        Assert.Equal(0, result["egress"]!["externalAttempts"]!.GetValue<long>());
    }
}
