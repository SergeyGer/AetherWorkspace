using System.Text.Json;
using Aether.Contracts.Models.Contexts;

namespace Aether.SampleApps.Headless;

public sealed record WatchlistFixture(
    string SchemaVersion,
    bool Synthetic,
    IReadOnlyList<WatchlistInstrument> Instruments);

public sealed record WatchlistInstrument(
    string Ticker,
    string Name,
    string Issuer,
    string Rating,
    string Sector,
    string SyntheticInstrumentId);

public sealed record RiskFixture(
    string SchemaVersion,
    bool Synthetic,
    string Currency,
    string AsOf,
    IReadOnlyList<RiskPosition> Positions);

public sealed record RiskPosition(
    string Ticker,
    string Issuer,
    string Rating,
    decimal Notional,
    decimal MarketValue,
    decimal Dv01,
    string SyntheticAccountId);

public sealed record ChartFixture(
    string SchemaVersion,
    bool Synthetic,
    IReadOnlyList<ChartSeries> Series);

public sealed record ChartSeries(string Ticker, string Label, IReadOnlyList<ChartPoint> Points);

public sealed record ChartPoint(string Month, decimal Value);

/// <summary>Loads synthetic fixtures; every value is fictitious and deterministic.</summary>
public static class Fixtures
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    public static WatchlistFixture LoadWatchlist(string directory) =>
        Load<WatchlistFixture>(Path.Combine(directory, "watchlist.json"));

    public static RiskFixture LoadRisk(string directory) =>
        Load<RiskFixture>(Path.Combine(directory, "risk.json"));

    public static ChartFixture LoadChart(string directory) =>
        Load<ChartFixture>(Path.Combine(directory, "chart.json"));

    private static T Load<T>(string path)
    {
        if (!File.Exists(path))
        {
            throw new FileNotFoundException($"Synthetic fixture not found: {path}");
        }

        var fixture = JsonSerializer.Deserialize<T>(File.ReadAllText(path), Options);
        return fixture ?? throw new InvalidOperationException($"Fixture '{path}' could not be parsed.");
    }
}
