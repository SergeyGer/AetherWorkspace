using System.Diagnostics;
using System.Runtime.CompilerServices;
using Aether.Contracts;
using Aether.Contracts.Broker;
using Aether.Contracts.Models;
using Aether.Contracts.V1;
using Grpc.Core;
using Grpc.Net.Client;

namespace Aether.Broker.Client;

/// <summary>
/// Typed client for the local broker. Business denials arrive as structured reply
/// fields; transport/authentication failures surface as <see cref="BrokerRpcException"/>.
/// Every call carries the session identity and the workflow trace id.
/// </summary>
public sealed class BrokerClient : IAsyncDisposable
{
    private readonly GrpcChannel _channel;
    private readonly BrokerService.BrokerServiceClient _service;

    public BrokerClient(GrpcChannel channel)
    {
        _channel = channel ?? throw new ArgumentNullException(nameof(channel));
        _service = new BrokerService.BrokerServiceClient(channel);
    }

    public BrokerSessionCredentials? Credentials { get; private set; }

    public RegisterReply? Registration { get; private set; }

    public async Task<RegisterReply> RegisterAsync(
        string appId,
        string registrationToken,
        string? instanceId = null,
        CancellationToken cancellationToken = default)
    {
        using var process = Process.GetCurrentProcess();
        var request = new RegisterRequest
        {
            AppId = appId,
            InstanceId = instanceId ?? "proc-" + Environment.ProcessId + "-" + Guid.NewGuid().ToString("n")[..8],
            RegistrationToken = registrationToken,
            ProcessId = Environment.ProcessId,
            ProcessStartUnixMs = new DateTimeOffset(process.StartTime.ToUniversalTime(), TimeSpan.Zero).ToUnixTimeMilliseconds(),
            ProtocolVersion = AetherVersions.ProtocolVersion,
            Nonce = Guid.NewGuid().ToString("n"),
        };

        try
        {
            var reply = await _service.RegisterAsync(request, cancellationToken: cancellationToken).ConfigureAwait(false);
            Credentials = new BrokerSessionCredentials(reply.SessionId, reply.SessionSecret);
            Registration = reply;
            return reply;
        }
        catch (RpcException exception)
        {
            throw BrokerRpcException.From(exception);
        }
    }

