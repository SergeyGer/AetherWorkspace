using System.Diagnostics;
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
/// Probes WPF UI-thread responsiveness while a workflow runs: a background pump enqueues a no-op
/// dispatcher operation every 16 ms and records how long the UI thread took to process it. This is
/// a measurement only, never a rendering/frame-rate claim, and it does not alter UI behavior.
/// </summary>
public sealed class UiFrameTelemetry
{
    private const double LongFrameThresholdMs = 50.0;
    private static readonly TimeSpan ProbeInterval = TimeSpan.FromMilliseconds(4);

    private readonly object _gate = new();
    private readonly List<double> _latencies = [];
    private Dispatcher? _dispatcher;
    private CancellationTokenSource? _pumpCancellation;
    private Task? _pumpTask;

    public void Start()
    {
        StopPump();
        lock (_gate)
        {
            _latencies.Clear();
        }

        _dispatcher = Dispatcher.CurrentDispatcher;
        _pumpCancellation = new CancellationTokenSource();
        _pumpTask = RunProbeAsync(_dispatcher, _pumpCancellation.Token);
    }

    public UiTelemetrySnapshot Stop(long commandDurationMs)
    {
        StopPump();
        var process = Process.GetCurrentProcess();
        var workingSetMb = Math.Round(process.WorkingSet64 / 1024.0 / 1024.0, 1);
        var managedMemoryMb = Math.Round(GC.GetTotalMemory(forceFullCollection: false) / 1024.0 / 1024.0, 1);
        double[] ordered;
        lock (_gate)
        {
            ordered = _latencies.OrderBy(value => value).ToArray();
        }

        if (ordered.Length == 0)
        {
            return UiTelemetrySnapshot.Empty(commandDurationMs) with
            {
                WorkingSetMb = workingSetMb,
                ManagedMemoryMb = managedMemoryMb,
            };
        }

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

    private async Task RunProbeAsync(Dispatcher dispatcher, CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                // Probe immediately before waiting: fast steady-state workflows can finish in a few
                // milliseconds, and a delayed first probe would otherwise yield zero samples.
                var stopwatch = Stopwatch.StartNew();
                await dispatcher.InvokeAsync(() => { }, DispatcherPriority.Normal, cancellationToken).Task.ConfigureAwait(false);
                stopwatch.Stop();
                if (!cancellationToken.IsCancellationRequested)
                {
                    lock (_gate)
                    {
                        _latencies.Add(stopwatch.Elapsed.TotalMilliseconds);
                    }
                }

                await Task.Delay(ProbeInterval, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (Exception)
            {
                // Measurement must never disturb the workflow.
                return;
            }
        }
    }

    private void StopPump()
    {
        try
        {
            _pumpCancellation?.Cancel();
        }
        catch (ObjectDisposedException)
        {
        }

        _pumpCancellation?.Dispose();
        _pumpCancellation = null;
        _pumpTask = null;
        _dispatcher = null;
    }

    private static double Percentile(double[] ordered, int percentile)
    {
        var index = (int)Math.Ceiling((percentile / 100.0) * ordered.Length) - 1;
        return Math.Round(ordered[Math.Clamp(index, 0, ordered.Length - 1)], 2);
    }
}
