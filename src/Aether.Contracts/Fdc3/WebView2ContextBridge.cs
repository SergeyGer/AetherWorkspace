using System.Diagnostics.CodeAnalysis;
using Aether.Contracts.Json;
using Aether.Contracts.Models.Contexts;
using Aether.Contracts.V1;

namespace Aether.Contracts.Fdc3;

/// <summary>
/// Bounded host-to-JavaScript message contract for the v1.2 WebView2 chart sample.
/// The host validates before posting; JavaScript validates again before rendering. Only
/// the three synthetic demo tickers are accepted and no arbitrary script/payload field is
/// carried across the bridge.
/// </summary>
public static class WebView2ContextBridge
{
    public const string MessageType = "aether.webview2.instrument";
    public const string SchemaVersion = "1.0";
    public static readonly string[] AllowedTickers = ["NRST", "VEGA", "ORIN"];

    /// <summary>Validates the typed instrument and serializes a minimal, allowlisted bridge message.</summary>
    public static string SerializeInstrument(
        InstrumentContext instrument,
        ContextEnvelope envelope,
        IReadOnlyList<WebView2BridgePoint> series)
    {
        ArgumentNullException.ThrowIfNull(instrument);
        ArgumentNullException.ThrowIfNull(envelope);
        ArgumentNullException.ThrowIfNull(series);

        if (!string.Equals(instrument.Type, InstrumentContext.ContextType, StringComparison.Ordinal))
        {
            throw new ArgumentException("Only fdc3.instrument contexts may cross the WebView2 bridge.", nameof(instrument));
        }

        var ticker = instrument.Id.Ticker;
        if (string.IsNullOrWhiteSpace(ticker) || !AllowedTickers.Contains(ticker, StringComparer.Ordinal))
        {
            throw new ArgumentException("The instrument ticker is not in the WebView2 demo allowlist.", nameof(instrument));
        }

        if (series.Count < 2)
        {
            throw new ArgumentException("A WebView2 chart series requires at least two points.", nameof(series));
        }

        var message = new WebView2BridgeMessage
        {
            Type = MessageType,
            SchemaVersion = SchemaVersion,
            Ticker = ticker,
            Name = instrument.Name,
            Classification = envelope.Classification.ToString(),
            TraceId = envelope.TraceId,
            Series = series,
        };
        return AetherJson.Serialize(message);
    }

    /// <summary>
    /// Strictly parses and validates a bridge message. Unknown types, schema versions, fields
    /// or tickers are rejected; JavaScript applies the same allowlist before rendering.
    /// </summary>
    public static bool TryValidate(
        string json,
        IReadOnlyCollection<string> allowedTickers,
        [NotNullWhen(true)] out WebView2BridgeMessage? message,
        out string? error)
    {
        message = null;
        error = null;
        if (string.IsNullOrWhiteSpace(json) || json.Length > 64 * 1024)
        {
            error = "bridge message is empty or too large";
            return false;
        }

        try
        {
            message = AetherJson.DeserializeStrict<WebView2BridgeMessage>(json);
        }
        catch (Exception)
        {
            error = "bridge message is not a valid strict JSON document";
            return false;
        }

        if (!string.Equals(message.Type, MessageType, StringComparison.Ordinal))
        {
            error = "unsupported bridge message type";
            return false;
        }

        if (!string.Equals(message.SchemaVersion, SchemaVersion, StringComparison.Ordinal))
        {
            error = "unsupported bridge schema version";
            return false;
        }

        if (string.IsNullOrWhiteSpace(message.Ticker)
            || !allowedTickers.Contains(message.Ticker, StringComparer.Ordinal))
        {
            error = "ticker is not in the WebView2 allowlist";
            return false;
        }

        if (string.IsNullOrWhiteSpace(message.TraceId)
            || message.Series is null
            || message.Series.Count < 2
            || message.Series.Any(point => string.IsNullOrWhiteSpace(point.Month)))
        {
            error = "bridge message is missing trace or series data";
            return false;
        }

        return true;
    }
}

/// <summary>One point of the local synthetic chart series.</summary>
public sealed class WebView2BridgePoint
{
    public required string Month { get; init; }

    public required decimal Value { get; init; }
}

/// <summary>Strict JSON payload posted from the .NET host to the local JavaScript chart.</summary>
public sealed class WebView2BridgeMessage
{
    public string Type { get; init; } = WebView2ContextBridge.MessageType;

    public string SchemaVersion { get; init; } = WebView2ContextBridge.SchemaVersion;

    public string Ticker { get; init; } = "";

    public string Name { get; init; } = "";

    public string Classification { get; init; } = "";

    public string TraceId { get; init; } = "";

    public IReadOnlyList<WebView2BridgePoint> Series { get; init; } = [];
}
