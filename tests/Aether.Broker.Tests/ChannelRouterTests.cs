using Aether.Broker;
using Aether.Broker.Channels;
using Aether.Broker.Sessions;
using Aether.Contracts;
using Aether.Contracts.Models;
using Aether.Contracts.V1;
using Aether.Testing;
using Xunit;

namespace Aether.Broker.Tests;

public class ChannelRouterTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.UtcNow;

    private static BrokerOptions Options(int queueCapacity = 64) => new()
    {
        Registry = TestData.CreateRegistry(),
        Contexts = TestData.CreateContextCatalog(),
        ChannelQueueCapacity = queueCapacity,
        TimeProvider = new ManualTimeProvider(Now),
    };

    private static BrokerSession Session(
        string instanceId,
        string appId = "sample.watchlist",
        IReadOnlySet<string>? capabilities = null,
        IReadOnlySet<string>? allowedChannels = null,
        IReadOnlySet<string>? allowedContextTypes = null,
        IReadOnlySet<DataClassification>? allowedClassifications = null)
    {
        var baseApp = TestData.CreateRegistry().GetRequired(appId);
        var entry = new AppManifestEntry
        {
            AppId = baseApp.AppId,
            DisplayName = baseApp.DisplayName,
            Launchable = baseApp.Launchable,
            Executable = baseApp.Executable,
            Arguments = baseApp.Arguments,
            Capabilities = capabilities?.ToArray() ?? baseApp.Entry.Capabilities.ToArray(),
            AllowedChannels = allowedChannels?.ToArray() ?? baseApp.Entry.AllowedChannels.ToArray(),
            AllowedContextTypes = allowedContextTypes?.ToArray() ?? baseApp.Entry.AllowedContextTypes.ToArray(),
            AllowedClassifications = allowedClassifications?.Select(classification => classification.ToWireName()).ToArray()
                ?? baseApp.Entry.AllowedClassifications.ToArray(),
        };
        var app = new Aether.Broker.Registry.RegisteredApp(entry, 0);
        return new BrokerSession(
            "sess-" + instanceId,
            app,
            instanceId,
            1000,
            Now,
            "hash",
            Now,
            Now.AddHours(1));
    }

    private static ContextEnvelope Envelope(string messageId = "msg-1", DataClassification classification = DataClassification.Restricted) => new()
    {
        MessageId = messageId,
        TraceId = TraceIdGenerator.NewTraceId(),
        SpanId = TraceIdGenerator.NewSpanId(),
        SchemaVersion = "1.0",
        ContextType = "fdc3.instrument",
        Classification = classification.ToProto(),
        CreatedAtUnixMs = Now.ToUnixTimeMilliseconds(),
        PayloadJson = "{\"type\":\"fdc3.instrument\",\"name\":\"Northstar\",\"id\":{\"ticker\":\"NRST\"}}",
        Channel = ChannelNames.Default,
        Provenance = new Provenance { SourceAppId = "aether.shell", SourceInstanceId = "shell", TraceId = TraceIdGenerator.NewTraceId(), SpanId = TraceIdGenerator.NewSpanId() },
    };

    [Fact]
    public void PublishedContextKeepsCorrelationMetadataAndReachesSubscribers()
    {
        using var router = new ChannelRouter(Options(), [ChannelNames.Default]);
        var subscriber = Session("sub-1");
        var publisher = Session("shell", "aether.shell");
        using var subscription = router.Subscribe(subscriber, ChannelNames.Default);
        var envelope = Envelope();

        var result = router.Publish(publisher, envelope, clientMessageId: null);

        Assert.True(result.Accepted);
        Assert.Equal(1, result.DeliveredCount);
        Assert.Equal(0, result.DroppedCount);
        Assert.True(subscription.Reader.TryRead(out var delivered));
        Assert.Equal(envelope.MessageId, delivered.MessageId);
        Assert.Equal(envelope.TraceId, delivered.TraceId);
        Assert.Equal(envelope.Provenance.SourceAppId, delivered.Provenance.SourceAppId);
        Assert.Equal(envelope.Classification, delivered.Classification);
    }

    [Fact]
    public void PerSubscriberOrderingIsFifo()
    {
        using var router = new ChannelRouter(Options(queueCapacity: 256), [ChannelNames.Default]);
        var subscriber = Session("sub-fifo", "sample.risk");
        var publisher = Session("shell", "aether.shell");
        using var subscription = router.Subscribe(subscriber, ChannelNames.Default);

        for (var index = 0; index < 100; index++)
        {
            router.Publish(publisher, Envelope("msg-" + index), clientMessageId: null);
        }

        for (var index = 0; index < 100; index++)
        {
            Assert.True(subscription.Reader.TryRead(out var delivered));
            Assert.Equal("msg-" + index, delivered.MessageId);
        }
    }

    [Fact]
    public void SubscriberClearanceFiltersUndeliverableContext()
    {
        using var router = new ChannelRouter(Options(), [ChannelNames.Default]);
        var publicOnlySession = Session("sub-public", allowedClassifications: new HashSet<DataClassification> { DataClassification.Public });
        var publisher = Session("shell", "aether.shell");
        using var subscription = router.Subscribe(publicOnlySession, ChannelNames.Default);

        var result = router.Publish(publisher, Envelope(classification: DataClassification.Restricted), clientMessageId: null);

        Assert.True(result.Accepted);
        Assert.Equal(0, result.DeliveredCount);
        Assert.Equal(1, result.DroppedCount);
        Assert.False(subscription.Reader.TryRead(out _));
    }

    [Fact]
    public void BoundedQueueReportsExplicitOverflow()
    {
        using var router = new ChannelRouter(Options(queueCapacity: 1), [ChannelNames.Default]);
        var subscriber = Session("sub-overflow");
        var publisher = Session("shell", "aether.shell");
        using var subscription = router.Subscribe(subscriber, ChannelNames.Default);

        var first = router.Publish(publisher, Envelope("msg-1"), clientMessageId: null);
        var second = router.Publish(publisher, Envelope("msg-2"), clientMessageId: null);

        Assert.Equal(1, first.DeliveredCount);
        Assert.Equal(0, second.DeliveredCount);
        Assert.True(second.Overflowed);
        Assert.Equal(1, second.DroppedCount);
        Assert.True(subscription.Reader.TryRead(out var only));
        Assert.Equal("msg-1", only.MessageId);
    }

    [Fact]
    public void DuplicateClientMessageIsIdempotent()
    {
        using var router = new ChannelRouter(Options(), [ChannelNames.Default]);
        var subscriber = Session("sub-dedupe");
        var publisher = Session("shell", "aether.shell");
        using var subscription = router.Subscribe(subscriber, ChannelNames.Default);

        var first = router.Publish(publisher, Envelope("msg-once"), clientMessageId: "client-1");
        var second = router.Publish(publisher, Envelope("msg-twice"), clientMessageId: "client-1");

        Assert.False(first.Duplicate);
        Assert.False(second.Duplicate is false && second.DeliveredCount > 0);
        Assert.True(second.Duplicate);
        Assert.Equal(0, second.DeliveredCount);
        Assert.Equal("msg-once", second.MessageId);
        Assert.True(subscription.Reader.TryRead(out var only));
        Assert.Equal("msg-once", only.MessageId);
        Assert.False(subscription.Reader.TryRead(out _));
    }

    [Fact]
    public void SlowSubscriberDoesNotBlockPublishForOthers()
    {
        using var router = new ChannelRouter(Options(queueCapacity: 1), [ChannelNames.Default]);
        var slow = Session("sub-slow");
        var fast = Session("sub-fast", "sample.risk");
        var publisher = Session("shell", "aether.shell");
        using var slowSubscription = router.Subscribe(slow, ChannelNames.Default);
        using var fastSubscription = router.Subscribe(fast, ChannelNames.Default);

        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        for (var index = 0; index < 10; index++)
        {
            router.Publish(publisher, Envelope("msg-" + index), clientMessageId: null);
        }

        stopwatch.Stop();
        Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(2));
        Assert.True(fastSubscription.Reader.TryRead(out _));
    }

    [Fact]
    public void DisposedSubscriptionStopsDeliveries()
    {
        using var router = new ChannelRouter(Options(), [ChannelNames.Default]);
        var subscriber = Session("sub-dispose");
        var publisher = Session("shell", "aether.shell");
        var subscription = router.Subscribe(subscriber, ChannelNames.Default);
        subscription.Dispose();

        var result = router.Publish(publisher, Envelope(), clientMessageId: null);

        Assert.True(result.Accepted);
        Assert.Equal(0, result.DeliveredCount);
    }

    [Fact]
    public void UnauthorizedChannelSubscriptionAndPublishAreDenied()
    {
        using var router = new ChannelRouter(Options(), [ChannelNames.Default]);
        var subscriber = Session("sub-denied", allowedChannels: new HashSet<string>(StringComparer.Ordinal));
        Assert.Throws<BrokerAccessDeniedException>(() => router.Subscribe(subscriber, ChannelNames.Default));

        var publisher = Session("shell", "aether.shell", allowedChannels: new HashSet<string>(StringComparer.Ordinal));
        var result = router.Publish(publisher, Envelope(), clientMessageId: null);
        Assert.False(result.Accepted);
        Assert.Equal(Contracts.Errors.AetherErrorCodes.AuthChannelDenied, result.DenialCode);
    }
}
