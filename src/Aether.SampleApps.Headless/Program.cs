using System.Globalization;
using System.Runtime.InteropServices;
using System.Text.Json;
using Aether.Broker.Client;
using Aether.Contracts;
using Aether.Contracts.Broker;
using Aether.Contracts.Json;
using Aether.Contracts.Models;
using Aether.Contracts.Models.Contexts;
using Aether.Contracts.V1;

namespace Aether.SampleApps.Headless;

/// <summary>
/// Headless instance of one synthetic sample app. It authenticates with the
/// broker using the launch token, subscribes to the typed context channel,
/// reports a deterministic virtual top-level window and renders received
/// context as JSON lines. Used for CI/E2E on any platform and as the headless
/// dual of the WPF sample processes.
/// </summary>
public static class Program
{
    public static async Task<int> Main(string[] args)
    {
        SampleOptions options;
        try
        {
            options = SampleOptions.Parse(args);
        }
        catch (ArgumentException exception)
        {
            Console.Error.WriteLine("sample.invalid_options: " + exception.Message);
            return 2;
        }

        var appId = AgentOrDefault("AETHER_APP_ID", "sample." + options.App);
        var instanceId = AgentOrDefault("AETHER_INSTANCE_ID", "manual-" + Environment.ProcessId.ToString(CultureInfo.InvariantCulture));
        var token = Agent("AETHER_SESSION_TOKEN");
        var transport = Agent("AETHER_BROKER_TRANSPORT");
        var endpoint = Agent("AETHER_BROKER_ENDPOINT");
        var traceId = Agent("AETHER_TRACE_ID");

        if (token is null || transport is null || endpoint is null)
        {
            Console.Error.WriteLine("sample.broker_missing: launch through the broker, or set AETHER_SESSION_TOKEN/AETHER_BROKER_TRANSPORT/AETHER_BROKER_ENDPOINT.");
            return 2;
        }

        if (!TraceContext.IsValidTraceId(traceId))
        {
            traceId = TraceIdGenerator.NewTraceId();
        }

        try
        {
            var fixtures = LoadFixtures(options);
            var endpointInfo = new BrokerEndpointInfo { Transport = transport, Endpoint = endpoint };
            await using var session = await BrokerClientSession.ConnectAsync(endpointInfo, appId, token, instanceId);

            using var shutdown = new CancellationTokenSource();
            Console.CancelKeyPress += (_, eventArgs) =>
            {
                eventArgs.Cancel = true;
                shutdown.Cancel();
            };
            using var sigterm = OperatingSystem.IsWindows()
                ? null
                : PosixSignalRegistration.Create(PosixSignal.SIGTERM, context =>
                {
                    context.Cancel = true;
                    shutdown.Cancel();
                });

            var subscribeTask = Task.Run(() => ConsumeAsync(session.Client, fixtures, appId, instanceId, shutdown.Token));

            var profile = WindowProfile.For(options.App);
            var report = await session.Client.ReportWindowAsync(
                new WindowState(appId, instanceId, profile.WindowId, profile.Title, profile.Bounds, options.MonitorId, Visible: true, options.NativeHandle),
                traceId!).ConfigureAwait(false);
            if (!report.Accepted)
            {
                Console.Error.WriteLine($"sample.window.rejected {report.DenialCode}");
            }

            Console.Out.WriteLine(AetherJson.Serialize(new
            {
                @event = "ready",
                appId,
                instanceId,
                traceId,
                profile = options.App,
                windowId = profile.WindowId,
                bounds = profile.Bounds,
            }));
            Console.Out.Flush();

            if (options.RunSeconds is int seconds)
            {
                shutdown.CancelAfter(TimeSpan.FromSeconds(seconds));
            }

            try
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, shutdown.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
            }

            try
            {
                await subscribeTask.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
            }

            Console.Out.WriteLine(AetherJson.Serialize(new { @event = "stopped", appId, instanceId, traceId }));
            Console.Out.Flush();
            return 0;
        }
        catch (BrokerRpcException exception)
        {
            Console.Error.WriteLine($"sample.broker_error {exception.Code}: {exception.Message}");
            return 3;
        }
        catch (FileNotFoundException exception)
        {
            Console.Error.WriteLine("sample.fixtures_missing: " + exception.Message);
            return 3;
        }
    }

    private static async Task ConsumeAsync(BrokerClient client, SampleFixtures fixtures, string appId, string instanceId, CancellationToken cancellationToken)
    {
        try
        {
            await foreach (var envelope in client.SubscribeAsync(ChannelNames.Default, TraceIdGenerator.NewTraceId(), cancellationToken).ConfigureAwait(false))
            {
                if (envelope.ContextType == OrganizationContext.ContextType)
                {
                    HandleOrganization(envelope, appId, instanceId, fixtures);
                    continue;
                }

                if (envelope.ContextType != InstrumentContext.ContextType)
                {
                    continue;
                }

                InstrumentContext instrument;
                try
                {
                    instrument = AetherJson.DeserializeStrict<InstrumentContext>(envelope.PayloadJson);
                }
                catch (Contracts.Errors.AetherContractException)
                {
                    continue;
                }

                var rendered = Render(fixtures, instrument);
                Console.Out.WriteLine(AetherJson.Serialize(new
                {
                    @event = "context",
                    appId,
                    instanceId,
                    traceId = envelope.TraceId,
                    messageId = envelope.MessageId,
                    contextType = envelope.ContextType,
                    classification = envelope.Classification.FromProto().ToWireName(),
                    provenance = new
                    {
                        sourceAppId = envelope.Provenance?.SourceAppId,
                        sourceInstanceId = envelope.Provenance?.SourceInstanceId,
                        traceId = envelope.Provenance?.TraceId,
                        spanId = envelope.Provenance?.SpanId,
                    },
                    instrument = new
                    {
                        name = instrument.Name,
                        ticker = instrument.Id.Ticker,
                        isin = instrument.Id.Isin,
                    },
                    view = rendered,
                }));
                Console.Out.Flush();
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (BrokerRpcException exception) when (exception.StatusCode == Grpc.Core.StatusCode.Cancelled)
        {
        }
    }

    private static void HandleOrganization(ContextEnvelope envelope, string appId, string instanceId, SampleFixtures fixtures)
    {
        OrganizationContext organization;
        try
        {
            organization = AetherJson.DeserializeStrict<OrganizationContext>(envelope.PayloadJson);
        }
        catch (Contracts.Errors.AetherContractException)
        {
            return;
        }

        Console.Out.WriteLine(AetherJson.Serialize(new
        {
            @event = "context",
            appId,
            instanceId,
            traceId = envelope.TraceId,
            messageId = envelope.MessageId,
            contextType = envelope.ContextType,
            classification = envelope.Classification.FromProto().ToWireName(),
            provenance = new
            {
                sourceAppId = envelope.Provenance?.SourceAppId,
                sourceInstanceId = envelope.Provenance?.SourceInstanceId,
                traceId = envelope.Provenance?.TraceId,
                spanId = envelope.Provenance?.SpanId,
            },
            organization = new
            {
                name = organization.Name,
                lei = organization.Id.Lei,
            },
            view = new
            {
                kind = "organization",
                app = fixtures.App,
                name = organization.Name,
            },
        }));
        Console.Out.Flush();
    }

    private static object Render(SampleFixtures fixtures, InstrumentContext instrument)
    {
        var ticker = instrument.Id.Ticker ?? "";
        return fixtures.App switch
        {
            "watchlist" => new
            {
                kind = "watchlist",
                highlighted = fixtures.Watchlist?.Instruments.Any(row => row.Ticker == ticker) ?? false,
                row = fixtures.Watchlist?.Instruments.FirstOrDefault(row => row.Ticker == ticker),
            },
            "risk" => new
            {
                kind = "risk",
                position = fixtures.Risk?.Positions.FirstOrDefault(row => row.Ticker == ticker),
                portfolioNotional = fixtures.Risk?.Positions.Sum(row => row.Notional),
            },
            "chart" => new
            {
                kind = "chart",
                series = fixtures.Chart?.Series.FirstOrDefault(series => series.Ticker == ticker),
            },
            _ => new { kind = fixtures.App },
        };
    }

    private static SampleFixtures LoadFixtures(SampleOptions options) => new(
        options.App,
        options.App == "watchlist" ? Fixtures.LoadWatchlist(options.FixturesDirectory) : null,
        options.App == "risk" ? Fixtures.LoadRisk(options.FixturesDirectory) : null,
        options.App == "chart" ? Fixtures.LoadChart(options.FixturesDirectory) : null);

    private static string? Agent(string name)
    {
        var value = Environment.GetEnvironmentVariable(name);
        return string.IsNullOrWhiteSpace(value) ? null : value;
    }

    private static string AgentOrDefault(string name, string fallback) => Agent(name) ?? fallback;
}

internal sealed record SampleFixtures(
    string App,
    WatchlistFixture? Watchlist,
    RiskFixture? Risk,
    ChartFixture? Chart);
