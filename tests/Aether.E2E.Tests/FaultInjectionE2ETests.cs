using System.Text.Json.Nodes;
using Aether.Testing;
using Xunit;

namespace Aether.E2E.Tests;

[Collection("E2E")]
public class FaultInjectionE2ETests
{
    private const string ReferenceCommand =
        "Open Northstar bond analytics, show the associated risk view, synchronize this instrument across the workspace and summarize the exposure privately.";

    [Fact(Timeout = 120_000)]
    public async Task ShellFailsFastAndStructuredWhenTheBrokerProcessDies()
    {
        await using var harness = await EndToEndHarness.StartAsync("broker-disconnect");
        var client = await harness.GetOperatorClientAsync();

        foreach (var appId in TestData.SampleAppIds)
        {
            var reply = await client.LaunchAppAsync(appId, Contracts.TraceIdGenerator.NewTraceId());
            Assert.True(reply.Accepted, reply.DenialCode);
        }

        await harness.KillBrokerAsync();

        // The shell must fail fast with a structured error (exit 4), not hang or crash.
        var run = await harness.RunShellAsync(
        [
            "--classification", "RESTRICTED",
            "--command", ReferenceCommand,
        ], timeout: TimeSpan.FromSeconds(45));

        Assert.Equal(4, run.ExitCode);
        Assert.Contains("shell.broker_error", run.StdErr, StringComparison.Ordinal);
        Assert.Null(run.ResultJson);

        // Shell-side failure is a clean process failure; no partial workflow evidence.
        EndToEndHarness.KillOrphanSampleApps();
    }

    [Fact(Timeout = 180_000)]
    public async Task RestartAfterCrashKeepsTheReferenceWorkflowGreen()
    {
        await using var harness = await EndToEndHarness.StartAsync("crash-restart-workflow");
        var client = await harness.GetOperatorClientAsync();

        var replies = new Dictionary<string, Aether.Contracts.V1.LaunchAppReply>(StringComparer.Ordinal);
        foreach (var appId in TestData.SampleAppIds)
        {
            var reply = await client.LaunchAppAsync(appId, Contracts.TraceIdGenerator.NewTraceId());
            Assert.True(reply.Accepted, reply.DenialCode);
            replies[appId] = reply;
        }

        using (var victim = System.Diagnostics.Process.GetProcessById(replies["sample.chart"].ProcessId))
        {
            victim.Kill(entireProcessTree: true);
            await victim.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10));
        }

        var restarted = await client.RestartAppAsync("sample.chart", Contracts.TraceIdGenerator.NewTraceId());
        Assert.True(restarted.Accepted, restarted.DenialCode + ": " + restarted.DenialMessage);

        var run = await harness.RunShellAsync(
        [
            "--classification", "RESTRICTED",
            "--command", ReferenceCommand,
        ]);

        Assert.Equal(0, run.ExitCode);
        var result = JsonNode.Parse(run.ResultJson!)!;
        Assert.True(result["success"]!.GetValue<bool>());
        Assert.Equal(3, result["steps"]!.AsArray().Count(step => step!["action"]!.GetValue<string>() == "LaunchApp"));
        Assert.True(harness.VerifyBrokerAudit().Valid);
    }
}
