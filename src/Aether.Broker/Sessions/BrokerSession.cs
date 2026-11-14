using Aether.Contracts;
using Aether.Contracts.Broker;
using Aether.Contracts.Capabilities;
using Aether.Contracts.V1;
using Aether.Broker.Registry;

namespace Aether.Broker.Sessions;

/// <summary>
/// Authenticated session bound to a launched process and an allowlisted app
/// identity. Capabilities and resource grants come from the trusted manifest.
/// Only the session secret hash is stored server-side.
/// </summary>
public sealed class BrokerSession
{
    internal BrokerSession(
        string sessionId,
        RegisteredApp app,
        string instanceId,
        int processId,
        DateTimeOffset processStartTimeUtc,
        string secretHash,
        DateTimeOffset createdAtUtc,
        DateTimeOffset expiresAtUtc)
    {
        SessionId = sessionId;
        App = app;
        InstanceId = instanceId;
        ProcessId = processId;
        ProcessStartTimeUtc = processStartTimeUtc;
        SecretHash = secretHash;
        CreatedAtUtc = createdAtUtc;
        ExpiresAtUtc = expiresAtUtc;
        LastHeartbeatUtc = createdAtUtc;
    }

    public string SessionId { get; }

    public RegisteredApp App { get; }

    public string AppId => App.AppId;

    public string InstanceId { get; }

    public int ProcessId { get; }

    public DateTimeOffset ProcessStartTimeUtc { get; }

    public DateTimeOffset CreatedAtUtc { get; }

    public DateTimeOffset ExpiresAtUtc { get; }

    public DateTimeOffset LastHeartbeatUtc { get; private set; }

    internal string SecretHash { get; }

    public IReadOnlySet<string> Capabilities => App.Capabilities;

    public IReadOnlySet<string> AllowedChannels => App.AllowedChannels;

    public IReadOnlySet<string> AllowedContextTypes => App.AllowedContextTypes;

    public IReadOnlySet<DataClassification> AllowedClassifications => App.AllowedClassifications;

    public DataClassification MaxClassification => App.MaxClassification;

    public bool HasCapability(string capability) => App.Capabilities.Contains(capability);

    public bool IsExpired(DateTimeOffset now) => now >= ExpiresAtUtc;

    internal void TouchHeartbeat(DateTimeOffset now) => LastHeartbeatUtc = now;

    public RegisterReply ToRegisterReply(string sessionSecret, BrokerOptions options) => new()
    {
        SessionId = SessionId,
        SessionSecret = sessionSecret,
        ExpiresAtUnixMs = ExpiresAtUtc.ToUnixTimeMilliseconds(),
        Capabilities = { Capabilities },
        AllowedChannels = { AllowedChannels },
        AllowedContextTypes = { AllowedContextTypes },
        AllowedClassifications = { AllowedClassifications.Select(classification => classification.ToProto()) },
        MaxClassification = MaxClassification.ToProto(),
        HeartbeatIntervalSeconds = options.HeartbeatIntervalSeconds,
        BrokerVersion = options.BrokerVersion,
    };
}
