using Aether.Broker.Catalog;
using Aether.Broker.Channels;
using Aether.Broker.Health;
using Aether.Broker.Intents;
using Aether.Broker.Workspaces;
using Aether.Broker.Hosting;
using Aether.Broker.Launching;
using Aether.Broker.Registry;
using Aether.Broker.Sessions;
using Aether.Broker.Transport;
using Aether.Broker.Windows;
using Aether.Contracts.Broker;
using Aether.Security.Audit;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Aether.Broker;

/// <summary>
/// In-process broker host: owns the local gRPC endpoint, allowlisted registry,
/// sessions, channels, windows, launcher and audit chain. The host can be used by
/// the broker process and by integration tests without a second executable.
/// </summary>
public sealed class BrokerHost : IAsyncDisposable
{
    private readonly WebApplication _app;
    private readonly BrokerEndpoint _endpoint;
    private readonly bool _usesNamedPipe;
    private bool _started;
    private bool _stopped;

    private BrokerHost(
        BrokerOptions options,
        WebApplication app,
        BrokerEndpoint endpoint,
        AppRegistry registry,
        ContextCatalog contexts,
        SessionManager sessions,
        AppLauncher launcher,
        ChannelRouter channels,
        WindowRegistry windows,
        IWindowArranger arranger,
        HealthMonitor health,
        IAuditSink auditSink)
    {
        Options = options;
        _app = app;
        _endpoint = endpoint;
        Registry = registry;
        Contexts = contexts;
        Sessions = sessions;
        Launcher = launcher;
        Channels = channels;
        Windows = windows;
        Arranger = arranger;
        Health = health;
        AuditSink = auditSink;
        _usesNamedPipe = endpoint.Transport == BrokerTransports.NamedPipe;
        EndpointInfo = new BrokerEndpointInfo
        {
            Transport = endpoint.Transport,
            Endpoint = endpoint.Endpoint,
            OperatorAppId = options.OperatorAppId ?? "",
            OperatorToken = sessions.OperatorToken,
            BrokerVersion = options.BrokerVersion,
            PolicyVersion = options.PolicyVersion,
            AuditFile = options.AuditFile is null ? null : Path.GetFullPath(options.AuditFile),
            MaxActionsPerPlan = options.MaxActionsPerPlan,
            WrittenAtUtc = options.TimeProvider.GetUtcNow(),
        };
    }

    public BrokerOptions Options { get; }

    public BrokerEndpoint Endpoint => _endpoint;

    public BrokerEndpointInfo EndpointInfo { get; }

    public AppRegistry Registry { get; }

    public ContextCatalog Contexts { get; }

    public SessionManager Sessions { get; }

    public AppLauncher Launcher { get; }

    public ChannelRouter Channels { get; }

    public WindowRegistry Windows { get; }

    public IWindowArranger Arranger { get; }

    public HealthMonitor Health { get; }

    public IAuditSink AuditSink { get; }

