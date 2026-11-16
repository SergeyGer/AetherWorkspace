using Aether.Broker.Windows;
using Aether.Contracts;
using Aether.Contracts.Errors;
using Aether.Contracts.Json;
using Aether.Contracts.Models;
using Aether.Contracts.Models.Workspaces;
using Aether.Testing;
using Xunit;

namespace Aether.Broker.Tests;

public class WorkspaceLayoutPlannerTests
{
    private static WorkspaceRect Rect(int x, int y, int width, int height) => new(x, y, width, height);

    private static WorkspaceMonitor Monitor(string device, bool primary, WorkspaceRect bounds, WorkspaceRect workArea, double dpiScale = 1.0) =>
        new(device, primary, bounds, workArea, dpiScale);

    private static WorkspaceMonitor PrimaryMonitor(double dpiScale = 1.0) =>
        Monitor("\\\\.\\DISPLAY1", true, Rect(0, 0, 1920, 1200), Rect(0, 0, 1920, 1160), dpiScale);

    private static WorkspaceMonitor SecondaryMonitor(double dpiScale = 1.0) =>
        Monitor("\\\\.\\DISPLAY2", false, Rect(1920, 0, 3440, 1440), Rect(1920, 0, 3440, 1400), dpiScale);

    private static WorkspaceLayout Layout(IReadOnlyList<WorkspaceMonitor> monitors, params WorkspaceBinding[] bindings) =>
        new(WorkspaceLayoutValidation.CurrentSchemaVersion, "layout-test",
            new DateTimeOffset(2026, 11, 16, 10, 0, 0, TimeSpan.Zero), monitors, bindings);

    private static WorkspaceLayout LoadExample()
    {
        var json = File.ReadAllText(TestRepo.PathTo("docs", "contracts", "workspace-layout.example.json"));
        var parsed = WorkspaceLayoutParser.Parse(json);
        Assert.True(parsed.Success, string.Join("; ", parsed.Issues.Select(issue => issue.Message)));
        return parsed.Layout!;
    }

    [Fact]
    public void PublishedExampleParsesAndValidates()
    {
        var layout = LoadExample();

        Assert.Equal(2, layout.Monitors.Count);
        Assert.Equal(2, layout.Bindings.Count);
        Assert.Empty(WorkspaceLayoutValidation.Validate(layout));
    }

    [Fact]
    public void CaptureUsesReportedMonitorAndFallsBackToPrimary()
    {
        var monitors = new[] { PrimaryMonitor(), SecondaryMonitor() };
        var windows = new[]
        {
            new WindowState("sample.watchlist", "inst-1", "win-1", "Watchlist", new WindowBounds(10, 20, 800, 600), "\\\\.\\DISPLAY1", true, 0),
            new WindowState("sample.chart", "inst-2", "win-2", "Chart", new WindowBounds(2000, 50, 1200, 900), "\\\\.\\DISPLAY2", true, 0),
            new WindowState("sample.risk", "inst-3", "win-3", "Risk", new WindowBounds(30, 40, 640, 480), "unknown-display", true, 0),
            new WindowState("sample.hidden", "inst-4", "win-4", "Hidden", new WindowBounds(0, 0, 320, 240), "\\\\.\\DISPLAY1", false, 0),
        };

        var layout = WorkspaceLayoutPlanner.Capture("captured", monitors, windows, new DateTimeOffset(2026, 11, 16, 10, 0, 0, TimeSpan.Zero));

        Assert.Equal(monitors, layout.Monitors);
        Assert.Equal(new[] { "sample.chart", "sample.risk", "sample.watchlist" }, layout.Bindings.Select(binding => binding.AppId));
        Assert.Equal("\\\\.\\DISPLAY2", layout.Bindings.Single(binding => binding.AppId == "sample.chart").MonitorDeviceName);
        Assert.Equal("\\\\.\\DISPLAY1", layout.Bindings.Single(binding => binding.AppId == "sample.risk").MonitorDeviceName);
        Assert.Empty(WorkspaceLayoutValidation.Validate(layout));
    }

    [Fact]
    public void RoundTripThroughStrictJsonPreservesLayout()
    {
        var layout = LoadExample();
        var json = AetherJson.Serialize(layout);

        var parsed = WorkspaceLayoutParser.Parse(json);

        Assert.True(parsed.Success);
        Assert.Equal(json, AetherJson.Serialize(parsed.Layout!));
    }

    [Fact]
    public void RestoreMapsWindowsToTheirSavedMonitors()
    {
        var plan = WorkspaceLayoutPlanner.PlanRestore(LoadExample(), new[] { PrimaryMonitor(), SecondaryMonitor() });

        Assert.Equal(0, plan.ClampedCount);
        Assert.Equal(0, plan.PrimaryFallbackCount);
        Assert.Equal(0, plan.MissingMonitorCount);
        Assert.Equal("\\\\.\\DISPLAY2", plan.Placements.Single(placement => placement.AppId == "sample.chart").MonitorDeviceName);
        Assert.Equal(Rect(0, 0, 960, 1160), plan.Placements.Single(placement => placement.AppId == "sample.watchlist").Bounds);
    }

