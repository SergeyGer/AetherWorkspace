using System.IO;
using System.Text.Json;

namespace Aether.SampleApps;

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

/// <summary>Loads the same synthetic fixtures as the headless samples. No live data.</summary>
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

        return JsonSerializer.Deserialize<T>(File.ReadAllText(path), Options)
            ?? throw new InvalidOperationException($"Fixture '{path}' could not be parsed.");
    }
}
