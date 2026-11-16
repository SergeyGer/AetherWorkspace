using System.Windows;

namespace Aether.SampleApps;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        SampleDiagnostics.Install("app");
        DispatcherUnhandledException += (_, args) =>
        {
            SampleDiagnostics.Log("dispatcher-unhandled", args.Exception);
            args.Handled = true;
            MessageBox.Show("The sample application hit an unexpected error. Diagnostics were written to %TEMP%\\aether-sample-logs.\\n\\n" + args.Exception.Message,
                "AetherWorkspace sample error", MessageBoxButton.OK, MessageBoxImage.Error);
        };

        SampleUiOptions options;
        try
        {
            options = SampleUiOptions.Parse(e.Args);
        }
        catch (Exception exception)
        {
            SampleDiagnostics.Log("startup-options", exception);
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