    public async Task<HeartbeatReply> HeartbeatAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            return await _service.HeartbeatAsync(
                new HeartbeatRequest { SentAtUnixMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() },
                Options(null, cancellationToken)).ConfigureAwait(false);
        }
        catch (RpcException exception)
        {
            throw BrokerRpcException.From(exception);
        }
    }

    public async Task<SnapshotReply> GetSnapshotAsync(string? traceId = null, CancellationToken cancellationToken = default)
    {
        try
        {
            return await _service.GetSnapshotAsync(
                new SnapshotRequest { TraceId = traceId ?? "" },
                Options(traceId, cancellationToken)).ConfigureAwait(false);
        }
        catch (RpcException exception)
        {
            throw BrokerRpcException.From(exception);
        }
    }

    public async Task<LaunchAppReply> LaunchAppAsync(string appId, string traceId, CancellationToken cancellationToken = default)
    {
        try
        {
            return await _service.LaunchAppAsync(
                new LaunchAppRequest { AppId = appId, TraceId = traceId },
                Options(traceId, cancellationToken)).ConfigureAwait(false);
        }
        catch (RpcException exception)
        {
            throw BrokerRpcException.From(exception);
        }
    }

    public async Task<StopAppReply> StopAppAsync(string appId, string? instanceId, string traceId, CancellationToken cancellationToken = default)
    {
        try
        {
            return await _service.StopAppAsync(
                new StopAppRequest { AppId = appId, InstanceId = instanceId ?? "", TraceId = traceId },
                Options(traceId, cancellationToken)).ConfigureAwait(false);
        }
        catch (RpcException exception)
        {
            throw BrokerRpcException.From(exception);
        }
    }

    public async Task<RestartAppReply> RestartAppAsync(string appId, string traceId, CancellationToken cancellationToken = default)
    {
        try
        {
            return await _service.RestartAppAsync(
                new RestartAppRequest { AppId = appId, TraceId = traceId },
                Options(traceId, cancellationToken)).ConfigureAwait(false);
        }
        catch (RpcException exception)
        {
            throw BrokerRpcException.From(exception);
        }
    }

    public async Task<PublishContextReply> PublishContextAsync(
        string contextRef,
        DataClassification classification,
        string traceId,
        string? clientMessageId = null,
        DateTimeOffset? deadlineUtc = null,
        string channel = ChannelNames.Default,
        CancellationToken cancellationToken = default)
    {
        var request = new PublishContextRequest
        {
            Channel = channel,
            ContextRef = contextRef,
            TraceId = traceId,
            SourceClassification = classification.ToProto(),
            ClientMessageId = clientMessageId ?? "",
            DeadlineUnixMs = deadlineUtc?.ToUnixTimeMilliseconds() ?? 0,
        };

        try
        {
            return await _service.PublishContextAsync(request, Options(traceId, cancellationToken)).ConfigureAwait(false);
        }
        catch (RpcException exception)
        {
            throw BrokerRpcException.From(exception);
        }
    }

    public async Task<ResolveContextReply> ResolveContextAsync(string contextRef, string traceId, CancellationToken cancellationToken = default)
    {
        try
        {
            return await _service.ResolveContextAsync(
                new ResolveContextRequest { ContextRef = contextRef, TraceId = traceId },
                Options(traceId, cancellationToken)).ConfigureAwait(false);
        }
        catch (RpcException exception)
        {
            throw BrokerRpcException.From(exception);
        }
    }

    public async Task<ReportWindowReply> ReportWindowAsync(WindowState state, string traceId, CancellationToken cancellationToken = default)
    {
        var request = new ReportWindowRequest
        {
            WindowId = state.WindowId,
            Title = state.Title,
            Bounds = new Aether.Contracts.V1.WindowBounds
            {
                X = state.Bounds.X,
                Y = state.Bounds.Y,
                Width = state.Bounds.Width,
                Height = state.Bounds.Height,
            },
            MonitorId = state.MonitorId,
            Visible = state.Visible,
            NativeHandle = state.NativeHandle,
            TraceId = traceId,
        };

        try
        {
            return await _service.ReportWindowAsync(request, Options(traceId, cancellationToken)).ConfigureAwait(false);
        }
        catch (RpcException exception)
        {
            throw BrokerRpcException.From(exception);
        }
    }

    public async Task<ArrangeWindowsReply> ArrangeWindowsAsync(string layout, string traceId, CancellationToken cancellationToken = default)
    {
        try
        {
            return await _service.ArrangeWindowsAsync(
                new ArrangeWindowsRequest { Layout = layout, TraceId = traceId },
                Options(traceId, cancellationToken)).ConfigureAwait(false);
        }
        catch (RpcException exception)
        {
            throw BrokerRpcException.From(exception);
        }
    }

    public async Task<HealthProbeReply> HealthProbeAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            return await _service.HealthProbeAsync(new HealthProbeRequest(), Options(null, cancellationToken)).ConfigureAwait(false);
        }
        catch (RpcException exception)
        {
            throw BrokerRpcException.From(exception);
        }
    }

    public async IAsyncEnumerable<ContextEnvelope> SubscribeAsync(
        string channel,
        string traceId,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        AsyncServerStreamingCall<ContextEnvelope> call;
        try
        {
            call = _service.Subscribe(
                new SubscribeRequest { Channel = channel, TraceId = traceId },
                Options(traceId, cancellationToken));
        }
        catch (RpcException exception)
        {
            throw BrokerRpcException.From(exception);
        }

        using (call)
        {
            while (true)
            {
                ContextEnvelope envelope;
                try
                {
                    if (!await call.ResponseStream.MoveNext(cancellationToken).ConfigureAwait(false))
                    {
                        yield break;
                    }

                    envelope = call.ResponseStream.Current;
                }
                catch (RpcException exception)
                {
                    if (exception.StatusCode == StatusCode.Cancelled)
                    {
                        yield break;
                    }

                    throw BrokerRpcException.From(exception);
                }

                yield return envelope;
            }
        }
    }

    private Metadata Options(string? traceId, CancellationToken cancellationToken)
    {
        _ = cancellationToken;
        var metadata = new Metadata();
        if (Credentials is not null)
        {
            metadata.Add(BrokerMetadataKeys.SessionId, Credentials.SessionId);
            metadata.Add(BrokerMetadataKeys.SessionSecret, Credentials.SessionSecret);
        }

        if (TraceContext.IsValidTraceId(traceId))
        {
            metadata.Add(BrokerMetadataKeys.TraceId, traceId!);
        }

        return metadata;
    }

    public ValueTask DisposeAsync()
    {
        _channel.Dispose();
        return ValueTask.CompletedTask;
    }
}
