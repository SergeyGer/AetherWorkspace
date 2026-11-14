using System.Diagnostics;

namespace Aether.Broker.Launching;

/// <summary>Observed process identity used for session binding.</summary>
public sealed record ProcessIdentity(int ProcessId, string ExecutablePath, DateTimeOffset StartTimeUtc);

public interface IProcessIdentityResolver
{
    ProcessIdentity? Resolve(int processId);
}

/// <summary>
/// Resolves process id to executable path and start time. On Linux this reads
/// <c>/proc/&lt;pid&gt;/exe</c>; on Windows it uses the main module path. The
/// binding is defense-in-depth: same-user processes remain a weak OS boundary.
/// </summary>
public sealed class SystemProcessIdentityResolver : IProcessIdentityResolver
{
    public ProcessIdentity? Resolve(int processId)
    {
        if (processId <= 0)
        {
            return null;
        }

        try
        {
            using var process = Process.GetProcessById(processId);
            var startTime = process.StartTime.ToUniversalTime();
            string? executablePath = null;

            if (OperatingSystem.IsWindows())
            {
                executablePath = process.MainModule?.FileName;
            }
            else
            {
                var link = new FileInfo($"/proc/{processId}/exe");
                executablePath = link.ResolveLinkTarget(returnFinalTarget: true)?.FullName;
            }

            if (string.IsNullOrWhiteSpace(executablePath))
            {
                return null;
            }

            return new ProcessIdentity(processId, CanonicalizeExecutable(executablePath), new DateTimeOffset(startTime, TimeSpan.Zero));
        }
        catch (ArgumentException)
        {
            // Process does not exist.
            return null;
        }
        catch (InvalidOperationException)
        {
            return null;
        }
        catch (System.ComponentModel.Win32Exception)
        {
            return null;
        }
    }

    /// <summary>Resolves symlinks and normalizes an executable path for comparison.</summary>
    public static string CanonicalizeExecutable(string path)
    {
        var fullPath = Path.GetFullPath(path);
        try
        {
            if (File.Exists(fullPath))
            {
                var target = File.ResolveLinkTarget(fullPath, returnFinalTarget: true);
                if (target is not null)
                {
                    return Path.GetFullPath(target.FullName);
                }
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }

        return fullPath;
    }
}

public static class ProcessPathComparer
{
    public static StringComparer Comparer { get; } =
        OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;

    public static bool PathsEqual(string? left, string? right) =>
        left is not null && right is not null && Comparer.Equals(left, right);
}
