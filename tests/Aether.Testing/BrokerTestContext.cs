using Aether.Broker;
using Aether.Broker.Client;
using Aether.Broker.Launching;
using Aether.Broker.Transport;
using Aether.Contracts.Broker;
using Aether.Contracts.V1;

namespace Aether.Testing;

/// <summary>Starts an in-process broker over a short Unix-domain-socket path.</summary>
public sealed class BrokerTestContext : IAsyncDisposable
{
    private BrokerTestContext(BrokerHost host, string directory)
    {
        Host = host;
        Directory = directory;
    }

    public BrokerHost Host { get; }

    public string Directory { get; }

    public BrokerEndpointInfo Endpoint => Host.EndpointInfo;

    public static BrokerTestContext Create(
        IProcessIdentityResolver? identityResolver = null,
        TimeProvider? timeProvider = null,
        string? testName = null)
    {
        var directory = Path.Combine(Path.GetTempPath(), "aether-" + (testName ?? "t") + "-" + Guid.NewGuid().ToString("n")[..6]);
        System.IO.Directory.CreateDirectory(directory);
        var socketPath = Path.Combine(directory, "b.sock");
        var options = TestBrokerFactory.CreateOptions(
            socketPath,
            auditFile: Path.Combine(directory, "audit.jsonl"),
            childLogDirectory: Path.Combine(directory, "child-logs"),
            identityResolver: identityResolver,
            timeProvider: timeProvider);
        return new BrokerTestContext(BrokerHost.Create(options), directory);
    }

    public static BrokerTestContext CreateWith(BrokerOptions options)
    {
        var directory = Path.Combine(Path.GetTempPath(), "aether-" + Guid.NewGuid().ToString("n")[..8]);
        System.IO.Directory.CreateDirectory(directory);
        var endpoint = new BrokerEndpoint(options.Transport ?? BrokerEndpoint.DefaultTransport(),
            options.Endpoint ?? Path.Combine(directory, "b.sock"));
        options.Transport = endpoint.Transport;
        options.Endpoint = endpoint.Endpoint;
        return new BrokerTestContext(BrokerHost.Create(options), directory);
    }

    public async Task StartAsync(CancellationToken cancellationToken = default) =>
        await Host.StartAsync(cancellationToken).ConfigureAwait(false);

    public BrokerClient CreateClient() =>
        new(BrokerChannelFactory.CreateChannel(Endpoint.Transport, Endpoint.Endpoint));

    public async Task<BrokerClient> RegisterOperatorAsync(CancellationToken cancellationToken = default)
    {
        var client = CreateClient();
        await client.RegisterAsync(Endpoint.OperatorAppId, Endpoint.OperatorToken, "op-" + Guid.NewGuid().ToString("n")[..8], cancellationToken)
            .ConfigureAwait(false);
        return client;
    }

    public async ValueTask DisposeAsync()
    {
        await Host.DisposeAsync().ConfigureAwait(false);
        try
        {
            if (System.IO.Directory.Exists(Directory))
            {
                System.IO.Directory.Delete(Directory, recursive: true);
            }
        }
        catch (IOException)
        {
        }
    }
}
