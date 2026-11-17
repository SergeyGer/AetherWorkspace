using Aether.Broker.Registry;
using Aether.Contracts.Models;

namespace Aether.Broker.Windows;

/// <summary>
/// Deterministic side-by-side layout planner. No window reparenting happens:
/// only top-level placement rectangles within the configured work area are
/// computed. The same input always produces the same layout.
/// </summary>
public static class LayoutPlanner
{
    public const int MinimumWindowWidth = 320;

    public const int MinimumWindowHeight = 240;

    public static WindowLayout Plan(
        IReadOnlyList<WindowState> windows,
        IReadOnlyList<RegisteredApp> appOrder,
        string layout,
        BrokerOptions options)
    {
        ArgumentNullException.ThrowIfNull(windows);
        ArgumentNullException.ThrowIfNull(appOrder);
        ArgumentNullException.ThrowIfNull(options);

        if (!Layouts.Supported.Contains(layout))
        {
            throw new ArgumentException($"Unsupported layout '{layout}'.", nameof(layout));
        }

        var orderIndex = appOrder
            .Select((app, index) => (app.AppId, index))
            .ToDictionary(pair => pair.AppId, pair => pair.index, StringComparer.Ordinal);

        var orderedWindows = windows
            .Where(window => window.Visible)
            .OrderBy(window => orderIndex.TryGetValue(window.AppId, out var index) ? index : int.MaxValue)
            .ThenBy(window => window.AppId, StringComparer.Ordinal)
            .ThenBy(window => window.InstanceId, StringComparer.Ordinal)
            .ThenBy(window => window.WindowId, StringComparer.Ordinal)
            .ToArray();

        if (orderedWindows.Length == 0)
        {
            return new WindowLayout(layout, [], options.MonitorId);
        }

        if (string.Equals(layout, Layouts.DemoGrid, StringComparison.Ordinal))
        {
            return PlanDemoGrid(orderedWindows, options);
        }

        var count = orderedWindows.Length;
        var gap = options.WindowGapPixels;
        var available = options.WorkAreaWidth;
        var width = (available - (gap * (count - 1))) / count;
        if (width < MinimumWindowWidth)
        {
            width = Math.Max(1, (available - (gap * (count - 1))) / count);
        }

        var height = options.WorkAreaHeight;
        var placements = new List<WindowPlacement>(count);
        var x = 0;
        for (var index = 0; index < count; index++)
        {
            var window = orderedWindows[index];
            var isLast = index == count - 1;
            var placementWidth = isLast ? Math.Max(1, available - x) : width;
            placements.Add(new WindowPlacement(
                window.AppId,
                window.InstanceId,
                window.WindowId,
                window.Title,
                new WindowBounds(x, 0, placementWidth, height),
                options.MonitorId,
                Visible: true));
            x += placementWidth + gap;
        }

        return new WindowLayout(layout, placements, options.MonitorId);
    }

    private static WindowLayout PlanDemoGrid(IReadOnlyList<WindowState> orderedWindows, BrokerOptions options)
    {
        var count = orderedWindows.Count;
        var gap = Math.Max(0, options.WindowGapPixels);
        var reserve = Math.Clamp(options.DemoLayoutTopReservePixels, 0, Math.Max(0, options.WorkAreaHeight - MinimumWindowHeight));
        var availableWidth = options.WorkAreaWidth;
        var availableHeight = Math.Max(MinimumWindowHeight, options.WorkAreaHeight - reserve);
        var width = Math.Max(MinimumWindowWidth, (availableWidth - (gap * (count - 1))) / count);
        var placements = new List<WindowPlacement>(count);
        var x = 0;
        for (var index = 0; index < count; index++)
        {
            var window = orderedWindows[index];
            var isLast = index == count - 1;
            var placementWidth = isLast ? Math.Max(MinimumWindowWidth, availableWidth - x) : width;
            placements.Add(new WindowPlacement(
                window.AppId,
                window.InstanceId,
                window.WindowId,
                window.Title,
                new WindowBounds(x, reserve, placementWidth, availableHeight),
                options.MonitorId,
                Visible: true));
            x += placementWidth + gap;
        }

        return new WindowLayout(Layouts.DemoGrid, placements, options.MonitorId);
    }
}
