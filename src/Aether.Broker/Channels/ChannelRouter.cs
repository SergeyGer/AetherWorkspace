using System.Threading.Channels;
using Aether.Broker.Sessions;
using Aether.Contracts;
using Aether.Contracts.Errors;
using Aether.Contracts.V1;

namespace Aether.Broker.Channels;

public sealed record ChannelPublishResult(
    bool Accepted,
    string MessageId,
    int DeliveredCount,
    int DroppedCount,
    bool Overflowed,
    bool Duplicate,
    string? DenialCode = null,
    string? DenialMessage = null)
{
    public static ChannelPublishResult Deny(string code, string message) =>
        new(false, "", 0, 0, false, false, code, message);
}

/// <summary>Per-subscriber bounded subscription over a named channel.</summary>
public sealed class ChannelSubscription : IAsyncDisposable, IDisposable
{
    private readonly Action<ChannelSubscription> _onDispose;
    private int _disposed;

    internal ChannelSubscription(string channel, string sessionId, ChannelReader<ContextEnvelope> reader, Action<ChannelSubscription> onDispose)
    {
        Channel = channel;
        SessionId = sessionId;
        Reader = reader;
        _onDispose = onDispose;
    }

    public string Channel { get; }

    public string SessionId { get; }

    public ChannelReader<ContextEnvelope> Reader { get; }

    public ValueTask DisposeAsync()
    {
        DisposeCore();
        return ValueTask.CompletedTask;
    }

    public void Dispose() => DisposeCore();

    private void DisposeCore()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 0)
        {
            _onDispose(this);
        }
    }
}

/// <summary>
/// Bounded per-channel message routing with FIFO ordering per subscriber,
/// at-most-once delivery, explicit overflow accounting and idempotent retries
/// through per-publisher deduplication keys.
/// </summary>
public sealed class ChannelRouter : IDisposable
{
    private readonly object _gate = new();
    private readonly BrokerOptions _options;
    private readonly Dictionary<string, ChannelState> _channels = new(StringComparer.Ordinal);
    private readonly Dictionary<string, (string MessageId, DateTimeOffset At)> _dedupe = new(StringComparer.Ordinal);
    private readonly Queue<string> _dedupeOrder = new();
    private readonly TimeProvider _timeProvider;
    private const int DedupeCapacity = 1024;
    private static readonly TimeSpan DedupeTtl = TimeSpan.FromMinutes(5);

