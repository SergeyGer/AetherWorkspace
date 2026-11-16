using System.Text.Json;
using System.Windows.Forms;
using Aether.Contracts;
using Aether.Contracts.Json;
using Aether.Contracts.Models.Workspaces;
using Aether.Testing;
using Xunit;
using Xunit.Abstractions;

namespace Aether.Desktop.E2E.Tests;

/// <summary>
/// v0.5 WT-05 durable multi-monitor layout scenarios on the reference desktop: save/restore
/// keeps every window inside a visible screen, two-monitor placements restore to their saved
/// monitor, a disappeared monitor falls back to the current primary, and corrupt persisted
/// layouts are denied. Windows-only; the dual-monitor scenario requires two physical screens.
/// </summary>
[Trait("Category", "Layout05")]
public sealed class WorkspaceLayoutDesktopTests
{
    private static readonly TimeSpan AppTimeout = TimeSpan.FromSeconds(60);
    private static readonly string[] SampleAppIds = TestData.SampleAppIds;
    private readonly ITestOutputHelper _output;

    public WorkspaceLayoutDesktopTests(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact(Timeout = 240_000)]
    public async Task SaveRestoreKeepsAllWindowsOnVisibleScreens()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        await using var harness = await NamedPipeBrokerHarness.StartAsync("layout-single");
        await using var client = await harness.CreateOperatorClientAsync();
        await LaunchWorkspaceAppsAsync(client, harness);
        var screens = Screen.AllScreens;

        var saved = await client.SaveWorkspaceLayoutAsync("wt05-single", "single-monitor baseline");
        Assert.True(saved.Accepted, saved.DenialCode + ": " + saved.DenialMessage);
        using (var document = JsonDocument.Parse(saved.LayoutJson))
        {
            Assert.True(document.RootElement.GetProperty("monitors").GetArrayLength() >= 1);
            Assert.Equal(SampleAppIds.Length, document.RootElement.GetProperty("bindings").GetArrayLength());
        }

        var arrangement = await client.ArrangeWindowsAsync("side-by-side", TraceIdGenerator.NewTraceId());
        Assert.True(arrangement.Applied, arrangement.DenialCode + ": " + arrangement.DenialMessage);

