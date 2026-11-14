using Aether.Contracts.Broker;

namespace Aether.Broker.Transport;

/// <summary>Resolved local transport endpoint for the broker process.</summary>
public sealed record BrokerEndpoint(string Transport, string Endpoint)
{
    public static BrokerEndpoint GenerateDefault(string? transport = null)
    {
        var resolvedTransport = transport ?? DefaultTransport();
        var endpoint = resolvedTransport == BrokerTransports.NamedPipe
            ? "aether-broker-" + Guid.NewGuid().ToString("n")[..16]
            : ShortSocketPath();
        return new BrokerEndpoint(resolvedTransport, endpoint);
    }

    public static string DefaultTransport() =>
        OperatingSystem.IsWindows() ? BrokerTransports.NamedPipe : BrokerTransports.UnixDomainSocket;

    private static string ShortSocketPath()
    {
        var directory = Path.Combine(Path.GetTempPath(), "aether-" + Guid.NewGuid().ToString("n")[..8]);
        Directory.CreateDirectory(directory);
        return Path.Combine(directory, "b.sock");
    }
}
