using System.Runtime.InteropServices;
using Aether.Contracts.Models;

namespace Aether.Broker.Windows;

public sealed record ArrangementApplyResult(bool Applied, int MovedWindows, string? Message);

/// <summary>Applies a computed layout to real or virtual top-level windows.</summary>
public interface IWindowArranger
{
    Task<ArrangementApplyResult> ApplyAsync(
        WindowLayout layout,
        IReadOnlyList<WindowState> windows,
        CancellationToken cancellationToken);
}

/// <summary>
/// Headless arranger used by CI, tests and the console demo. It records the
/// computed layout without touching a desktop; useful for deterministic E2E
/// assertions of arrangement geometry.
/// </summary>
public sealed class VirtualWindowArranger : IWindowArranger
{
    private readonly object _gate = new();
    private WindowLayout? _lastLayout;

    public WindowLayout? LastLayout
    {
        get
        {
            lock (_gate)
            {
                return _lastLayout;
            }
        }
    }

    public Task<ArrangementApplyResult> ApplyAsync(WindowLayout layout, IReadOnlyList<WindowState> windows, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(layout);
        lock (_gate)
        {
            _lastLayout = layout;
        }

        return Task.FromResult(new ArrangementApplyResult(true, layout.Placements.Count, null));
    }
}

/// <summary>
/// Windows-only top-level arrangement via SetWindowPos. Never reparents windows
/// and only touches windows whose native handle was reported by a registered,
/// authenticated sample app. No-op on non-Windows platforms.
/// </summary>
public sealed class Win32WindowArranger : IWindowArranger
{
    private const uint SwpNoZOrder = 0x0004;
    private const uint SwpNoActivate = 0x0010;

    public Task<ArrangementApplyResult> ApplyAsync(WindowLayout layout, IReadOnlyList<WindowState> windows, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(layout);

        if (!OperatingSystem.IsWindows())
        {
            return Task.FromResult(new ArrangementApplyResult(false, 0, "Win32 window arrangement is only available on Windows."));
        }

        var handles = windows
            .Where(window => window.NativeHandle != 0)
            .GroupBy(window => (window.AppId, window.InstanceId, window.WindowId))
            .ToDictionary(group => group.Key, group => (nint)group.First().NativeHandle);

        var moved = 0;
        foreach (var placement in layout.Placements)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!handles.TryGetValue((placement.AppId, placement.InstanceId, placement.WindowId), out var handle))
            {
                continue;
            }

            if (SetWindowPos(
                    handle,
                    nint.Zero,
                    placement.Bounds.X,
                    placement.Bounds.Y,
                    placement.Bounds.Width,
                    placement.Bounds.Height,
                    SwpNoZOrder | SwpNoActivate))
            {
                moved++;
            }
        }

        return Task.FromResult(new ArrangementApplyResult(true, moved, null));
    }

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowPos(nint hWnd, nint hWndInsertAfter, int x, int y, int cx, int cy, uint uFlags);
}
