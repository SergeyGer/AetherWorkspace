using System.Text.Json.Nodes;
using Aether.Contracts;
using Aether.Contracts.Synthetic;
using Aether.Testing;
using Xunit;

namespace Aether.Contracts.Tests;

/// <summary>
/// v1.1 AW-048: the synthetic demo catalog must stay consistent across the context catalog,
/// watchlist, risk and chart fixtures so a Watchlist selection can truthfully synchronize the
/// other two applications for NRST, VEGA and ORIN.
/// </summary>
public class InstrumentCatalogParityTests
{
    private static readonly string[] ExpectedTickers = ["NRST", "VEGA", "ORIN"];

    [Fact]
    public void InstrumentContextsExistForEveryDemoTicker()
    {
        var contexts = LoadArray("samples", "contexts", "contexts.json", "contexts");
        foreach (var ticker in ExpectedTickers)
        {
            var matches = contexts
                .Where(context => context["contextType"]!.GetValue<string>() == "fdc3.instrument")
                .Where(context => context["payload"]!["id"]!["ticker"]!.GetValue<string>() == ticker)
                .ToArray();

            Assert.Single(matches);
            var classification = matches[0]["classification"]!.GetValue<string>();
            Assert.Contains(classification, new[] { "PUBLIC", "INTERNAL", "RESTRICTED" });
        }

        Assert.Contains(contexts, context => context["contextRef"]!.GetValue<string>() == "synthetic.instrument.orin");
    }

    [Fact]
    public void WatchlistRiskAndChartTickerSetsMatchTheCatalog()
    {
        var watchlist = Tickers(LoadArray("samples", "fixtures", "watchlist.json", "instruments"), "ticker");
        var risk = Tickers(LoadArray("samples", "fixtures", "risk.json", "positions"), "ticker");
        var chart = Tickers(LoadArray("samples", "fixtures", "chart.json", "series"), "ticker");

        Assert.Equal(ExpectedTickers, watchlist);
        Assert.Equal(ExpectedTickers, risk);
        Assert.Equal(ExpectedTickers, chart);
    }

    [Fact]
    public void ChartHasDistinctSeriesWithDeterministicPoints()
    {
        var series = LoadArray("samples", "fixtures", "chart.json", "series");

        Assert.Equal(3, series.Length);
        foreach (var item in series)
        {
            var ticker = item["ticker"]!.GetValue<string>();
            var label = item["label"]!.GetValue<string>();
            var points = item["points"]!.AsArray();
            Assert.Contains(ticker, ExpectedTickers);
            Assert.Contains("synthetic", label, StringComparison.OrdinalIgnoreCase);
            Assert.True(points.Count >= 2, $"{ticker} chart series needs at least two points.");
            Assert.All(points, point => Assert.NotNull(point!["month"]));
            Assert.All(points, point => Assert.NotNull(point!["value"]));
        }

        var labels = series.Select(item => item["label"]!.GetValue<string>()).ToArray();
        Assert.Equal(labels.Length, labels.Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public void DemoInstrumentCatalogMatchesApprovedContexts()
    {
        var contexts = LoadArray("samples", "contexts", "contexts.json", "contexts");

        Assert.Equal(ExpectedTickers, DemoInstrumentCatalog.All.Select(instrument => instrument.Ticker).ToArray());
        foreach (var demo in DemoInstrumentCatalog.All)
        {
            var context = contexts.Single(entry => entry["contextRef"]!.GetValue<string>() == demo.ContextRef);
            Assert.Equal("fdc3.instrument", context["contextType"]!.GetValue<string>());
            Assert.Equal(demo.Ticker, context["payload"]!["id"]!["ticker"]!.GetValue<string>());
            Assert.Equal(demo.Classification.ToWireName(), context["classification"]!.GetValue<string>());
            Assert.True(DemoInstrumentCatalog.TryGet(demo.Ticker, out var found));
            Assert.Equal(demo.ContextRef, found.ContextRef);
        }

        Assert.False(DemoInstrumentCatalog.TryGet("NOT-A-TICKER", out _));
        Assert.False(DemoInstrumentCatalog.TryGet(null, out _));
    }

    [Fact]
    public void ChartFixtureRemainsMarkedSyntheticAndPiiFree()
    {
        var content = File.ReadAllText(TestRepo.PathTo("samples", "fixtures", "chart.json"));
        var node = JsonNode.Parse(content)!;
        Assert.True(node["synthetic"]!.GetValue<bool>());
        Assert.Contains("fictitious", content, StringComparison.OrdinalIgnoreCase);
    }

    private static JsonObject[] LoadArray(params string[] parts)
    {
        var path = TestRepo.PathTo(parts[..^1]);
        var root = JsonNode.Parse(File.ReadAllText(path))!;
        return root[parts[^1]]!.AsArray().Select(node => node!.AsObject()).ToArray();
    }

    private static string[] Tickers(JsonObject[] rows, string property) =>
        rows.Select(row => row[property]!.GetValue<string>()).ToArray();
}
