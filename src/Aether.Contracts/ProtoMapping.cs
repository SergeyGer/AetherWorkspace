using Aether.Contracts.V1;

namespace Aether.Contracts;

/// <summary>Conversions between domain models and generated protobuf types.</summary>
public static class ProtoMapping
{
    public static Classification ToProto(this DataClassification classification) => classification switch
    {
        DataClassification.Public => Classification.Public,
        DataClassification.Internal => Classification.Internal,
        DataClassification.Restricted => Classification.Restricted,
        _ => Classification.Unknown,
    };

    public static DataClassification FromProto(this Classification classification) => classification switch
    {
        Classification.Public => DataClassification.Public,
        Classification.Internal => DataClassification.Internal,
        Classification.Restricted => DataClassification.Restricted,
        _ => DataClassification.Unknown,
    };

    public static long ToUnixMs(this DateTimeOffset value) => value.ToUnixTimeMilliseconds();

    public static DateTimeOffset FromUnixMs(long value) => DateTimeOffset.FromUnixTimeMilliseconds(value);

    public static Provenance ToProto(this ProvenanceInfo provenance) => new()
    {
        SourceAppId = provenance.SourceAppId,
        SourceInstanceId = provenance.SourceInstanceId,
        TraceId = provenance.TraceId,
        SpanId = provenance.SpanId,
        CreatedAtUnixMs = provenance.CreatedAtUtc.ToUnixTimeMilliseconds(),
    };

    public static ProvenanceInfo ToModel(this Provenance provenance) => new(
        provenance.SourceAppId,
        provenance.SourceInstanceId,
        provenance.TraceId,
        provenance.SpanId,
        DateTimeOffset.FromUnixTimeMilliseconds(provenance.CreatedAtUnixMs));
}

/// <summary>Immutable provenance record attached to context events.</summary>
public sealed record ProvenanceInfo(
    string SourceAppId,
    string SourceInstanceId,
    string TraceId,
    string SpanId,
    DateTimeOffset CreatedAtUtc);
