using System.Windows.Media;

namespace Aether.Shell;

/// <summary>Sanitized UI frame/command telemetry for one workflow run (v1.1 AW-066).</summary>
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
/// Samples WPF render frame intervals while a workflow runs. This is measurement only:
/// it never changes UI behavior and publishes percentiles rather than a latency claim.
/// </summary>
public sealed class UiFrameTelemetry
{
    private const double LongFrameThresholdMs = 50.0;
    private readonly List<double> _intervals = [];
    private DateTime? _lastFrameUtc;
    private bool _capturing;

    public void Start()
    {
        _intervals.Clear();
        _lastFrameUtc = null;
        if (_capturing)
        {
            return;
        }

        _capturing = true;
        CompositionTarget.Rendering += OnRendering;
    }

    public UiTelemetrySnapshot Stop(long commandDurationMs)
    {
        if (_capturing)
        {
            CompositionTarget.Rendering -= OnRendering;
            _capturing = false;
        }

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

    private void OnRendering(object? sender, EventArgs e)
    {
        var now = DateTime.UtcNow;
        if (_lastFrameUtc is { } previous)
        {
            var interval = (now - previous).TotalMilliseconds;
            if (interval is > 0 and < 10_000)
            {
                _intervals.Add(interval);
            }
        }

        _lastFrameUtc = now;
    }

    private static double Percentile(double[] ordered, int percentile)
    {
        var index = (int)Math.Ceiling((percentile / 100.0) * ordered.Length) - 1;
        return Math.Round(ordered[Math.Clamp(index, 0, ordered.Length - 1)], 2);
    }
}
