namespace Aether.Contracts.Synthetic;

/// <summary>One approved synthetic demo instrument and the catalog context it publishes.</summary>
public sealed record DemoInstrument(string Ticker, string ContextRef, DataClassification Classification);

/// <summary>
/// Approved ticker → trusted-catalog context mapping for the v1.1 interactive demo. A UI selection
/// may only publish these catalog refs; it never constructs arbitrary payloads or URLs.
/// </summary>
public static class DemoInstrumentCatalog
{
    public static IReadOnlyList<DemoInstrument> All { get; } =
    [
        new("NRST", "synthetic.instrument.northstar", DataClassification.Restricted),
        new("VEGA", "synthetic.instrument.vega", DataClassification.Public),
        new("ORIN", "synthetic.instrument.orin", DataClassification.Internal),
    ];

    private static readonly IReadOnlyDictionary<string, DemoInstrument> ByTicker =
        All.ToDictionary(instrument => instrument.Ticker, StringComparer.Ordinal);

    public static bool TryGet(string? ticker, out DemoInstrument instrument)
    {
        if (ticker is not null && ByTicker.TryGetValue(ticker, out var found))
        {
            instrument = found;
            return true;
        }

        instrument = default!;
        return false;
    }
}
