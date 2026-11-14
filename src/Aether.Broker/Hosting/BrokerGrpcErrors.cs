using Aether.Contracts.Errors;
using Aether.Contracts.Broker;
using Grpc.Core;

namespace Aether.Broker.Hosting;

/// <summary>Maps structured broker denials to gRPC status + metadata.</summary>
public static class BrokerGrpcErrors
{
    public static RpcException PermissionDenied(string code, string message) =>
        Build(StatusCode.PermissionDenied, code, message);

    public static RpcException Unauthenticated(string code, string message) =>
        Build(StatusCode.Unauthenticated, code, message);

    public static RpcException InvalidArgument(string code, string message) =>
        Build(StatusCode.InvalidArgument, code, message);

    public static RpcException FromAccessDenied(Channels.BrokerAccessDeniedException exception) =>
        PermissionDenied(exception.Code, exception.Message);

    private static RpcException Build(StatusCode status, string code, string message)
    {
        var metadata = new Metadata
        {
            { BrokerMetadataKeys.ErrorCode, code },
            { BrokerMetadataKeys.DenialCategory, AetherErrorCodes.GetDenialCategory(code).ToString() },
        };
        return new RpcException(new Status(status, message), metadata);
    }
}
