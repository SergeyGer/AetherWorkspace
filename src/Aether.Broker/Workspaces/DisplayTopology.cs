using System.Runtime.InteropServices;
using Aether.Contracts.Models.Workspaces;

namespace Aether.Broker.Workspaces;

/// <summary>Locally trusted display topology (monitor identity, bounds, work area, DPI scale).</summary>
public interface IDisplayTopologyProvider
{
    IReadOnlyList<WorkspaceMonitor> GetMonitors();
}

/// <summary>Deterministic headless topology derived from broker options (CI, tests, non-Windows).</summary>
public sealed class VirtualDisplayTopology : IDisplayTopologyProvider
{
    private readonly WorkspaceMonitor _monitor;

    public VirtualDisplayTopology(BrokerOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        var bounds = new WorkspaceRect(0, 0, options.WorkAreaWidth, options.WorkAreaHeight);
        _monitor = new WorkspaceMonitor(options.MonitorId, true, bounds, bounds, 1.0);
    }

    public IReadOnlyList<WorkspaceMonitor> GetMonitors() => [_monitor];
}

/// <summary>
/// Windows display topology via EnumDisplayMonitors/GetMonitorInfo and per-monitor DPI.
/// Falls back to a deterministic virtual monitor when enumeration is unavailable.
/// </summary>
public sealed class Win32DisplayTopology : IDisplayTopologyProvider
{
    private const uint MonitorInfoPrimary = 0x00000001;
    private const int EffectiveDpi = 0;

    private readonly BrokerOptions _options;

    public Win32DisplayTopology(BrokerOptions options)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
    }

    public IReadOnlyList<WorkspaceMonitor> GetMonitors()
    {
        if (!OperatingSystem.IsWindows())
        {
            return new VirtualDisplayTopology(_options).GetMonitors();
        }

        try
        {
            var monitors = new List<WorkspaceMonitor>();
            var callback = new MonitorEnumProc((IntPtr hMonitor, IntPtr hdcMonitor, ref Rect lprcMonitor, IntPtr dwData) =>
            {
                var info = new MonitorInfoEx { Size = Marshal.SizeOf<MonitorInfoEx>() };
                if (GetMonitorInfo(hMonitor, ref info))
                {
                    var dpi = 96u;
                    if (GetDpiForMonitor(hMonitor, EffectiveDpi, out var dpiX, out _) == 0 && dpiX is >= 72 and <= 480)
                    {
                        dpi = dpiX;
                    }

                    monitors.Add(new WorkspaceMonitor(
                        info.DeviceName,
                        (info.Flags & MonitorInfoPrimary) != 0,
                        ToRect(info.Monitor),
                        ToRect(info.WorkArea),
                        Math.Round(dpi / 96.0, 2)));
                }

                return true;
            });

            if (EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, callback, IntPtr.Zero) && monitors.Count > 0)
            {
                GC.KeepAlive(callback);
                return monitors;
            }
        }
        catch (Exception exception) when (exception is DllNotFoundException or EntryPointNotFoundException or MarshalDirectiveException)
        {
        }

        return new VirtualDisplayTopology(_options).GetMonitors();
    }

    private static WorkspaceRect ToRect(Rect rect) =>
        new(rect.Left, rect.Top, Math.Max(1, rect.Right - rect.Left), Math.Max(1, rect.Bottom - rect.Top));

    [StructLayout(LayoutKind.Sequential)]
    private struct Rect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct MonitorInfoEx
    {
        public int Size;
        public Rect Monitor;
        public Rect WorkArea;
        public uint Flags;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string DeviceName;
    }

    private delegate bool MonitorEnumProc(IntPtr hMonitor, IntPtr hdcMonitor, ref Rect lprcMonitor, IntPtr dwData);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EnumDisplayMonitors(IntPtr hdc, IntPtr clip, MonitorEnumProc callback, IntPtr data);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfoEx info);

    [DllImport("shcore.dll")]
    private static extern int GetDpiForMonitor(IntPtr monitor, int dpiType, out uint dpiX, out uint dpiY);
}