    public static BrokerHost Create(BrokerOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        var validated = options.Validate();

        var auditSink = validated.AuditSink ?? new HashChainAuditLog(
            hmacKey: null,
            store: validated.AuditFile is null
                ? null
                : validated.AuditMaxFileBytes > 0
                    ? new RotatingJsonlAuditStore(validated.AuditFile, validated.AuditMaxFileBytes, validated.AuditMaxFiles)
                    : new JsonlAuditStore(validated.AuditFile),
            timeProvider: validated.TimeProvider,
            continueExistingChain: validated.AuditFile is not null);

        var endpoint = new BrokerEndpoint(
            validated.Transport ?? BrokerEndpoint.DefaultTransport(),
            validated.Endpoint ?? BrokerEndpoint.GenerateDefault(validated.Transport).Endpoint);
        validated.Transport = endpoint.Transport;
        validated.Endpoint = endpoint.Endpoint;

        var identityResolver = validated.ProcessIdentityResolver ?? new SystemProcessIdentityResolver();
        var sessions = new SessionManager(validated, validated.Registry, identityResolver, auditSink);
        var launcher = new AppLauncher(validated, validated.Registry, sessions, identityResolver, auditSink);
        var channels = new ChannelRouter(
            validated,
            validated.Registry.Ordered
                .SelectMany(app => app.AllowedChannels)
                .Distinct(StringComparer.Ordinal));
        var windows = new WindowRegistry(validated);
        var arranger = validated.EnableWin32WindowArrangement && OperatingSystem.IsWindows()
            ? (IWindowArranger)new Win32WindowArranger()
            : new VirtualWindowArranger();
        var health = new HealthMonitor(validated, sessions, launcher);
        var intentRouter = new IntentRouter(validated.Registry, validated.Contexts);
        var displayTopology = validated.DisplayTopology ??
            (OperatingSystem.IsWindows()
                ? (IDisplayTopologyProvider)new Win32DisplayTopology(validated)
                : new VirtualDisplayTopology(validated));
        var layoutStore = string.IsNullOrWhiteSpace(validated.LayoutStoreDirectory)
            ? null
            : new WorkspaceLayoutStore(validated.LayoutStoreDirectory, validated.LayoutMaxBytes);

        var builder = WebApplication.CreateSlimBuilder(new WebApplicationOptions
        {
            ApplicationName = "Aether.Broker",
            EnvironmentName = "Production",
        });
        builder.Logging.ClearProviders();
        builder.Logging.SetMinimumLevel(LogLevel.Warning);

        builder.Services.AddSingleton(new BrokerAuthInterceptor(sessions, validated));
        builder.Services.AddGrpc(grpc => grpc.Interceptors.Add<BrokerAuthInterceptor>());
        builder.Services.AddSingleton(new BrokerGrpcService(
            validated,
            validated.Registry,
            validated.Contexts,
            sessions,
            launcher,
            channels,
            windows,
            arranger,
            health,
            auditSink,
            intentRouter,
            displayTopology,
            layoutStore,
            endpoint.Transport));

        builder.WebHost.ConfigureKestrel(kestrel =>
        {
            if (endpoint.Transport == BrokerTransports.NamedPipe)
            {
                kestrel.ListenNamedPipe(endpoint.Endpoint, listenOptions => listenOptions.Protocols = HttpProtocols.Http2);
            }
            else
            {
                var directory = Path.GetDirectoryName(endpoint.Endpoint);
                if (!string.IsNullOrEmpty(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                if (File.Exists(endpoint.Endpoint))
                {
                    File.Delete(endpoint.Endpoint);
                }

                kestrel.ListenUnixSocket(endpoint.Endpoint, listenOptions => listenOptions.Protocols = HttpProtocols.Http2);
            }
        });

        var app = builder.Build();
        app.MapGrpcService<BrokerGrpcService>();

        return new BrokerHost(validated, app, endpoint, validated.Registry, validated.Contexts, sessions, launcher, channels, windows, arranger, health, auditSink);
    }

    public string DescribeEndpoint() => $"{_endpoint.Transport}:{_endpoint.Endpoint}";

    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        if (_started)
        {
            throw new InvalidOperationException("The broker host is already started.");
        }

        await _app.StartAsync(cancellationToken).ConfigureAwait(false);
        _started = true;
    }

    public Task WaitForShutdownAsync(CancellationToken cancellationToken = default) =>
        _app.WaitForShutdownAsync(cancellationToken);

    public async Task StopAsync()
    {
        if (!_started || _stopped)
        {
            return;
        }

        _stopped = true;
        try
        {
            // Stop child processes and complete channel readers first: graceful
            // gRPC shutdown otherwise waits for their open subscription streams.
            await Launcher.StopAllAsync().ConfigureAwait(false);
            Channels.Dispose();
            Windows.Clear();

            using var stopTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            try
            {
                await _app.StopAsync(stopTimeout.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
            }
        }
        finally
        {
            await _app.DisposeAsync().ConfigureAwait(false);
            if (!_usesNamedPipe && File.Exists(_endpoint.Endpoint))
            {
                try
                {
                    File.Delete(_endpoint.Endpoint);
                }
                catch (IOException)
                {
                }
            }
        }
    }

    public IReadOnlyDictionary<string, string> BuildAuditSummary() =>
        AuditSink.Snapshot()
            .GroupBy(entry => entry.EventType, StringComparer.Ordinal)
            .ToDictionary(
                group => group.Key,
                group => $"{group.Count()} events ({group.Count(entry => entry.Outcome == AuditOutcomes.Denied)} denied)",
                StringComparer.Ordinal);

    public async ValueTask DisposeAsync() => await StopAsync().ConfigureAwait(false);
}
