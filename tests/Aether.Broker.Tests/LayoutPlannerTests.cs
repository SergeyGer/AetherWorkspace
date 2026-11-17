using Aether.Broker;
using Aether.Broker.Windows;
using Aether.Contracts.Models;
using Aether.Testing;
using Xunit;

namespace Aether.Broker.Tests;

public class LayoutPlannerTests
{
    private static BrokerOptions Options() => new()
    {
        Registry = TestData.CreateRegistry(),
        Contexts = TestData.CreateContextCatalog(),
        WorkAreaWidth = 1920,
        WorkAreaHeight = 1080,
        WindowGapPixels = 8,
        MonitorId = "virtual-monitor-1",
    };

    private static WindowState Window(string appId, int x = 0) => new(
        appId,
        "inst-" + appId,
        "main",
        appId + " window",
        new WindowBounds(x, 0, 800, 600),
        "virtual-monitor-1",
        Visible: true,
        NativeHandle: 0);

    [Fact]
    public void SideBySideLayoutIsDeterministicAndCoversTheWorkArea()
    {
        var options = Options();
        var windows = new[] { Window("sample.chart"), Window("sample.watchlist"), Window("sample.risk") };
        var layout = LayoutPlanner.Plan(windows, options.Registry.Ordered, Layouts.SideBySide, options);

        Assert.Equal(3, layout.Placements.Count);
        Assert.Equal(["sample.watchlist", "sample.risk", "sample.chart"], layout.Placements.Select(placement => placement.AppId));
        Assert.Equal(new WindowBounds(0, 0, 634, 1080), layout.Placements[0].Bounds);
        Assert.Equal(new WindowBounds(642, 0, 634, 1080), layout.Placements[1].Bounds);
        Assert.Equal(new WindowBounds(1284, 0, 636, 1080), layout.Placements[2].Bounds);
        Assert.All(layout.Placements, placement => Assert.Equal("virtual-monitor-1", placement.MonitorId));

        var secondRun = LayoutPlanner.Plan(windows, options.Registry.Ordered, Layouts.SideBySide, options);
        Assert.Equal(layout.Layout, secondRun.Layout);
        Assert.Equal(layout.Placements.Count, secondRun.Placements.Count);
        for (var index = 0; index < layout.Placements.Count; index++)
        {
            Assert.Equal(layout.Placements[index], secondRun.Placements[index]);
        }
    }

    [Fact]
    public void DemoGridReservesTopStripForCommandSurfaceAndStaysWithinWorkArea()
    {
        var options = Options();
        var windows = new[] { Window("sample.watchlist"), Window("sample.risk"), Window("sample.chart") };

        var layout = LayoutPlanner.Plan(windows, options.Registry.Ordered, Layouts.DemoGrid, options);

        Assert.Equal(Layouts.DemoGrid, layout.Layout);
        Assert.Equal(3, layout.Placements.Count);
        Assert.All(layout.Placements, placement =>
        {
            Assert.Equal(240, placement.Bounds.Y);
            Assert.Equal(840, placement.Bounds.Height);
            Assert.True(placement.Bounds.X >= 0);
            Assert.True(placement.Bounds.X + placement.Bounds.Width <= options.WorkAreaWidth);
            Assert.True(placement.Bounds.Height >= LayoutPlanner.MinimumWindowHeight);
        });
        Assert.Equal(0, layout.Placements[0].Bounds.X);
        Assert.Equal(642, layout.Placements[1].Bounds.X);
        Assert.Equal(1284, layout.Placements[2].Bounds.X);
        Assert.Equal([ "sample.watchlist", "sample.risk", "sample.chart" ], layout.Placements.Select(placement => placement.AppId));
    }

    [Fact]
    public void PlacementsDoNotOverlap()
    {
        var options = Options();
        var windows = new[] { Window("sample.chart"), Window("sample.watchlist"), Window("sample.risk") };
        var layout = LayoutPlanner.Plan(windows, options.Registry.Ordered, Layouts.SideBySide, options);

        for (var index = 1; index < layout.Placements.Count; index++)
        {
            var previous = layout.Placements[index - 1].Bounds;
            var current = layout.Placements[index].Bounds;
            Assert.True(previous.X + previous.Width <= current.X);
        }
    }

    [Fact]
    public void InvisibleWindowsAreIgnored()
    {
        var options = Options();
        var hidden = Window("sample.risk") with { Visible = false };
        var layout = LayoutPlanner.Plan([Window("sample.watchlist"), hidden], options.Registry.Ordered, Layouts.SideBySide, options);

        Assert.Single(layout.Placements);
        Assert.Equal("sample.watchlist", layout.Placements[0].AppId);
    }

    [Fact]
    public void EmptyWindowSetProducesEmptyLayout()
    {
        var options = Options();
        var layout = LayoutPlanner.Plan([], options.Registry.Ordered, Layouts.SideBySide, options);

        Assert.Empty(layout.Placements);
        Assert.Equal(Layouts.SideBySide, layout.Layout);
    }

    [Fact]
    public void UnsupportedLayoutIsRejected()
    {
        var options = Options();
        Assert.Throws<ArgumentException>(() =>
            LayoutPlanner.Plan([Window("sample.watchlist")], options.Registry.Ordered, "mosaic", options));
    }
}
