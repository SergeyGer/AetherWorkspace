namespace Aether.Contracts.Models.Contexts;

/// <summary>
/// Bounded FDC3-aligned instrument context (subset of the FDC3 3.0 instrument
/// context type). Only the fields declared here are accepted in v0.1.
/// </summary>
public sealed class InstrumentContext
{
    public const string ContextType = "fdc3.instrument";
    public const string SchemaVersion = "1.0";

    public string Type { get; init; } = ContextType;

    public string Name { get; init; } = "";

    public InstrumentId Id { get; init; } = new();
}

public sealed class InstrumentId
{
    public string? Ticker { get; init; }

    public string? Isin { get; init; }

    public string? Cusip { get; init; }

    public string? Figi { get; init; }

    public string? Bbg { get; init; }
}
