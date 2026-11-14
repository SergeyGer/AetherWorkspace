using Aether.Contracts.Broker;
using Aether.Contracts.V1;
using Grpc.Core;
using Grpc.Net.Client;

namespace Aether.Broker.Client;

/// <summary>
/// Convenience host for sample apps: connects, registers with the token injected
/// by the broker, then maintains a heartbeat loop. No network access is involved.
/// </summary>
public sealed class BrokerClientSession : IAsyncDisposable
{
    private readonly GrpcChannel _channel;
    private readonly CancellationTokenSource _heartbeatCts = new();
    private readonly Task _heartbeatTask;

    private BrokerClientSession(GrpcChannel channel, BrokerClient client, RegisterReply registration)
    {
        _channel = channel;
        Client = client;
        Registration = registration;
        _heartbeatTask = RunHeartbeatAsync();
    }

    public BrokerClient Client { get; }

    public RegisterReply Registration { get; }

    public static async Task<BrokerClientSession> ConnectAsync(
        BrokerEndpointInfo endpointInfo,
        string appId,
        string registrationToken,
        string? instanceId = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(endpointInfo);

        var channel = BrokerChannelFactory.CreateChannel(endpointInfo.Transport, endpointInfo.Endpoint);
        var client = new BrokerClient(channel);
        for (var attempt = 0; attempt < 40; attempt++)
        {
            try
            {
                var reply = await client.RegisterAsync(appId, registrationToken, instanceId, cancellationToken).ConfigureAwait(false);
                return new BrokerClientSession(channel, client, reply);
            }
            catch (BrokerRpcException) when (attempt < 39)
            {
                // Broker endpoint may not be accepting yet or registration token is
                // still being published; back off briefly.
                await Task.Delay(100, cancellationToken).ConfigureAwait(false);
            }
        }

        await client.DisposeAsync().ConfigureAwait(false);
        throw new BrokerRpcException("auth.token.invalid", Contracts.Errors.DenialCategory.PolicyDenied, StatusCode.Unauthenticated, "Registration with the broker failed.");
    }

    private async Task RunHeartbeatAsync()
    {
        var interval = TimeSpan.FromSeconds(Math.Max(1, Registration.HeartbeatIntervalSeconds));
        try
        {
            while (!_heartbeatCts.IsCancellationRequested)
            {
                await Task.Delay(interval, _heartbeatCts.Token).ConfigureAwait(false);
                try
                {
                    await Client.HeartbeatAsync(_heartbeatCts.Token).ConfigureAwait(false);
                }
                catch (BrokerRpcException exception) when (exception.StatusCode == StatusCode.Unauthenticated)
                {
                    return;
                }
            }
        }
        catch (OperationCanceledException)
        {
        }
    }

    public async ValueTask DisposeAsync()
    {
        await _heartbeatCts.CancelAsync().ConfigureAwait(false);
        try
        {
            await _heartbeatTask.ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
        }

        _heartbeatCts.Dispose();
        await Client.DisposeAsync().ConfigureAwait(false);
        _channel.Dispose();
    }
}
