using Aether.Contracts.Models;
using Aether.Contracts.Models.Workspaces;

namespace Aether.Broker.Windows;

/// <summary>One restored window placement in virtual-desktop pixels.</summary>
public sealed record WorkspaceRestorePlacement(
    string AppId,
    string MonitorDeviceName,
    WorkspaceRect Bounds,
    bool Visible);

/// <summary>Result of planning a durable layout restore against the current topology.</summary>
public sealed record WorkspaceRestorePlan(
    IReadOnlyList<WorkspaceRestorePlacement> Placements,
    int ClampedCount,
    int PrimaryFallbackCount,
    int MissingMonitorCount,
    int DpiScaledCount,
    IReadOnlyList<string> Notes);

/// <summary>
/// Captures and restores durable multi-monitor workspace layouts. Restore clamps
/// every window into a visible work area, falls back to the current primary
/// monitor when a saved monitor disappears, and rescales geometry when the DPI
/// scale changed. No window reparenting or offscreen placement is ever planned.
/// </summary>
public static class WorkspaceLayoutPlanner
{
    public const int MinimumWindowWidth = 320;
    public const int MinimumWindowHeight = 240;

    public static WorkspaceLayout Capture(
        string layoutId,
        IReadOnlyList<WorkspaceMonitor> monitors,
        IReadOnlyList<WindowState> windows,
        DateTimeOffset createdAtUtc,
        string? description = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(layoutId);
        ArgumentNullException.ThrowIfNull(monitors);
        ArgumentNullException.ThrowIfNull(windows);
        if (monitors.Count == 0)
        {
            throw new ArgumentException("At least one monitor is required to capture a layout.", nameof(monitors));
        }

        var primary = monitors.FirstOrDefault(monitor => monitor.Primary) ?? monitors[0];
        var byDevice = new Dictionary<string, WorkspaceMonitor>(StringComparer.OrdinalIgnoreCase);
        foreach (var monitor in monitors)
        {
            byDevice[monitor.DeviceName] = monitor;
        }

        var bindings = windows
            .Where(window => window.Visible)
            .GroupBy(window => window.AppId, StringComparer.Ordinal)
            .Select(group => group.OrderBy(window => window.InstanceId, StringComparer.Ordinal).First())
            .OrderBy(window => window.AppId, StringComparer.Ordinal)
            .Select(window =>
            {
                var monitor = byDevice.TryGetValue(window.MonitorId, out var reported) ? reported : primary;
                return new WorkspaceBinding(
                    window.AppId,
                    monitor.DeviceName,
                    new WorkspaceRect(window.Bounds.X, window.Bounds.Y, window.Bounds.Width, window.Bounds.Height),
                    window.Visible);
            })
            .ToArray();

        return new WorkspaceLayout(
            WorkspaceLayoutValidation.CurrentSchemaVersion,
            layoutId,
            createdAtUtc,
            monitors,
            bindings,
            description);
    }

    public static WorkspaceRestorePlan PlanRestore(
        WorkspaceLayout layout,
        IReadOnlyList<WorkspaceMonitor> currentMonitors)
    {
        ArgumentNullException.ThrowIfNull(layout);
        ArgumentNullException.ThrowIfNull(currentMonitors);

        if (currentMonitors.Count == 0)
        {
            return new WorkspaceRestorePlan([], 0, 0, layout.Bindings.Count, 0,
                ["No current monitors are available; restore is denied rather than placing windows offscreen."]);
        }

        var primary = currentMonitors.FirstOrDefault(monitor => monitor.Primary) ?? currentMonitors[0];
        var byDevice = new Dictionary<string, WorkspaceMonitor>(StringComparer.OrdinalIgnoreCase);
        foreach (var monitor in currentMonitors)
        {
            byDevice[monitor.DeviceName] = monitor;
        }

        var savedMonitors = new Dictionary<string, WorkspaceMonitor>(StringComparer.OrdinalIgnoreCase);
        foreach (var monitor in layout.Monitors)
        {
            savedMonitors[monitor.DeviceName] = monitor;
        }

        var placements = new List<WorkspaceRestorePlacement>();
        var notes = new List<string>();
        var clamped = 0;
        var fallback = 0;
        var missing = 0;
        var dpiScaled = 0;

        foreach (var binding in layout.Bindings.OrderBy(item => item.AppId, StringComparer.Ordinal))
        {
            var target = primary;
            if (!byDevice.TryGetValue(binding.MonitorDeviceName, out var matchedTarget))
            {
                missing++;
                fallback++;
                notes.Add($"Monitor '{binding.MonitorDeviceName}' is unavailable; '{binding.AppId}' restored on primary '{primary.DeviceName}'.");
            }
            else
            {
                target = matchedTarget;
            }

            var desired = binding.Bounds;
            if (savedMonitors.TryGetValue(binding.MonitorDeviceName, out var savedMonitor) &&
                target.DpiScale > 0 &&
                Math.Abs(savedMonitor.DpiScale - target.DpiScale) > 0.001)
            {
                var ratio = target.DpiScale / savedMonitor.DpiScale;
                desired = Scale(desired, ratio);
                dpiScaled++;
            }

            var clampedRect = Clamp(desired, target.WorkArea);
            if (clampedRect != desired)
            {
                clamped++;
            }

            placements.Add(new WorkspaceRestorePlacement(binding.AppId, target.DeviceName, clampedRect, binding.Visible));
        }

        return new WorkspaceRestorePlan(placements, clamped, fallback, missing, dpiScaled, notes);
    }

    private static WorkspaceRect Scale(WorkspaceRect rect, double ratio) =>
        new(
            (int)Math.Round(rect.X * ratio, MidpointRounding.AwayFromZero),
            (int)Math.Round(rect.Y * ratio, MidpointRounding.AwayFromZero),
            Math.Max(1, (int)Math.Round(rect.Width * ratio, MidpointRounding.AwayFromZero)),
            Math.Max(1, (int)Math.Round(rect.Height * ratio, MidpointRounding.AwayFromZero)));

    private static WorkspaceRect Clamp(WorkspaceRect desired, WorkspaceRect area)
    {
        var maxWidth = Math.Max(1, area.Width);
        var maxHeight = Math.Max(1, area.Height);
        var width = Math.Min(Math.Max(desired.Width, Math.Min(MinimumWindowWidth, maxWidth)), maxWidth);
        var height = Math.Min(Math.Max(desired.Height, Math.Min(MinimumWindowHeight, maxHeight)), maxHeight);
        var minX = area.X;
        var minY = area.Y;
        var maxX = area.X + maxWidth - width;
        var maxY = area.Y + maxHeight - height;
        var x = Math.Clamp(desired.X, minX, maxX);
        var y = Math.Clamp(desired.Y, minY, maxY);
        return new WorkspaceRect(x, y, width, height);
    }
}
