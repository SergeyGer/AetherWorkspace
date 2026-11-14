using System.Diagnostics;
using System.Globalization;
using Aether.Broker;
using Aether.Broker.Client;
using Aether.Contracts;
using Aether.Contracts.Errors;
using Aether.Testing;
using Xunit;

namespace Aether.Broker.Tests;

/// <summary>
/// Fault-injection evidence for v0.2: a hung or killed sample app must not take down
/// the broker/shell, health state must become deterministic, and restart must be bounded.
/// </summary>
public class FaultInjectionTests
{
    private static async Task<BrokerClient> StartOperatorAsync(BrokerTestContext context) =>
        await context.RegisterOperatorAsync();

    private static async Task<string?> WaitForHealthStateAsync(
        BrokerClient client,
        string appId,
        string expectedState,
        TimeSpan timeout)
    {
        var deadline = DateTimeOffset.UtcNow + timeout;
        string? lastStates = null;
        while (DateTimeOffset.UtcNow < deadline)
        {
            var probe = await client.HealthProbeAsync();
            lastStates = string.Join(",", probe.AppHealth);
            if (probe.AppHealth.Any(state => state.StartsWith($"{appId}={expectedState}", StringComparison.Ordinal)))
            {
                return lastStates;
            }

            await Task.Delay(ResponseTime, CancellationToken.None);
        }

        return lastStates;
    }

    private static readonly TimeSpan ResponseTime = TimeSpan.FromMilliseconds(250);

    private static void SendSignal(int processId, string signal)
    {
        using var process = Process.Start(new ProcessStartInfo
        {
            FileName = "kill",
            ArgumentList = { "-" + signal, processId.ToString(CultureInfo.InvariantCulture) },
            UseShellExecute = false,
            RedirectStandardError = true,
        });
        Assert.NotNull(process);
        process!.WaitForExit(5000);
        Assert.Equal(0, process.ExitCode);
    }

    [Fact(Timeout = 120_000)]
    public async Task HungSampleAppBecomesUnresponsiveAndBoundedRestartRecovers()
    {
        if (!OperatingSystem.IsLinux())
        {
            return; // SIGSTOP-based hang injection is Unix-only; Windows CI has other coverage.
        }

        await using var context = BrokerTestContext.Create(
            testName: "hang",
            heartbeatIntervalSeconds: 1,
            missedHeartbeatsBeforeUnresponsive: 2);
        await context.StartAsync();
        await using var client = await StartOperatorAsync(context);

        var launched = await client.LaunchAppAsync("sample.watchlist", TraceIdGenerator.NewTraceId());
        Assert.True(launched.Accepted, launched.DenialCode + ": " + launched.DenialMessage);

        SendSignal(launched.ProcessId, "STOP");
        var unresponsiveStates = await WaitForHealthStateAsync(client, "sample.watchlist", "UNRESPONSIVE", TimeSpan.FromSeconds(20));
        Assert.True(unresponsiveStates is not null &&
                    unresponsiveStates.Contains("sample.watchlist=UNRESPONSIVE", StringComparison.Ordinal),
            "expected UNRESPONSIVE health state, got: " + unresponsiveStates);

        SendSignal(launched.ProcessId, "CONT");

        var restarted = await client.RestartAppAsync("sample.watchlist", TraceIdGenerator.NewTraceId());
        Assert.True(restarted.Accepted, restarted.DenialCode + ": " + restarted.DenialMessage);
        Assert.NotEmpty(restarted.InstanceId);

        var healthyStates = await WaitForHealthStateAsync(client, "sample.watchlist", "HEALTHY", TimeSpan.FromSeconds(20));
        Assert.True(healthyStates is not null && healthyStates.Contains("sample.watchlist=HEALTHY", StringComparison.Ordinal),
            "expected HEALTHY after restart, got: " + healthyStates);

        // Broker and operator session survived the entire fault sequence.
        Assert.True((await client.HealthProbeAsync()).ActiveSessions >= 1);
    }