    public ChannelRouter(BrokerOptions options, IEnumerable<string> knownChannels)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _timeProvider = options.TimeProvider;
        foreach (var channel in knownChannels)
        {
            _channels[channel] = new ChannelState(channel);
        }
    }

    public int SubscriberCount(string channel)
    {
        lock (_gate)
        {
            return _channels.TryGetValue(channel, out var state) ? state.Subscribers.Count : 0;
        }
    }

    public int SubscriberCountForInstance(string instanceId)
    {
        lock (_gate)
        {
            return _channels.Values.Sum(state => state.Subscribers.Count(subscriber =>
                string.Equals(subscriber.Session.InstanceId, instanceId, StringComparison.Ordinal)));
        }
    }

    public ChannelSubscription Subscribe(BrokerSession session, string channel)
    {
        ArgumentNullException.ThrowIfNull(session);

        if (!session.AllowedChannels.Contains(channel))
        {
            throw new BrokerAccessDeniedException(AetherErrorCodes.AuthChannelDenied, "The session is not allowed to subscribe to this channel.");
        }

        ChannelState state;
        lock (_gate)
        {
            if (!_channels.TryGetValue(channel, out state!))
            {
                throw new BrokerAccessDeniedException(AetherErrorCodes.AuthChannelDenied, "The requested channel is not configured.");
            }
        }

        var queue = Channel.CreateBounded<ContextEnvelope>(new BoundedChannelOptions(_options.ChannelQueueCapacity)
        {
            SingleReader = true,
            SingleWriter = false,
            FullMode = BoundedChannelFullMode.Wait,
            AllowSynchronousContinuations = false,
        });

        ChannelSubscription? subscription = null;
        var subscriber = new Subscriber(session, queue);
        subscription = new ChannelSubscription(channel, session.SessionId, queue.Reader, self =>
        {
            lock (_gate)
            {
                if (_channels.TryGetValue(self.Channel, out var current))
                {
                    current.Subscribers.Remove(subscriber);
                }
            }

            queue.Writer.TryComplete();
        });

        lock (_gate)
        {
            state.Subscribers.Add(subscriber);
        }

        return subscription;
    }

    public ChannelPublishResult Publish(BrokerSession publisher, ContextEnvelope envelope, string? clientMessageId)
    {
        ArgumentNullException.ThrowIfNull(publisher);
        ArgumentNullException.ThrowIfNull(envelope);

        if (!publisher.AllowedChannels.Contains(envelope.Channel))
        {
            return ChannelPublishResult.Deny(AetherErrorCodes.AuthChannelDenied, "The session is not allowed to publish to this channel.");
        }

        var dedupeKey = string.IsNullOrWhiteSpace(clientMessageId)
            ? null
            : publisher.SessionId + "|" + clientMessageId;

        int delivered = 0;
        var dropped = 0;
        var overflowed = false;

        lock (_gate)
        {
            if (!_channels.TryGetValue(envelope.Channel, out var state))
            {
                return ChannelPublishResult.Deny(AetherErrorCodes.AuthChannelDenied, "The requested channel is not configured.");
            }

            PruneDedupeLocked();

            if (dedupeKey is not null && _dedupe.TryGetValue(dedupeKey, out var existing))
            {
                return new ChannelPublishResult(true, existing.MessageId, 0, 0, false, true);
            }

            var classification = envelope.Classification.FromProto();
            var subscribers = state.Subscribers.ToArray();
            foreach (var subscriber in subscribers)
            {
                var session = subscriber.Session;
                if (session.IsExpired(_timeProvider.GetUtcNow()))
                {
                    continue;
                }

                if (!session.AllowedClassifications.Contains(classification) ||
                    !session.AllowedContextTypes.Contains(envelope.ContextType))
                {
                    dropped++;
                    continue;
                }

                if (subscriber.Queue.Writer.TryWrite(envelope))
                {
                    delivered++;
                }
                else
                {
                    dropped++;
                    overflowed = true;
                }
            }

            if (dedupeKey is not null)
            {
                _dedupe[dedupeKey] = (envelope.MessageId, _timeProvider.GetUtcNow());
                _dedupeOrder.Enqueue(dedupeKey);
            }
        }

        return new ChannelPublishResult(true, envelope.MessageId, delivered, dropped, overflowed, false);
    }

    private void PruneDedupeLocked()
    {
        var now = _timeProvider.GetUtcNow();
        while (_dedupeOrder.Count != 0)
        {
            var oldest = _dedupeOrder.Peek();
            if (_dedupe.Count <= DedupeCapacity && _dedupe.TryGetValue(oldest, out var entry) && now - entry.At <= DedupeTtl)
            {
                return;
            }

            _dedupe.Remove(oldest);
            _dedupeOrder.Dequeue();
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            foreach (var state in _channels.Values)
            {
                foreach (var subscriber in state.Subscribers)
                {
                    subscriber.Queue.Writer.TryComplete();
                }

                state.Subscribers.Clear();
            }
        }
    }

    private sealed class ChannelState
    {
        public ChannelState(string name) => Name = name;

        public string Name { get; }

        public List<Subscriber> Subscribers { get; } = [];
    }

    private sealed class Subscriber
    {
        public Subscriber(BrokerSession session, Channel<ContextEnvelope> queue)
        {
            Session = session;
            Queue = queue;
        }

        public BrokerSession Session { get; }

        public Channel<ContextEnvelope> Queue { get; }
    }
}

/// <summary>Structured access denial raised by broker components; mapped to a gRPC status.</summary>
public sealed class BrokerAccessDeniedException : Exception
{
    public BrokerAccessDeniedException(string code, string message)
        : base(message) => Code = code;

    public string Code { get; }
}
