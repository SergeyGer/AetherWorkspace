using System.IO;
namespace Aether.SampleApps;

/// <summary>Role/arguments for the WPF sample process (one process per window).</summary>
public sealed class SampleUiOptions
{
    public required string App { get; init; }

    public required string FixturesDirectory { get; init; }

    public static SampleUiOptions Parse(string[] args)
    {
        string? app = null;
        string? fixtures = null;
        for (var index = 0; index < args.Length; index++)
        {
            switch (args[index])
            {
                case "--app":
                    app = args[++index];
                    break;
                case "--fixtures":
                    fixtures = args[++index];
                    break;
            }
        }

        if (app is not ("watchlist" or "risk" or "chart" or "webview2-chart"))
        {
            throw new ArgumentException("--app must be one of: watchlist, risk, chart, webview2-chart.");
        }

        return new SampleUiOptions
        {
            App = app,
            FixturesDirectory = fixtures ?? Path.Combine(Environment.CurrentDirectory, "samples", "fixtures"),
        };
    }

    public string AppId => App == "webview2-chart" ? "sample.chart.webview2" : "sample." + App;
}
