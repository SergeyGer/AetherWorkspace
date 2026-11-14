using Aether.Broker.Registry;

namespace Aether.Broker.Sessions;

/// <summary>One-time registration token handed to a freshly launched process.</summary>
internal sealed record PendingRegistration(
    RegisteredApp App,
    string InstanceId,
    string TokenHash,
    string ExpectedExecutablePath,
    int ExpectedProcessId,
    DateTimeOffset ExpectedStartTimeUtc,
    DateTimeOffset ExpiresAtUtc);
