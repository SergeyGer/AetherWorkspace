using System.Windows;

namespace Aether.SampleApps;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        SampleUiOptions options;
        try
        {
            options = SampleUiOptions.Parse(e.Args);
        }
        catch (Exception exception)
        {
            MessageBox.Show(exception.Message, "AetherWorkspace sample");
            Shutdown(2);
            return;
        }

        Window window = options.App switch
        {
            "watchlist" => new WatchlistWindow(options),
            "risk" => new RiskWindow(options),
            "chart" => new ChartWindow(options),
            _ => throw new InvalidOperationException("Unsupported sample role."),
        };
        MainWindow = window;
        window.Show();
    }
}
