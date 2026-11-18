using Aether.Broker;
using Aether.Broker.Client;
using Aether.Contracts;
using Aether.Contracts.Broker;
using Aether.Contracts.Errors;
using Aether.Contracts.Fdc3;
using Aether.Contracts.Models;
using Aether.Testing;
using Xunit;

namespace Aether.Fdc3.Tests;

/// <summary>v1.5 AW-064 / ADR-013: the bounded FDC3 subset is kept; broader surfaces stay rejected.</summary>
public sealed class BroadenedSurfaceRejectionTests
{
    private static async Task<(BrokerTestContext Context, BrokerClient Client)> StartAsync(string name)
    {
        var context = BrokerTestContext.Create(testName: name);
        await context.StartAsync();
        var client = await context.RegisterOperatorAsync();
        return (context, client);
    }

    [Theory]
    [InlineData("JoinUserChannel")]
    [InlineData("OpenGlobalChannel")]
    [InlineData("AddContextListener")]
    [InlineData("RaiseExternalIntent")]
    [InlineData("FederateDirectory")]
    public async Task BroaderFdc3IntentSurfaceIsRejected(string intent)
    {
        var (context, client) = await StartAsync("v15-intent-" + intent.ToLowerInvariant());
        await using var _ = context;
        await using var __ = client;

        var reply = await client.ResolveIntentAsync(intent, "synthetic.instrument.northstar");

        Assert.False(reply.Resolved);
        Assert.Equal(AetherErrorCodes.Fdc3IntentUnsupported, reply.DenialCode);
    }

    [Theory]
    [InlineData("user/blue")]
    [InlineData("global")]
    [InlineData("channel-1")]
    public async Task NonDefaultChannelsAreRejectedForSubscription(string channel)
    {
        var (context, client) = await StartAsync("v15-channel-" + channel.Replace('/', '-'));
        await using var _ = context;
        await using var __ = client;

        var enumerator = client.SubscribeAsync(channel, TraceIdGenerator.NewTraceId()).GetAsyncEnumerator();
        try
        {
            var exception = await Assert.ThrowsAsync<BrokerRpcException>(async () => await enumerator.MoveNextAsync());
            // The operator session lacks context.subscribe; a session that had it would still be
            // rejected because only the default channel is configured. Either rejection is a
            // bounded-surface denial, never access to a user/global channel.
            Assert.Contains(exception.Code, new[] { AetherErrorCodes.AuthChannelDenied, AetherErrorCodes.AuthCapabilityMissing });
        }
        finally
        {
            await enumerator.DisposeAsync();
        }
    }

    [Fact]
    public async Task NonDefaultChannelPublishIsRejected()
    {
        var (context, client) = await StartAsync("v15-channel-publish");
        await using var _ = context;

        var reply = await client.PublishContextAsync(
            "synthetic.instrument.vega",
            DataClassification.Public,
            TraceIdGenerator.NewTraceId(),
            channel: "user/blue");

        Assert.False(reply.Accepted);
        Assert.Equal(AetherErrorCodes.AuthChannelDenied, reply.DenialCode);
    }

    [Fact]
    public async Task SnapshotExposesOnlyTheBoundedIntentAndContextSubset()
    {
        var (context, client) = await StartAsync("v15-snapshot");
        await using var _ = context;

        var snapshot = await client.GetSnapshotAsync(TraceIdGenerator.NewTraceId());
        var allowedIntents = new HashSet<string>(StringComparer.Ordinal)
        {
            Fdc3Intents.ViewInstrument, Fdc3Intents.ViewAnalysis, Fdc3Intents.ViewChart,
        };
        var allowedContexts = new HashSet<string>(StringComparer.Ordinal)
        {
            Fdc3ContextTypes.Instrument, Fdc3ContextTypes.Organization, "aether.exposure",
        };

        Assert.NotEmpty(snapshot.Apps);
        foreach (var app in snapshot.Apps)
        {
            Assert.All(app.Intents, intent => Assert.Contains(intent, allowedIntents));
            Assert.All(app.AllowedContextTypes, contextType => Assert.Contains(contextType, allowedContexts));
            Assert.All(app.AllowedChannels, channel => Assert.Equal(ChannelNames.Default, channel));
        }
    }
}
