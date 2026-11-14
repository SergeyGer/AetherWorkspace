namespace Aether.Contracts.Models;

/// <summary>Deterministic window geometry in virtual-desktop pixels.</summary>
public sealed record WindowBounds(int X, int Y, int Width, int Height)
{
    /// <summary>Upper bounds that protect the broker/arranger from absurd input.</summary>
    public bool IsSane() =>
        Width is > 0 and <= 16384 &&
        Height is > 0 and <= 16384 &&
        X is >= -32768 and <= 32768 &&
        Y is >= -32768 and <= 32768;
}

/// <summary>Latest reported state of a registered top-level window.</summary>
public sealed record WindowState(
    string AppId,
    string InstanceId,
    string WindowId,
    string Title,
    WindowBounds Bounds,
    string MonitorId,
    bool Visible,
    long NativeHandle);

/// <summary>A deterministic placement computed by the broker's layout planner.</summary>
public sealed record WindowPlacement(
    string AppId,
    string InstanceId,
    string WindowId,
    string Title,
    WindowBounds Bounds,
    string MonitorId,
    bool Visible);

/// <summary>Result of a layout computation and (optionally) window application.</summary>
public sealed record WindowLayout(
    string Layout,
    IReadOnlyList<WindowPlacement> Placements,
    string MonitorId);
