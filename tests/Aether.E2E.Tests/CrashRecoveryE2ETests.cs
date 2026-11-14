using System.Diagnostics;
using Aether.Testing;
using Xunit;

namespace Aether.E2E.Tests;

[Collection("E2E")]
public class CrashRecoveryE2ETests
{
    private const string ReferenceCommand =
        "Open Northstar bond analytics, show the associated risk view, synchronize this instrument across the workspace and summarize the exposure privately.";

    [Fact(Timeout = 240_000)]
    public async Task BrokerAndShellSurviveASampleAppCrashAndSupportBoundedRestart()
    {
        await using var harness = await EndToEndHarness.StartAsync("crash");
        var client = await harness.GetOperatorClientAsync();

        var launched = new List<Aether.Contracts.V1.LaunchAppReply>();
        foreach (var appId in TestData.SampleAppIds)
        {
            var reply = await client.LaunchAppAsync(appId, Contracts.TraceIdGenerator.NewTraceId());
            Assert.True(reply.Accepted, reply.DenialCode);
            launched.Add(reply);
        }

        var victim = launched.Single(reply => reply.AppId == "sample.risk");
        using (var process = Process.GetProcessById(victim.ProcessId))
        {
            process.Kill(entireProcessTree: true);
            await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10));
        }

        // The broker survives the crash and the operator session stays usable.
        var probe = await client.HealthProbeAsync();
        Assert.Contains(probe.AppHealth, state => state.StartsWith("sample.risk=EXITED", StringComparison.Ordinal));

        // Shell reference workflow is still denied/affected only by the dead app path,
        // but restart is available and bounded.
        var restarted = await client.RestartAppAsync("sample.risk", Contracts.TraceIdGenerator.NewTraceId());
        Assert.True(restarted.Accepted, restarted.DenialCode + ": " + restarted.DenialMessage);
        Assert.True(restarted.InstanceId.Length > 0);

        var afterRestart = await client.HealthProbeAsync();
        Assert.Contains(afterRestart.AppHealth, state => state.StartsWith("sample.risk=HEALTHY", StringComparison.Ordinal));

        // A full workflow still completes after crash + restart (idempotent launch reuse).
        var run = await harness.RunShellAsync(
        [
            "--classification", "RESTRICTED",
            "--command", ReferenceCommand,
        ]);
        Assert.Equal(0, run.ExitCode);
        Assert.True(System.Text.Json.Nodes.JsonNode.Parse(run.ResultJson!)!["success"]!.GetValue<bool>());
        Assert.True(harness.VerifyBrokerAudit().Valid);
    }
}
