using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Aether.Contracts;
using System.Windows;
using System.Windows.Interop;
using Aether.Broker.Client;
using Aether.Contracts.Broker;
using Aether.Contracts.Models;
using Aether.Contracts.Models.Contexts;
using Aether.Contracts.V1;
using DomainWindowBounds = Aether.Contracts.Models.WindowBounds;
using DomainWindowState = Aether.Contracts.Models.WindowState;

namespace Aether.SampleApps;

/// <summary>
/// Shared WPF sample-app runtime: authenticates with the launcher token,
/// subscribes to the typed context channel, reports the real top-level window
/// handle for arrangement, and raises typed context events on the UI dispatcher.
/// </summary>
public sealed class SampleRuntime : IAsyncDisposable
{
    private readonly BrokerClientSession _session;
    private readonly string _appId;
    private readonly string _instanceId;
    private readonly string _windowId;
    private readonly CancellationTokenSource _subscriptionCancellation = new();
    private readonly Task _subscriptionTask;

    private SampleRuntime(BrokerClientSession session, string appId, string instanceId, string windowId)
    {
        _session = session;
        _appId = appId;
        _instanceId = instanceId;
        _windowId = windowId;
        _subscriptionTask = RunSubscriptionAsync();
    }

    public event Action<InstrumentContext, ContextEnvelope>? ContextReceived;

    public string InstanceId => _instanceId;

    public static async Task<SampleRuntime> ConnectAsync(SampleUiOptions options, Window window)
    {
        var transport = Environment.GetEnvironmentVariable("AETHER_BROKER_TRANSPORT");
        var endpoint = Environment.GetEnvironmentVariable("AETHER_BROKER_ENDPOINT");
        var token = Environment.GetEnvironmentVariable("AETHER_SESSION_TOKEN");
        var instanceId = Environment.GetEnvironmentVariable("AETHER_INSTANCE_ID") ?? "manual-" + Environment.ProcessId;

        if (string.IsNullOrWhiteSpace(transport) || string.IsNullOrWhiteSpace(endpoint) || string.IsNullOrWhiteSpace(token))
        {
            throw new InvalidOperationException("The sample app must be launched by the broker so it receives a session token.");
        }

        var runtime = new SampleRuntime(
            await BrokerClientSession.ConnectAsync(
                new BrokerEndpointInfo { Transport = transport, Endpoint = endpoint },
                options.AppId,
                token,
                instanceId).ConfigureAwait(true),
            options.AppId,
            instanceId,
            "main");

        await runtime.ReportWindowAsync(window).ConfigureAwait(true);
        return runtime;
    }

    private async Task ReportWindowAsync(Window window)
    {
        var handle = new WindowInteropHelper(window).EnsureHandle();
        var bounds = new DomainWindowBounds(
            (int)Math.Max(0, window.Left),
            (int)Math.Max(0, window.Top),
            (int)Math.Max(320, window.ActualWidth > 0 ? window.ActualWidth : window.Width),
            (int)Math.Max(240, window.ActualHeight > 0 ? window.ActualHeight : window.Height));
        await _session.Client.ReportWindowAsync(
            new DomainWindowState(_appId, _instanceId, _windowId, window.Title, bounds, "virtual-monitor-1", Visible: true, handle),
            TraceIdGenerator.NewTraceId()).ConfigureAwait(true);
    }

    private async Task RunSubscriptionAsync()
    {
        try
        {
            await foreach (var envelope in _session.Client
                .SubscribeAsync(ChannelNames.Default, TraceIdGenerator.NewTraceId(), _subscriptionCancellation.Token)
                .ConfigureAwait(false))
            {
                if (envelope.ContextType != InstrumentContext.ContextType)
                {
                    continue;
                }

                InstrumentContext instrument;
                try
                {
                    instrument = Contracts.Json.AetherJson.DeserializeStrict<InstrumentContext>(envelope.PayloadJson);
                }
                catch (Contracts.Errors.AetherContractException)
                {
                    continue;
                }

                Application.Current?.Dispatcher.BeginInvoke(() => ContextReceived?.Invoke(instrument, envelope));
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (BrokerRpcException)
        {
            // Subscription failure is surfaced by the broker health state; the
            // window stays responsive and offers no privilege escalation.
        }
    }

    public async ValueTask DisposeAsync()
    {
        await _subscriptionCancellation.CancelAsync().ConfigureAwait(false);
        try
        {
            await _subscriptionTask.ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
        }

        _subscriptionCancellation.Dispose();
        await _session.DisposeAsync().ConfigureAwait(false);
    }
}