    [Fact]
    public void RestoreClampsOffscreenAndOversizedWindowsIntoVisibleWorkArea()
    {
        var layout = Layout(
            new[] { PrimaryMonitor() },
            new WorkspaceBinding("sample.watchlist", "\\\\.\\DISPLAY1", Rect(5000, 4000, 5000, 5000), true),
            new WorkspaceBinding("sample.risk", "\\\\.\\DISPLAY1", Rect(-500, -400, 100, 100), true));

        var plan = WorkspaceLayoutPlanner.PlanRestore(layout, new[] { PrimaryMonitor() });

        var watchlist = plan.Placements.Single(placement => placement.AppId == "sample.watchlist");
        Assert.Equal(Rect(0, 0, 1920, 1160), watchlist.Bounds);
        var risk = plan.Placements.Single(placement => placement.AppId == "sample.risk");
        Assert.Equal(Rect(0, 0, 320, 240), risk.Bounds);
        Assert.Equal(2, plan.ClampedCount);
    }

    [Fact]
    public void RestoreFallsBackToPrimaryWhenSavedMonitorIsMissing()
    {
        var layout = LoadExample();

        var plan = WorkspaceLayoutPlanner.PlanRestore(layout, new[] { PrimaryMonitor() });

        Assert.Equal(1, plan.MissingMonitorCount);
        Assert.Equal(1, plan.PrimaryFallbackCount);
        var chart = plan.Placements.Single(placement => placement.AppId == "sample.chart");
        Assert.Equal("\\\\.\\DISPLAY1", chart.MonitorDeviceName);
        Assert.True(chart.Bounds.X >= 0 && chart.Bounds.X + chart.Bounds.Width <= 1920);
        Assert.Contains(plan.Notes, note => note.Contains("unavailable", StringComparison.Ordinal));
    }

    [Fact]
    public void RestoreRescalesGeometryWhenDpiScaleChanged()
    {
        var layout = Layout(
            new[] { PrimaryMonitor(dpiScale: 1.0) },
            new WorkspaceBinding("sample.watchlist", "\\\\.\\DISPLAY1", Rect(100, 100, 800, 600), true));

        var plan = WorkspaceLayoutPlanner.PlanRestore(layout, new[] { PrimaryMonitor(dpiScale: 1.25) });

        var placement = plan.Placements.Single();
        Assert.Equal(1, plan.DpiScaledCount);
        Assert.Equal(Rect(125, 125, 1000, 750), placement.Bounds);
    }

    [Fact]
    public void RestoreWithoutMonitorsPlansNothing()
    {
        var plan = WorkspaceLayoutPlanner.PlanRestore(LoadExample(), []);

        Assert.Empty(plan.Placements);
        Assert.Single(plan.Notes);
    }

    [Fact]
    public void RestoreIsDeterministic()
    {
        var layout = LoadExample();
        var monitors = new[] { SecondaryMonitor(), PrimaryMonitor() };

        var first = WorkspaceLayoutPlanner.PlanRestore(layout, monitors);
        var second = WorkspaceLayoutPlanner.PlanRestore(layout, monitors);

        Assert.Equal(first.Placements, second.Placements);
    }

    [Theory]
    [InlineData("""{"schemaVersion":"1.0","layoutId":"x","createdAtUtc":"2026-11-16T10:00:00Z","monitors":[],"bindings":[],"unknown":1}""", true)]
    [InlineData("""{"schemaVersion":"2.0","layoutId":"x","createdAtUtc":"2026-11-16T10:00:00Z","monitors":[]}""", false)]
    [InlineData("""not-json""", true)]
    public void CorruptLayoutJsonIsRejected(string json, bool unparseable)
    {
        var parsed = WorkspaceLayoutParser.Parse(json);

        Assert.False(parsed.Success);
        Assert.Equal(unparseable, parsed.Layout is null);
        Assert.Contains(parsed.Issues, issue => issue.Code == AetherErrorCodes.WorkspaceLayoutInvalid);
    }

    [Fact]
    public void ValidationRejectsSelfInconsistentLayout()
    {
        var layout = Layout(
            new[] { PrimaryMonitor(), PrimaryMonitor() },
            new WorkspaceBinding("sample.chart", "\\\\.\\MISSING", Rect(0, 0, 100, 100), true),
            new WorkspaceBinding("sample.chart", "\\\\.\\DISPLAY1", Rect(0, 0, 100, 100), true));

        var issues = WorkspaceLayoutValidation.Validate(layout);

        Assert.Contains(issues, issue => issue.Message.Contains("Duplicate monitor", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(issues, issue => issue.Message.Contains("primary", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(issues, issue => issue.Message.Contains("unknown monitor", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(issues, issue => issue.Message.Contains("Duplicate binding", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void InvalidLayoutIdAndRectanglesAreRejected()
    {
        var layout = new WorkspaceLayout(
            WorkspaceLayoutValidation.CurrentSchemaVersion,
            "",
            DateTimeOffset.UnixEpoch,
            new[] { Monitor("", true, Rect(0, 0, 0, 0), Rect(0, 0, -1, -1), 9.0) },
            []);

        var issues = WorkspaceLayoutValidation.Validate(layout);

        Assert.True(issues.Count >= 4);
        Assert.All(issues, issue => Assert.Equal(AetherErrorCodes.WorkspaceLayoutInvalid, issue.Code));
    }
}
