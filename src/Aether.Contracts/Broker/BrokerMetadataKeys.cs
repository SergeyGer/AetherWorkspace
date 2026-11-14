namespace Aether.Contracts.Broker;

/// <summary>Shared gRPC metadata keys for session authentication and error details.</summary>
public static class BrokerMetadataKeys
{
    public const string SessionId = "x-aether-session-id";
    public const string SessionSecret = "x-aether-session-secret";
    public const string TraceId = "x-aether-trace-id";
    public const string ErrorCode = "aether-error-code";
    public const string DenialCategory = "aether-denial-category";

    public const string UserStateSession = "aether.session";

    /// <summary>Methods callable without a session. Register performs its own token validation.</summary>
    public static bool IsAnonymousMethod(string? methodPath) =>
        methodPath is not null &&
        (methodPath.EndsWith("/Register", StringComparison.Ordinal) ||
         methodPath.EndsWith("/HealthProbe", StringComparison.Ordinal));
}