    [Fact(Timeout = 120_000)]
    public async Task KilledSampleAppIsReportedExitedAndRestartIsBounded()
    {
        await using var context = BrokerTestContext.Create(
            testName: "kill",
            heartbeatIntervalSeconds: 1,
            missedHeartbeatsBeforeUnresponsive: 2);
        await context.StartAsync();
        await using var client = await StartOperatorAsync(context);

        var launched = await client.LaunchAppAsync("sample.risk", TraceIdGenerator.NewTraceId());
        Assert.True(launched.Accepted, launched.DenialCode);

        using (var process = Process.GetProcessById(launched.ProcessId))
        {
            process.Kill(entireProcessTree: true);
            await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10));
        }

        var exitedStates = await WaitForHealthStateAsync(client, "sample.risk", "EXITED", TimeSpan.FromSeconds(15));
        Assert.True(exitedStates is not null && exitedStates.Contains("sample.risk=EXITED", StringComparison.Ordinal),
            "expected EXITED state, got: " + exitedStates);

        var restarted = await client.RestartAppAsync("sample.risk", TraceIdGenerator.NewTraceId());
        Assert.True(restarted.Accepted, restarted.DenialCode + ": " + restarted.DenialMessage);

        var healthyStates = await WaitForHealthStateAsync(client, "sample.risk", "HEALTHY", TimeSpan.FromSeconds(20));
        Assert.True(healthyStates is not null && healthyStates.Contains("sample.risk=HEALTHY", StringComparison.Ordinal),
            "expected HEALTHY after restart, got: " + healthyStates);
    }

    [Fact(Timeout = 120_000)]
    public async Task DuplicateClientMessageIdIsDeliveredOnlyOnceAtTheRpcBoundary()
    {
        await using var context = BrokerTestContext.Create(testName: "retry-dedupe");
        await context.StartAsync();
        await using var client = await StartOperatorAsync(context);

        var launched = await client.LaunchAppAsync("sample.watchlist", TraceIdGenerator.NewTraceId());
        Assert.True(launched.Accepted, launched.DenialCode);
        var traceId = TraceIdGenerator.NewTraceId();
        const string clientMessageId = "retry-idempotency-key-1";

        var first = await client.PublishContextAsync(
            "synthetic.instrument.northstar",
            DataClassification.Restricted,
            traceId,
            clientMessageId);
        var second = await client.PublishContextAsync(
            "synthetic.instrument.northstar",
            DataClassification.Restricted,
            traceId,
            clientMessageId);

        Assert.True(first.Accepted, first.DenialCode);
        Assert.Equal(1, first.DeliveredCount);
        Assert.True(second.Accepted, second.DenialCode + " (retries are idempotent)");
        Assert.Equal(0, second.DeliveredCount);
        Assert.Equal(first.MessageId, second.MessageId);

        await Task.Delay(300, CancellationToken.None);
        var logPath = Path.Combine(context.Directory, "child-logs", $"sample.watchlist-{launched.InstanceId}.log");
        Assert.True(File.Exists(logPath), "expected child log at " + logPath);
        var contextLines = File.ReadAllLines(logPath).Count(line => line.Contains("\"event\":\"context\"", StringComparison.Ordinal));
        Assert.Equal(1, contextLines);
    }

    [Fact]
    public async Task DuplicateMessageIsRejectedAcrossDistinctPublishCallsWithoutKey()
    {
        await using var context = BrokerTestContext.Create(testName: "no-dedupe-key");
        await context.StartAsync();
        await using var client = await StartOperatorAsync(context);

        var launched = await client.LaunchAppAsync("sample.watchlist", TraceIdGenerator.NewTraceId());
        Assert.True(launched.Accepted, launched.DenialCode);

        var first = await client.PublishContextAsync("synthetic.instrument.vega", DataClassification.Public, TraceIdGenerator.NewTraceId());
        var second = await client.PublishContextAsync("synthetic.instrument.vega", DataClassification.Public, TraceIdGenerator.NewTraceId());

        Assert.True(first.Accepted);
        Assert.True(second.Accepted);
        Assert.NotEqual(first.MessageId, second.MessageId);
        Assert.Equal(1, first.DeliveredCount);
        Assert.Equal(1, second.DeliveredCount);
    }
}
