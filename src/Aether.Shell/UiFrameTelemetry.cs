using System.Windows.Threading;

namespace Aether.Shell;

/// <summary>Sanitized UI responsiveness telemetry for one workflow run (v1.1 AW-066).</summary>
public sealed record UiTelemetrySnapshot(
    int FrameCount,
    double P50FrameMs,
    double P95FrameMs,
    double P99FrameMs,
    double MaxFrameMs,
    int LongFrames,
    long CommandDurationMs,
    double WorkingSetMb,
    double ManagedMemoryMb)
{
    public static UiTelemetrySnapshot Empty(long commandDurationMs) =>
        new(0, 0, 0, 0, 0, 0, commandDurationMs, 0, 0);
}

/// <summary>
/// Samples WPF UI-thread scheduling intervals with a 16 ms dispatcher heartbeat while a workflow
/// runs. A delayed tick means the UI thread was busy; this is a measurement only and never changes
/// UI behavior or turns into a latency claim. Calendar/frame composition events are not claimed.
/// </summary>
public sealed class UiFrameTelemetry
{
    private const double LongFrameThresholdMs = 50.0;
    private static readonly TimeSpan HeartbeatInterval = TimeSpan.FromMilliseconds(16);

    private readonly List<double> _intervals = [];
    private DispatcherTimer? _timer;
    private DateTime? _lastTickUtc;

    public void Start()
    {
        StopTimer();
        _intervals.Clear();
        _lastTickUtc = null;
        _timer = new DispatcherTimer(DispatcherPriority.Background, Dispatcher.CurrentDispatcher)
        {
            Interval = HeartbeatInterval,
        };
        _timer.Tick += OnTick;
        _timer.Start();
    }

    public UiTelemetrySnapshot Stop(long commandDurationMs)
    {
        StopTimer();
        var process = System.Diagnostics.Process.GetCurrentProcess();
        var workingSetMb = Math.Round(process.WorkingSet64 / 1024.0 / 1024.0, 1);
        var managedMemoryMb = Math.Round(GC.GetTotalMemory(forceFullCollection: false) / 1024.0 / 1024.0, 1);
        if (_intervals.Count == 0)
        {
            return UiTelemetrySnapshot.Empty(commandDurationMs) with
            {
                WorkingSetMb = workingSetMb,
                ManagedMemoryMb = managedMemoryMb,
            };
        }

        var ordered = _intervals.OrderBy(value => value).ToArray();
        return new UiTelemetrySnapshot(
            ordered.Length,
            Percentile(ordered, 50),
            Percentile(ordered, 95),
            Percentile(ordered, 99),
            Math.Round(ordered[^1], 2),
            ordered.Count(value => value > LongFrameThresholdMs),
            commandDurationMs,
            workingSetMb,
            managedMemoryMb);
    }

    private void OnTick(object? sender, EventArgs e)
    {
        var now = DateTime.UtcNow;
        if (_lastTickUtc is { } previous)
        {
            var interval = (now - previous).TotalMilliseconds;
            if (interval is > 0 and < 10_000)
            {
                _intervals.Add(interval);
            }
        }

        _lastTickUtc = now;
    }

    private void StopTimer()
    {
        if (_timer is null)
        {
            return;
        }

        _timer.Stop();
        _timer.Tick -= OnTick;
        _timer = null;
    }

    private static double Percentile(double[] ordered, int percentile)
    {
        var index = (int)Math.Ceiling((percentile / 100.0) * ordered.Length) - 1;
        return Math.Round(ordered[Math.Clamp(index, 0, ordered.Length - 1)], 2);
    }
}
