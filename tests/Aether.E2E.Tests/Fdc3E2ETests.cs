using System.Text.Json.Nodes;
using Aether.Contracts;
using Aether.Contracts.Fdc3;
using Aether.Testing;
using Xunit;

namespace Aether.E2E.Tests;

[Collection("E2E")]
public class Fdc3E2ETests
{
    [Fact(Timeout = 180_000)]
    public async Task OrganizationContextReachesAllAppsAndIntentsResolveAcrossProcesses()
    {
        await using var harness = await EndToEndHarness.StartAsync("fdc3");
        var client = await harness.GetOperatorClientAsync();

        foreach (var appId in TestData.SampleAppIds)
        {
            var reply = await client.LaunchAppAsync(appId, TraceIdGenerator.NewTraceId());
            Assert.True(reply.Accepted, reply.DenialCode);
        }

        var traceId = TraceIdGenerator.NewTraceId();
        var published = await client.PublishContextAsync(
            "synthetic.organization.northstar",
            DataClassification.Internal,
            traceId,
            clientMessageId: "fdc3-org-" + traceId);
        Assert.True(published.Accepted, published.DenialCode);
        Assert.Equal(3, published.DeliveredCount);

        await Task.Delay(500, CancellationToken.None);
        foreach (var appId in TestData.SampleAppIds)
        {
            var lines = harness.ReadChildLogLines(appId);
            Assert.Contains(lines, line =>
                line.Contains("\"contextType\":\"fdc3.organization\"", StringComparison.Ordinal) &&
                line.Contains($"\"traceId\":\"{traceId}\"", StringComparison.Ordinal));
        }

        var resolved = await client.ResolveIntentAsync(Fdc3Intents.ViewChart, "synthetic.instrument.northstar");
        Assert.True(resolved.Resolved, resolved.DenialCode);
        Assert.Equal("sample.chart", resolved.AppId);

        var rejected = await client.ResolveIntentAsync("TakeOverWorkspace", "synthetic.instrument.northstar");
        Assert.False(rejected.Resolved);
        Assert.Equal(Aether.Contracts.Errors.AetherErrorCodes.Fdc3IntentUnsupported, rejected.DenialCode);

        var snapshot = await client.GetSnapshotAsync(TraceIdGenerator.NewTraceId());
        var watchlist = snapshot.Apps.Single(app => app.AppId == "sample.watchlist");
        Assert.Contains("ViewInstrument", watchlist.Intents);
        Assert.True(harness.VerifyBrokerAudit().Valid);
    }
}
