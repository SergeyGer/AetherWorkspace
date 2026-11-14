using System.Net.Sockets;
using Aether.Contracts.Broker;
using Grpc.Net.Client;

namespace Aether.Broker.Client;

/// <summary>
/// Creates gRPC channels over the local transport. Windows uses named pipes; Unix
/// uses a Unix domain socket. There is no network listener: the address is only a
/// gRPC pseudo-authority and the actual connection is an OS local IPC primitive.
/// </summary>
public static class BrokerChannelFactory
{
    public static GrpcChannel CreateChannel(string transport, string endpoint)
    {
        if (string.IsNullOrWhiteSpace(endpoint))
        {
            throw new ArgumentException("A broker endpoint is required.", nameof(endpoint));
        }

        var handler = new SocketsHttpHandler
        {
            EnableMultipleHttp2Connections = true,
            PooledConnectionIdleTimeout = Timeout.InfiniteTimeSpan,
            KeepAlivePingDelay = TimeSpan.FromSeconds(60),
            KeepAlivePingTimeout = TimeSpan.FromSeconds(30),
            UseProxy = false,
        };

        if (transport == BrokerTransports.NamedPipe)
        {
            handler.ConnectCallback = async (context, cancellationToken) =>
            {
                var pipe = new System.IO.Pipes.NamedPipeClientStream(
                    ".",
                    endpoint,
                    System.IO.Pipes.PipeDirection.InOut,
                    System.IO.Pipes.PipeOptions.Asynchronous);
                pipe.ReadMode = System.IO.Pipes.PipeTransmissionMode.Byte;
                await pipe.ConnectAsync(5000, cancellationToken).ConfigureAwait(false);
                return pipe;
            };
        }
        else if (transport == BrokerTransports.UnixDomainSocket)
        {
            handler.ConnectCallback = async (context, cancellationToken) =>
            {
                var socket = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
                try
                {
                    await socket.ConnectAsync(new UnixDomainSocketEndPoint(endpoint), cancellationToken).ConfigureAwait(false);
                    return new NetworkStream(socket, ownsSocket: true);
                }
                catch
                {
                    socket.Dispose();
                    throw;
                }
            };
        }
        else
        {
            throw new ArgumentException($"Unknown broker transport '{transport}'.", nameof(transport));
        }

        return GrpcChannel.ForAddress("http://localhost", new GrpcChannelOptions
        {
            HttpHandler = handler,
            MaxReceiveMessageSize = 8 * 1024 * 1024,
            MaxSendMessageSize = 1024 * 1024,
        });
    }
}
