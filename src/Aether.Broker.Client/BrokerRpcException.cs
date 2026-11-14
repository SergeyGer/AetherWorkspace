using Aether.Contracts.Broker;
using Aether.Contracts.Errors;
using Grpc.Core;

namespace Aether.Broker.Client;

/// <summary>Structured broker RPC failure carrying the stable error code from metadata.</summary>
public sealed class BrokerRpcException : Exception
{
    public BrokerRpcException(string code, DenialCategory denialCategory, StatusCode statusCode, string message)
        : base(message)
    {
        Code = code;
        DenialCategory = denialCategory;
        StatusCode = statusCode;
    }

    public string Code { get; }

    public DenialCategory DenialCategory { get; }

    public StatusCode StatusCode { get; }

    public static BrokerRpcException From(RpcException exception)
    {
        var code = exception.Trailers?.Get(BrokerMetadataKeys.ErrorCode)?.Value ?? "broker.rpc.error";
        var categoryText = exception.Trailers?.Get(BrokerMetadataKeys.DenialCategory)?.Value;
        var category = Enum.TryParse<DenialCategory>(categoryText, ignoreCase: true, out var parsed)
            ? parsed
            : AetherErrorCodes.GetDenialCategory(code);

        return new BrokerRpcException(code, category, exception.StatusCode, exception.Status.Detail);
    }
}
