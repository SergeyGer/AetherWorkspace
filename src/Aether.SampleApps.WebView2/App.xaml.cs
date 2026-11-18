using System;
using System.Windows;
using Aether.SampleApps;

namespace Aether.SampleApps.WebView2;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        SampleDiagnostics.Install("webview2");
        DispatcherUnhandledException += (_, args) =>
        {
            SampleDiagnostics.Log("dispatcher-unhandled", args.Exception);
            args.Handled = true;
            MessageBox.Show(
                "The WebView2 chart sample hit an unexpected error. Diagnostics were written to %TEMP%\\aether-sample-logs.\n\n"
                + args.Exception.Message,
                "AetherWorkspace WebView2 chart error",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        };

        SampleUiOptions options;
        try
        {
            options = SampleUiOptions.Parse(e.Args);
            if (options.App != "webview2-chart")
            {
                throw new ArgumentException("The WebView2 sample host only supports --app webview2-chart.");
            }
        }
        catch (Exception exception)
        {
            SampleDiagnostics.Log("startup-options", exception);
            MessageBox.Show(exception.Message, "AetherWorkspace WebView2 chart sample");
            Shutdown(2);
            return;
        }

        var window = new WebView2ChartWindow(options);
        MainWindow = window;
        window.Show();
    }
}
