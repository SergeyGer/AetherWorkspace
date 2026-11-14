using Aether.Contracts.Models;

namespace Aether.SampleApps.Headless;

/// <summary>Command-line options for the headless sample app process.</summary>
public sealed class SampleOptions
{
    public required string App { get; init; }

    public required string FixturesDirectory { get; init; }

    public int? RunSeconds { get; init; }

    public long NativeHandle { get; init; }

    public string MonitorId { get; init; } = "virtual-monitor-1";

    public static SampleOptions Parse(string[] args)
    {
        string? app = null;
        string? fixtures = null;
        int? runSeconds = null;
        long nativeHandle = 0;
        var monitorId = "virtual-monitor-1";

        for (var index = 0; index < args.Length; index++)
        {
            var argument = args[index];
            string NextValue() => index + 1 < args.Length
                ? args[++index]
                : throw new ArgumentException($"Missing value for option '{argument}'.");

            switch (argument)
            {
                case "--app":
                    app = NextValue();
                    break;
                case "--fixtures":
                    fixtures = NextValue();
                    break;
                case "--run-seconds":
                    runSeconds = int.Parse(NextValue(), System.Globalization.CultureInfo.InvariantCulture);
                    break;
                case "--native-handle":
                    nativeHandle = long.Parse(NextValue(), System.Globalization.CultureInfo.InvariantCulture);
                    break;
                case "--monitor":
                    monitorId = NextValue();
                    break;
                default:
                    throw new ArgumentException($"Unknown option '{argument}'.");
            }
        }

        if (string.IsNullOrWhiteSpace(app) || !WindowProfile.For(app).IsKnown)
        {
            throw new ArgumentException("--app must be one of: watchlist, risk, chart.");
        }

        return new SampleOptions
        {
            App = app,
            FixturesDirectory = fixtures ?? Path.Combine(Environment.CurrentDirectory, "samples", "fixtures"),
            RunSeconds = runSeconds,
            NativeHandle = nativeHandle,
            MonitorId = monitorId,
        };
    }
}

/// <summary>Deterministic virtual window geometry per sample app.</summary>
public sealed record WindowProfile(string WindowId, string Title, WindowBounds Bounds)
{
    public bool IsKnown => Bounds.Width > 0;

    public static WindowProfile For(string app) => app switch
    {
        "watchlist" => new WindowProfile("main", "Synthetic Watchlist", new WindowBounds(0, 0, 800, 600)),
        "risk" => new WindowProfile("main", "Synthetic Risk View", new WindowBounds(800, 0, 800, 600)),
        "chart" => new WindowProfile("main", "Synthetic Bond Chart", new WindowBounds(0, 600, 1600, 480)),
        _ => new WindowProfile("", "", new WindowBounds(0, 0, 0, 0)),
    };
}
