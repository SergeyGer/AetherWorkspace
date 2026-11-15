using System.Globalization;
using Aether.Contracts.Json;
using Aether.Contracts.Models;
using Aether.Contracts.V1;

namespace Aether.Contracts.Fdc3;

/// <summary>
/// Projects the broker's own context provenance envelope onto the FDC3 3.0
/// <c>ContextMetadata</c> wire shape (source app/instance, authoritative timestamp, traceId).
/// This is an interoperability mapping helper only: the broker keeps ownership of
/// authenticated source identity and authoritative timestamps.
/// </summary>
public static class Fdc3MetadataProjection
{
    public static string ToJson(ContextEnvelope envelope) =>
        AetherJson.Serialize(new
        {
            source = new
            {
                appId = envelope.Provenance?.SourceAppId ?? "",
                instanceId = envelope.Provenance?.SourceInstanceId ?? "",
            },
            timestamp = ToIso8601(envelope.CreatedAtUnixMs),
            traceId = envelope.TraceId,
        });

    public static DateTimeOffset FromUnixMilliseconds(long unixMs) =>
        DateTimeOffset.FromUnixTimeMilliseconds(unixMs).ToUniversalTime();

    public static string ToIso8601(long unixMs) =>
        FromUnixMilliseconds(unixMs).ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture);
}
