using System;
using System.IO;
using System.Text;

namespace Aether.SampleApps;

/// <summary>
/// Local, privacy-safe diagnostics for WPF sample applications: writes unhandled-exception
/// details and lifecycle events to stderr (captured by the broker child log) and to a per-process
/// log file under %TEMP%/aether-sample-logs. Never contains payloads or secrets.
/// </summary>
public static class SampleDiagnostics
{
    private static readonly object Gate = new();
    private static string? _logPath;

    public static string? LogPath => _logPath;

    public static void Install(string role)
    {
        var directory = Path.Combine(Path.GetTempPath(), "aether-sample-logs");
        Directory.CreateDirectory(directory);
        _logPath = Path.Combine(directory, $"sample-{role}-{Environment.ProcessId}.log");
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
            Log("unhandled-domain", args.ExceptionObject as Exception ?? new Exception(args.ExceptionObject?.ToString()));
        System.Threading.Tasks.TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            Log("unobserved-task", args.Exception);
            args.SetObserved();
        };
    }

    public static void Log(string category, Exception? exception, string? detail = null)
    {
        var message = $"[{DateTimeOffset.UtcNow:O}] {category}: {exception?.GetType().Name}: {exception?.Message}";
        if (!string.IsNullOrWhiteSpace(detail))
        {
            message += " | " + detail;
        }

        try
        {
            Console.Error.WriteLine(message);
        }
        catch
        {
            // stderr may be unavailable in some hosts; the file log is best-effort.
        }

        var path = _logPath;
        if (path is not null)
        {
            try
            {
                lock (Gate)
                {
                    File.AppendAllText(path, message + Environment.NewLine, Encoding.UTF8);
                }
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }
}
