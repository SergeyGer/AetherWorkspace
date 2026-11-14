using System.Collections.Concurrent;
using Aether.Broker.Launching;

namespace Aether.Testing;

/// <summary>Test process-identity resolver; lets handshake tests use known identities.</summary>
public sealed class FakeProcessIdentityResolver : IProcessIdentityResolver
{
    private readonly ConcurrentDictionary<int, ProcessIdentity> _identities = new();

    public void Register(int processId, string executablePath, DateTimeOffset startTimeUtc) =>
        _identities[processId] = new ProcessIdentity(processId, executablePath, startTimeUtc);

    public ProcessIdentity? Resolve(int processId) =>
        _identities.TryGetValue(processId, out var identity) ? identity : null;
}
