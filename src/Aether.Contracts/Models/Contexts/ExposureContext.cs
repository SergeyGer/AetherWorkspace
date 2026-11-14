namespace Aether.Contracts.Models.Contexts;

/// <summary>
/// Synthetic exposure context used by the reference workflow to demonstrate
/// private summarization. All values in fixtures are fictitious.
/// </summary>
public sealed class ExposureContext
{
    public const string ContextType = "aether.exposure";
    public const string SchemaVersion = "1.0";

    public string Type { get; init; } = ContextType;

    public string Name { get; init; } = "";

    public string Currency { get; init; } = "USD";

    public string AsOf { get; init; } = "";

    public IReadOnlyList<ExposurePosition> Positions { get; init; } = [];
}

public sealed class ExposurePosition
{
    public string Issuer { get; init; } = "";

    public string? InstrumentId { get; init; }

    public string Rating { get; init; } = "";

    public decimal Notional { get; init; }

    public decimal MarketValue { get; init; }

    public decimal Dv01 { get; init; }

    /// <summary>Fictitious synthetic account identifier used for detector tests.</summary>
    public string? SyntheticAccountId { get; init; }
}
