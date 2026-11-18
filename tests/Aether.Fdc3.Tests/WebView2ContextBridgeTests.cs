using Aether.Contracts.Fdc3;
using Aether.Contracts.Models.Contexts;
using Aether.Contracts.V1;
using Xunit;

namespace Aether.Fdc3.Tests;

/// <summary>
/// v1.2 AW-058: the host-to-JavaScript bridge accepts only the bounded fdc3.instrument
/// projection for the three synthetic demo tickers and rejects unknown payloads.
/// </summary>
public sealed class WebView2ContextBridgeTests
{
    private static readonly IReadOnlyList<WebView2BridgePoint> Series =
    [
        new() { Month = "2026-01", Value = 4.1m },
        new() { Month = "2026-02", Value = 4.4m },
    ];

    [Fact]
    public void SerializesOnlyTheAllowlistedInstrumentProjection()
    {
        var json = WebView2ContextBridge.SerializeInstrument(Instrument("VEGA", "Vega Energy 2029 Note"), Envelope("trace-vega"), Series);

        Assert.True(WebView2ContextBridge.TryValidate(json, WebView2ContextBridge.AllowedTickers, out var message, out var error), error);
        Assert.NotNull(message);
        Assert.Equal("aether.webview2.instrument", message.Type);
        Assert.Equal("1.0", message.SchemaVersion);
        Assert.Equal("VEGA", message.Ticker);
        Assert.Equal("Vega Energy 2029 Note", message.Name);
        Assert.Equal("Public", message.Classification);
        Assert.Equal("trace-vega", message.TraceId);
        Assert.Equal(2, message.Series.Count);
    }

    [Theory]
    [InlineData("NRST")]
    [InlineData("ORIN")]
    public void AllowsEveryBoundedDemoTicker(string ticker)
    {
        var json = WebView2ContextBridge.SerializeInstrument(Instrument(ticker, "Synthetic instrument"), Envelope("trace-" + ticker), Series);
        Assert.True(WebView2ContextBridge.TryValidate(json, WebView2ContextBridge.AllowedTickers, out var message, out var error), error);
        Assert.Equal(ticker, message!.Ticker);
    }

    [Fact]
    public void RejectsTickerOutsideTheAllowlist()
    {
        var json = """
        {"type":"aether.webview2.instrument","schemaVersion":"1.0","ticker":"EVIL","name":"Unknown",
         "classification":"Public","traceId":"trace-1","series":[{"month":"2026-01","value":1},{"month":"2026-02","value":2}]}
        """;

        Assert.False(WebView2ContextBridge.TryValidate(json, WebView2ContextBridge.AllowedTickers, out _, out var error));
        Assert.Contains("allowlist", error, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("aether.webview2.unknown")]
    [InlineData("fdc3.instrument")]
    public void RejectsUnknownBridgeType(string type)
    {
        var json = "{\"type\":\"" + type + "\",\"schemaVersion\":\"1.0\",\"ticker\":\"VEGA\",\"name\":\"Vega\","
                   + "\"classification\":\"Public\",\"traceId\":\"trace-1\",\"series\":[{\"month\":\"2026-01\",\"value\":1},{\"month\":\"2026-02\",\"value\":2}]}";

        Assert.False(WebView2ContextBridge.TryValidate(json, WebView2ContextBridge.AllowedTickers, out _, out var error));
        Assert.Contains("type", error, StringComparison.Ordinal);
    }

    [Fact]
    public void RejectsWrongSchemaVersion()
    {
        var json = """
        {"type":"aether.webview2.instrument","schemaVersion":"2.0","ticker":"VEGA","name":"Vega",
         "classification":"Public","traceId":"trace-1","series":[{"month":"2026-01","value":1},{"month":"2026-02","value":2}]}
        """;

        Assert.False(WebView2ContextBridge.TryValidate(json, WebView2ContextBridge.AllowedTickers, out _, out var error));
        Assert.Contains("schema", error, StringComparison.Ordinal);
    }

    [Fact]
    public void RejectsUnknownFieldsAndMalformedDocuments()
    {
        var unknownField = """
        {"type":"aether.webview2.instrument","schemaVersion":"1.0","ticker":"VEGA","name":"Vega",
         "classification":"Public","traceId":"trace-1","series":[{"month":"2026-01","value":1},{"month":"2026-02","value":2}],
         "execute":"alert(1)"}
        """;

        Assert.False(WebView2ContextBridge.TryValidate(unknownField, WebView2ContextBridge.AllowedTickers, out _, out var unknownFieldError));
        Assert.Contains("strict JSON", unknownFieldError, StringComparison.Ordinal);

        Assert.False(WebView2ContextBridge.TryValidate("{not-json", WebView2ContextBridge.AllowedTickers, out _, out var malformedError));
        Assert.Contains("strict JSON", malformedError, StringComparison.Ordinal);
    }

    [Fact]
    public void RejectsMissingSeriesOrTrace()
    {
        var noSeries = """
        {"type":"aether.webview2.instrument","schemaVersion":"1.0","ticker":"VEGA","name":"Vega",
         "classification":"Public","traceId":"trace-1","series":[]}
        """;

        Assert.False(WebView2ContextBridge.TryValidate(noSeries, WebView2ContextBridge.AllowedTickers, out _, out var error));
        Assert.Contains("trace or series", error, StringComparison.Ordinal);
    }

    [Fact]
    public void ThrowsWhenTheHostTriesToProjectAnUnapprovedTicker()
    {
        var instrument = Instrument("EVIL", "Unknown");
        Assert.Throws<ArgumentException>(() =>
            WebView2ContextBridge.SerializeInstrument(instrument, Envelope("trace-1"), Series));
    }

    private static InstrumentContext Instrument(string ticker, string name) => new()
    {
        Type = InstrumentContext.ContextType,
        Name = name,
        Id = new InstrumentId { Ticker = ticker },
    };

    private static ContextEnvelope Envelope(string traceId) => new()
    {
        TraceId = traceId,
        ContextType = InstrumentContext.ContextType,
        Classification = Classification.Public,
    };
}
