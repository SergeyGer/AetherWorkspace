using Aether.Broker.Channels;
using Aether.Broker.Sessions;
using Aether.Contracts.Broker;
using Grpc.Core;
using Grpc.Core.Interceptors;

namespace Aether.Broker.Hosting;

/// <summary>
/// Authenticates every broker RPC except the explicit anonymous methods. The
/// session is resolved from metadata, validated (TTL + constant-time secret
/// comparison) and attached to the call context for capability checks.
/// </summary>
public sealed class BrokerAuthInterceptor : Interceptor
{
    private readonly SessionManager _sessions;
    private readonly BrokerOptions _options;

    public BrokerAuthInterceptor(SessionManager sessions, BrokerOptions options)
    {
        _sessions = sessions ?? throw new ArgumentNullException(nameof(sessions));
        _options = options ?? throw new ArgumentNullException(nameof(options));
    }

    public override async Task<TResponse> UnaryServerHandler<TRequest, TResponse>(
        TRequest request,
        ServerCallContext context,
        UnaryServerMethod<TRequest, TResponse> continuation)
    {
        AttachSession(context);
        try
        {
            return await continuation(request, context).ConfigureAwait(false);
        }
        catch (BrokerAccessDeniedException exception)
        {
            throw BrokerGrpcErrors.FromAccessDenied(exception);
        }
    }

    public override async Task ServerStreamingServerHandler<TRequest, TResponse>(
        TRequest request,
        IServerStreamWriter<TResponse> responseStream,
        ServerCallContext context,
        ServerStreamingServerMethod<TRequest, TResponse> continuation)
    {
        AttachSession(context);
        try
        {
            await continuation(request, responseStream, context).ConfigureAwait(false);
        }
        catch (BrokerAccessDeniedException exception)
        {
            throw BrokerGrpcErrors.FromAccessDenied(exception);
        }
    }

    private void AttachSession(ServerCallContext context)
    {
        var sessionId = ReadHeader(context, BrokerMetadataKeys.SessionId);
        var secret = ReadHeader(context, BrokerMetadataKeys.SessionSecret);
        var now = _options.TimeProvider.GetUtcNow();
        var hasCredentials = _sessions.TryValidateSession(sessionId, secret, now, out var session);

        if (hasCredentials)
        {
            context.UserState[BrokerMetadataKeys.UserStateSession] = session;
        }

        if (BrokerMetadataKeys.IsAnonymousMethod(context.Method))
        {
            // Anonymous methods may optionally be upgraded by credentials (for
            // example HealthProbe returns richer health details to a session).
            return;
        }

        if (!hasCredentials)
        {
            throw BrokerGrpcErrors.Unauthenticated(
                "auth.session.invalid",
                "A valid broker session is required for this operation.");
        }
    }

    internal static string? ReadHeader(ServerCallContext context, string key)
    {
        var entry = context.RequestHeaders.Get(key);
        return entry?.Value;
    }
}
