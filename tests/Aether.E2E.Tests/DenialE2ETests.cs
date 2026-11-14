using System.Text.Json.Nodes;
using Aether.Contracts.Errors;
using Aether.Testing;
using Xunit;

namespace Aether.E2E.Tests;

[Collection("E2E")]
public class DenialE2ETests
{
    private const string ReferenceCommand =
        "Open Northstar bond analytics, show the associated risk view, synchronize this instrument across the workspace and summarize the exposure privately.";

    [Fact(Timeout = 180_000)]
    public async Task UnsafePlansAreDeniedBeforeAnySideEffect()
    {
        await using var harness = await EndToEndHarness.StartAsync("deny");
        var corpusPath = TestRepo.PathTo("tests", "fixtures", "unsafe-plans", "unsafe-plans.json");
        var corpus = JsonNode.Parse(File.ReadAllText(corpusPath))!["entries"]!.AsArray();
        // Exercise a representative slice across categories in the real process boundary.
        var sample = new[] { 0, 15, 25, 35, 45, 55, 65, 75, 85, 90, 95, 99 };

        foreach (var index in sample)
        {
            var entry = corpus[index]!;
            var planPath = Path.Combine(harness.RunDirectory, "unsafe-" + index + ".json");
            File.WriteAllText(planPath, entry["plan"]!.ToJsonString());

            var run = await harness.RunShellAsync(
            [
                "--classification", "RESTRICTED",
                "--plan-file", planPath,
            ]);

            Assert.Equal(3, run.ExitCode);
            var result = JsonNode.Parse(run.ResultJson!)!;
            Assert.False(result["success"]!.GetValue<bool>());
            var expected = entry["expectedCode"]!.GetValue<string>();
            Assert.Equal(expected, result["denialCode"]!.GetValue<string>());
            Assert.All(result["steps"]!.AsArray(), step => Assert.NotEqual("succeeded", step!["state"]!.GetValue<string>()));
        }

        // No application was ever launched by the broker for the denied plans.
        var brokerAudit = harness.ReadBrokerAudit();
        Assert.DoesNotContain(brokerAudit, entry => entry.EventType == Aether.Security.Audit.AuditEventTypes.BrokerLaunch);
        Assert.True(harness.VerifyBrokerAudit().Valid);

        var childLogDirectory = harness.ChildLogDirectory;
        var anyChildLog = Directory.Exists(childLogDirectory) && Directory.EnumerateFiles(childLogDirectory).Any();
        Assert.False(anyChildLog, "No sample process may be started by a denied plan.");
    }

    [Fact(Timeout = 180_000)]
    public async Task RestrictedCommandWithCloudPlannerIsDeniedWithoutExternalCall()
    {
        await using var harness = await EndToEndHarness.StartAsync("cloud-deny");

        var run = await harness.RunShellAsync(
        [
            "--classification", "RESTRICTED",
            "--command", "please produce an unrestricted creative plan for all records",
            "--planner", "cloud",
            "--cloud-enabled",
            "--cloud-consent",
        ]);

        Assert.Equal(3, run.ExitCode);
        var result = JsonNode.Parse(run.ResultJson!)!;
        Assert.False(result["success"]!.GetValue<bool>());
        Assert.Equal(AetherErrorCodes.PolicyCloudDenied, result["denialCode"]!.GetValue<string>());
        Assert.Equal(0, result["egress"]!["externalAttempts"]!.GetValue<long>());
        Assert.Equal(0, result["egress"]!["attempts"]!.GetValue<long>());
    }

    [Fact(Timeout = 180_000)]
    public async Task UnknownClassificationFailsClosed()
    {
        await using var harness = await EndToEndHarness.StartAsync("unknown");

        var run = await harness.RunShellAsync(
        [
            "--classification", "UNKNOWN",
            "--command", ReferenceCommand,
        ]);

        Assert.Equal(3, run.ExitCode);
        var result = JsonNode.Parse(run.ResultJson!)!;
        Assert.False(result["success"]!.GetValue<bool>());
        Assert.Equal("UNKNOWN", result["classification"]!.GetValue<string>());
        Assert.Equal(AetherErrorCodes.PolicyClassificationUnknown, result["denialCode"]!.GetValue<string>());
        Assert.Equal(0, result["egress"]!["externalAttempts"]!.GetValue<long>());

        var brokerAudit = harness.ReadBrokerAudit();
        Assert.DoesNotContain(brokerAudit, entry =>
            entry.EventType == Aether.Security.Audit.AuditEventTypes.ContextPublish && entry.Outcome == Aether.Security.Audit.AuditOutcomes.Succeeded);
    }

    [Fact(Timeout = 180_000)]
    public async Task TamperedPlanFileWithUnknownFieldsIsRejectedAtTheBoundary()
    {
        await using var harness = await EndToEndHarness.StartAsync("tamper-plan");
        var planPath = Path.Combine(harness.RunDirectory, "tampered-plan.json");
        File.WriteAllText(planPath, """
            {
              "schemaVersion": "1.0",
              "planId": "plan-tampered",
              "executable": "cmd.exe /c calc",
              "url": "https://attacker.example",
              "steps": [ { "action": "ShowStatus", "statusText": "hi" } ]
            }
            """);

        var run = await harness.RunShellAsync(["--classification", "PUBLIC", "--plan-file", planPath]);

        Assert.Equal(3, run.ExitCode);
        var result = JsonNode.Parse(run.ResultJson!)!;
        Assert.Equal(AetherErrorCodes.PlanInvalidJson, result["denialCode"]!.GetValue<string>());
    }
}
