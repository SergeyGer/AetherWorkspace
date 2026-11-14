using System.Text.Json.Nodes;
using Aether.Testing;
using Xunit;

namespace Aether.E2E.Tests;

[Collection("E2E")]
public class MvpWorkflowE2ETests
{
    private const string ReferenceCommand =
        "Open Northstar bond analytics, show the associated risk view, synchronize this instrument across the workspace and summarize the exposure privately.";

    [Fact(Timeout = 180_000)]
    public async Task ReferenceWorkflowRunsAcrossIndependentProcessesWithPrivateSummary()
    {
        await using var harness = await EndToEndHarness.StartAsync("mvp");

        var run = await harness.RunShellAsync(
        [
            "--classification", "RESTRICTED",
            "--command", ReferenceCommand,
            "--audit-file", harness.ShellAuditFile,
        ]);

        Assert.Equal(0, run.ExitCode);
        Assert.NotNull(run.ResultJson);
        var result = JsonNode.Parse(run.ResultJson!)!;

        Assert.True(result["success"]!.GetValue<bool>(), run.ResultJson);
        Assert.Equal("succeeded", result["status"]!.GetValue<string>());
        Assert.Equal("RESTRICTED", result["classification"]!.GetValue<string>());

        // Trace id correlation across the whole run.
        var traceId = result["traceId"]!.GetValue<string>();
        Assert.True(Contracts.TraceContext.IsValidTraceId(traceId));

        // Six bounded steps, all deterministic and successful.
        var steps = result["steps"]!.AsArray();
        Assert.Equal(6, steps.Count);
        Assert.All(steps, step => Assert.Equal("succeeded", step!["state"]!.GetValue<string>()));
        Assert.Equal(3, steps.Count(step => step!["action"]!.GetValue<string>() == "LaunchApp"));

        // Context published to all three independent subscriber processes.
        var publish = steps.First(step => step!["action"]!.GetValue<string>() == "PublishContext")!;
        Assert.Equal(3, int.Parse(publish["artifacts"]!["deliveredCount"]!.GetValue<string>(), System.Globalization.CultureInfo.InvariantCulture));
        Assert.Equal(0, int.Parse(publish["artifacts"]!["droppedCount"]!.GetValue<string>(), System.Globalization.CultureInfo.InvariantCulture));
        Assert.Equal("RESTRICTED", publish["artifacts"]!["classification"]!.GetValue<string>());

        // Deterministic side-by-side top-level layout (no reparenting).
        var arrange = steps.First(step => step!["action"]!.GetValue<string>() == "ArrangeWindows")!;
        var layout = JsonNode.Parse(arrange["artifacts"]!["layoutJson"]!.GetValue<string>())!;
        Assert.Equal("side-by-side", layout["layout"]!.GetValue<string>());
        var placements = layout["placements"]!.AsArray();
        Assert.Equal(3, placements.Count);
        Assert.Equal(["sample.watchlist", "sample.risk", "sample.chart"], placements.Select(placement => placement!["appId"]!.GetValue<string>()));
        Assert.All(placements, placement => Assert.True(placement!["bounds"]!["width"]!.GetValue<int>() > 0));

        // Private local summary, restricted output classification, no cloud attempt.
        Assert.NotNull(result["summary"]);
        Assert.Equal("local-deterministic", result["summary"]!["provider"]!.GetValue<string>());
        Assert.Equal("RESTRICTED", result["summary"]!["classification"]!.GetValue<string>());
        Assert.Contains("Private summary", result["summary"]!["text"]!.GetValue<string>());
        Assert.DoesNotContain("ACC-", result["summary"]!["text"]!.GetValue<string>());
        Assert.Equal(0, result["egress"]!["externalAttempts"]!.GetValue<long>());
        Assert.Equal(0, result["egress"]!["attempts"]!.GetValue<long>());

        // Workflow audit chain verifies.
        Assert.True(result["audit"]!["chainValid"]!.GetValue<bool>());
        Assert.True(result["audit"]!["entryCount"]!.GetValue<int>() >= 8);

        // Broker audit chain verifies and contains no raw payload content.
        var brokerAudit = harness.ReadBrokerAudit();
        Assert.True(harness.VerifyBrokerAudit().Valid);
        Assert.Contains(brokerAudit, entry => entry.EventType == Aether.Security.Audit.AuditEventTypes.ContextPublish);
        Assert.Contains(brokerAudit, entry => entry.EventType == Aether.Security.Audit.AuditEventTypes.BrokerLaunch);
        Assert.DoesNotContain("ACC-00000001", File.ReadAllText(harness.BrokerAuditFile));

        // Each sample process independently rendered the typed context with the workflow trace id.
        foreach (var appId in TestData.SampleAppIds)
        {
            var lines = harness.ReadChildLogLines(appId);
            var contextLines = lines.Where(line => line.Contains("\"event\":\"context\"", StringComparison.Ordinal)).ToArray();
            Assert.NotEmpty(contextLines);
            Assert.Contains(contextLines, line => line.Contains($"\"traceId\":\"{traceId}\"", StringComparison.Ordinal));
            Assert.Contains(contextLines, line => line.Contains("\"contextType\":\"fdc3.instrument\"", StringComparison.Ordinal));
        }
    }

    [Fact(Timeout = 180_000)]
    public async Task WorkflowRemainsDeterministicAcrossRepeatedRuns()
    {
        await using var harness = await EndToEndHarness.StartAsync("repeat");

        string? firstTrace = null;
        string? firstPlan = null;
        for (var run = 0; run < 2; run++)
        {
            var shellRun = await harness.RunShellAsync(
            [
                "--classification", "RESTRICTED",
                "--command", ReferenceCommand,
            ]);
            Assert.Equal(0, shellRun.ExitCode);
            var result = JsonNode.Parse(shellRun.ResultJson!)!;
            Assert.True(result["success"]!.GetValue<bool>());
            var trace = result["traceId"]!.GetValue<string>();
            var plan = result["planId"]!.GetValue<string>();
            if (run == 0)
            {
                firstTrace = trace;
                firstPlan = plan;
            }
            else
            {
                Assert.NotEqual(firstTrace, trace);
                Assert.Equal(firstPlan, plan);
            }
        }
    }
}