        var restored = await client.RestoreWorkspaceLayoutAsync("wt05-single");
        Assert.True(restored.Applied, restored.DenialCode + ": " + restored.DenialMessage);
        Assert.Equal(SampleAppIds.Length, restored.Placements.Count);
        foreach (var placement in restored.Placements)
        {
            Assert.Contains(screens, screen => IsInside(placement, screen.Bounds));
        }
    }

    [Fact(Timeout = 240_000)]
    public async Task DualMonitorLayoutRestoresSecondaryMonitorPlacements()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var screens = Screen.AllScreens;
        if (screens.Length < 2)
        {
            _output.WriteLine("Dual-monitor scenario not executed: only one screen is attached (SKIPPED_HARDWARE on the CI VM).");
            return;
        }

        var primary = screens.Single(screen => screen.Primary);
        var secondary = screens.First(screen => !screen.Primary);
        await using var harness = await NamedPipeBrokerHarness.StartAsync("layout-dual");
        await using var client = await harness.CreateOperatorClientAsync();
        await LaunchWorkspaceAppsAsync(client, harness);

        var layout = new WorkspaceLayout(
            WorkspaceLayoutValidation.CurrentSchemaVersion,
            "wt05-dual",
            DateTimeOffset.UtcNow,
            [ToMonitor(primary, primary: true), ToMonitor(secondary, primary: false)],
            [
                new WorkspaceBinding(SampleAppIds[0], primary.DeviceName, new WorkspaceRect(primary.Bounds.X + 20, primary.Bounds.Y + 20, 800, 600), true),
                new WorkspaceBinding(SampleAppIds[^1], secondary.DeviceName, new WorkspaceRect(secondary.Bounds.X + 40, secondary.Bounds.Y + 40, 900, 700), true),
            ]);
        Directory.CreateDirectory(harness.LayoutStoreDirectory);
        File.WriteAllText(Path.Combine(harness.LayoutStoreDirectory, "wt05-dual.json"), AetherJson.SerializeIndented(layout));

        var restored = await client.RestoreWorkspaceLayoutAsync("wt05-dual");

        Assert.True(restored.Applied, restored.DenialCode + ": " + restored.DenialMessage);
        Assert.True(restored.Placements.Count >= 2);
        var secondaryPlacement = restored.Placements.Single(placement => placement.AppId == SampleAppIds[^1]);
        Assert.Equal(secondary.DeviceName, secondaryPlacement.MonitorDeviceName);
        Assert.True(IsInside(secondaryPlacement, secondary.Bounds), "The restored window is not inside the secondary screen bounds.");
        Assert.Equal(0, restored.MissingMonitorCount);
    }

    [Fact(Timeout = 240_000)]
    public async Task MissingMonitorFallsBackToAPresentScreen()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        await using var harness = await NamedPipeBrokerHarness.StartAsync("layout-fallback");
        await using var client = await harness.CreateOperatorClientAsync();
        await LaunchWorkspaceAppsAsync(client, harness);

        var fallbackRect = new WorkspaceRect(0, 0, 1280, 800);
        var layout = new WorkspaceLayout(
            WorkspaceLayoutValidation.CurrentSchemaVersion,
            "wt05-missing-monitor",
            DateTimeOffset.UtcNow,
            [new WorkspaceMonitor("\\\\.\\DISPLAY9", true, fallbackRect, fallbackRect, 1.0)],
            [new WorkspaceBinding(SampleAppIds[0], "\\\\.\\DISPLAY9", new WorkspaceRect(10, 10, 900, 600), true)]);
        Directory.CreateDirectory(harness.LayoutStoreDirectory);
        File.WriteAllText(Path.Combine(harness.LayoutStoreDirectory, "wt05-missing-monitor.json"), AetherJson.SerializeIndented(layout));

        var restored = await client.RestoreWorkspaceLayoutAsync("wt05-missing-monitor");

        Assert.True(restored.Applied, restored.DenialCode + ": " + restored.DenialMessage);
        Assert.True(restored.MissingMonitorCount >= 1);
        Assert.True(restored.PrimaryFallbackCount >= 1);
        var placement = restored.Placements.Single();
        var screens = Screen.AllScreens;
        Assert.Contains(screens, screen => screen.DeviceName == placement.MonitorDeviceName);
        Assert.Contains(screens, screen => IsInside(placement, screen.Bounds));
    }

    [Fact(Timeout = 180_000)]
    public async Task CorruptPersistedLayoutIsDeniedWithoutTouchingWindows()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        await using var harness = await NamedPipeBrokerHarness.StartAsync("layout-corrupt");
        await using var client = await harness.CreateOperatorClientAsync();
        Directory.CreateDirectory(harness.LayoutStoreDirectory);
        File.WriteAllText(Path.Combine(harness.LayoutStoreDirectory, "wt05-corrupt.json"), "not-json");

        var restored = await client.RestoreWorkspaceLayoutAsync("wt05-corrupt");

        Assert.False(restored.Applied);
        Assert.Equal(Aether.Contracts.Errors.AetherErrorCodes.WorkspaceLayoutInvalid, restored.DenialCode);
    }

    private static async Task LaunchWorkspaceAppsAsync(Aether.Broker.Client.BrokerClient client, NamedPipeBrokerHarness harness)
    {
        foreach (var appId in SampleAppIds)
        {
            var launched = await client.LaunchAppAsync(appId, TraceIdGenerator.NewTraceId());
            Assert.True(launched.Accepted, launched.DenialCode + ": " + launched.DenialMessage);
        }

        await harness.WaitForAppHealthAsync(client, SampleAppIds, AppTimeout);
    }

    private static WorkspaceMonitor ToMonitor(Screen screen, bool primary) =>
        new(screen.DeviceName, primary, ToRect(screen.Bounds), ToRect(screen.Bounds), 1.0);

    private static WorkspaceRect ToRect(Rectangle bounds) => new(bounds.X, bounds.Y, bounds.Width, bounds.Height);

    private static bool IsInside(Aether.Contracts.V1.WorkspaceLayoutPlacement placement, Rectangle bounds) =>
        placement.X >= bounds.X &&
        placement.Y >= bounds.Y &&
        placement.X + placement.Width <= bounds.Right &&
        placement.Y + placement.Height <= bounds.Bottom;
}
