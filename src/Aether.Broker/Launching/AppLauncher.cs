using System.Diagnostics;
using System.Text.RegularExpressions;
using Aether.Broker.Registry;
using Aether.Broker.Sessions;
using Aether.Contracts;
using Aether.Contracts.Errors;
using Aether.Security.Audit;

namespace Aether.Broker.Launching;

/// <summary>
/// Allowlisted process launcher. Executable and argument templates come from
/// the trusted registry and are expanded only with broker-supplied tokens; the
/// model/plan can reference an app id but never a path or argument. Canonical
/// paths are checked against configured executable roots before launch.
/// </summary>
public sealed partial class AppLauncher : IAsyncDisposable
{
    private readonly object _gate = new();
    private readonly BrokerOptions _options;
    private readonly AppRegistry _registry;
    private readonly SessionManager _sessions;
    private readonly IProcessIdentityResolver _identityResolver;
    private readonly IAuditSink _auditSink;
    private readonly Dictionary<string, LaunchedApp> _launched = new(StringComparer.Ordinal);

    public AppLauncher(
        BrokerOptions options,
        AppRegistry registry,
        SessionManager sessions,
        IProcessIdentityResolver identityResolver,
        IAuditSink auditSink)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
        _sessions = sessions ?? throw new ArgumentNullException(nameof(sessions));
        _identityResolver = identityResolver ?? throw new ArgumentNullException(nameof(identityResolver));
        _auditSink = auditSink ?? throw new ArgumentNullException(nameof(auditSink));
    }

    public IReadOnlyList<LaunchedApp> Snapshot()
    {
        lock (_gate)
        {
            return _launched.Values.ToArray();
        }
    }

    public async Task<LaunchOutcome> LaunchAsync(string appId, string traceId, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(appId) || !_registry.TryGet(appId, out var app))
        {
            return LaunchOutcome.Deny(AetherErrorCodes.BrokerAppUnknown, "The requested application is not registered in the allowlist.");
        }

        if (!app.Launchable)
        {
            return LaunchOutcome.Deny(AetherErrorCodes.BrokerAppUnknown, "The requested application is not launchable.");
        }

        var existingSession = _sessions.GetActiveInstance(appId);
        if (existingSession is not null)
        {
            var existing = FindLaunched(existingSession.InstanceId);
            if (existing is not null && !existing.HasExited)
            {
                return LaunchOutcome.Ok(existing, reusedExistingInstance: true);
            }
        }

        var now = _options.TimeProvider.GetUtcNow();
        var instanceId = "inst-" + Guid.NewGuid().ToString("n")[..12];
        var token = SessionManager.TokenGenerator.NewToken();

        if (!TryExpandArguments(app, out var executable, out var arguments, out var workingDirectory, out var denialMessage))
        {
            AuditLaunch(traceId, appId, AuditOutcomes.Denied, AetherErrorCodes.BrokerExecutableDenied);
            return LaunchOutcome.Deny(AetherErrorCodes.BrokerExecutableDenied, denialMessage ?? "The registered executable configuration was rejected.");
        }

        Process process;
        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = executable,
                WorkingDirectory = workingDirectory,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };
            foreach (var argument in arguments)
            {
                startInfo.ArgumentList.Add(argument);
            }

            startInfo.Environment["AETHER_BROKER_TRANSPORT"] = _options.Transport ?? Transport.BrokerEndpoint.DefaultTransport();
            startInfo.Environment["AETHER_BROKER_ENDPOINT"] = _options.Endpoint ?? "";
            startInfo.Environment["AETHER_APP_ID"] = app.AppId;
            startInfo.Environment["AETHER_INSTANCE_ID"] = instanceId;
            startInfo.Environment["AETHER_SESSION_TOKEN"] = token;
            startInfo.Environment["AETHER_TRACE_ID"] = traceId;
            startInfo.Environment["AETHER_BROKER_PROTOCOL_VERSION"] = AetherVersions.ProtocolVersion;

            process = Process.Start(startInfo) ?? throw new InvalidOperationException("Process.Start returned null.");
        }
        catch (Exception exception) when (exception is System.ComponentModel.Win32Exception or InvalidOperationException or IOException)
        {
            AuditLaunch(traceId, appId, AuditOutcomes.Failed, AetherErrorCodes.BrokerLaunchFailed);
            return LaunchOutcome.Deny(AetherErrorCodes.BrokerLaunchFailed, $"The application process could not be started: {exception.Message}");
        }

        // Drain child stdout/stderr so a noisy app cannot block on a full pipe.
        _ = DrainAsync(process.StandardOutput, appId, instanceId, isError: false, _options.ChildLogDirectory);
        _ = DrainAsync(process.StandardError, appId, instanceId, isError: true, _options.ChildLogDirectory);

        ProcessIdentity? identity;
        try
        {
            identity = await WaitForIdentityAsync(process, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            TryKill(process);
            throw;
        }

        if (identity is null)
        {
            TryKill(process);
            AuditLaunch(traceId, appId, AuditOutcomes.Denied, AetherErrorCodes.AuthIdentityMismatch);
            return LaunchOutcome.Deny(
                AetherErrorCodes.AuthIdentityMismatch,
                "The launched process identity could not be verified; the process was stopped.");
        }

        var launched = new LaunchedApp(
            app.AppId,
            instanceId,
            process,
            identity.ExecutablePath,
            now,
            now.AddSeconds(_options.PendingRegistrationTtlSeconds));

        _sessions.AddPendingRegistration(app, instanceId, token, identity, now);
        lock (_gate)
        {
            PruneExitedLocked();
            _launched[instanceId] = launched;
        }

        AuditLaunch(traceId, appId, AuditOutcomes.Allowed, null);
        return LaunchOutcome.Ok(launched, reusedExistingInstance: false);
    }

    public async Task<StopOutcome> StopAsync(string appId, string? instanceId, string traceId, CancellationToken cancellationToken)
    {
        LaunchedApp? target = null;
        lock (_gate)
        {
            if (!string.IsNullOrWhiteSpace(instanceId))
            {
                _launched.TryGetValue(instanceId, out target);
            }
            else
            {
                target = _launched.Values
                    .Where(item => string.Equals(item.AppId, appId, StringComparison.Ordinal))
                    .OrderByDescending(item => item.LaunchedAtUtc)
                    .FirstOrDefault();
            }
        }

        if (target is null)
        {
            return StopOutcome.Deny(AetherErrorCodes.BrokerAppUnknown, "No running instance was found for the requested application.");
        }

        await StopProcessAsync(target, cancellationToken).ConfigureAwait(false);
        _sessions.RevokeInstance(target.InstanceId);
        lock (_gate)
        {
            _launched.Remove(target.InstanceId);
        }

        AuditLaunch(traceId, target.AppId, AuditOutcomes.Info, null);
        return StopOutcome.Ok();
    }

    public async Task<LaunchOutcome> RestartAsync(string appId, string traceId, CancellationToken cancellationToken)
    {
        if (!_registry.TryGet(appId, out var app) || !app.Launchable)
        {
            return LaunchOutcome.Deny(AetherErrorCodes.BrokerAppUnknown, "The requested application is not registered as launchable.");
        }

        var lastFailure = AetherErrorCodes.BrokerLaunchFailed;
        for (var attempt = 0; attempt < _options.MaxRestartAttempts; attempt++)
        {
            await StopAsync(appId, null, traceId, cancellationToken).ConfigureAwait(false);
            var outcome = await LaunchAsync(appId, traceId, cancellationToken).ConfigureAwait(false);
            if (outcome.Success)
            {
                return outcome;
            }

            lastFailure = outcome.DenialCode ?? lastFailure;
            await Task.Delay(TimeSpan.FromMilliseconds(150 * (attempt + 1)), cancellationToken).ConfigureAwait(false);
        }

        return LaunchOutcome.Deny(lastFailure, "The application could not be restarted within the bounded attempt budget.");
    }

    public async Task StopAllAsync()
    {
        IReadOnlyList<LaunchedApp> snapshot;
        lock (_gate)
        {
            snapshot = _launched.Values.ToArray();
            _launched.Clear();
        }

        foreach (var app in snapshot)
        {
            await StopProcessAsync(app, CancellationToken.None).ConfigureAwait(false);
            _sessions.RevokeInstance(app.InstanceId);
        }
    }

    private LaunchedApp? FindLaunched(string instanceId)
    {
        lock (_gate)
        {
            return _launched.TryGetValue(instanceId, out var app) ? app : null;
        }
    }

    private async Task<ProcessIdentity?> WaitForIdentityAsync(Process process, CancellationToken cancellationToken)
    {
        var deadline = _options.TimeProvider.GetUtcNow() + TimeSpan.FromSeconds(10);
        while (_options.TimeProvider.GetUtcNow() < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (process.HasExited)
            {
                return null;
            }

            var identity = _identityResolver.Resolve(process.Id);
            if (identity is not null)
            {
                return identity;
            }

            await Task.Delay(25, cancellationToken).ConfigureAwait(false);
        }

        return null;
    }

    private bool TryExpandArguments(
        RegisteredApp app,
        out string executable,
        out IReadOnlyList<string> arguments,
        out string workingDirectory,
        out string? denialMessage)
    {
        executable = "";
        arguments = [];
        workingDirectory = "";
        denialMessage = null;

        if (!TryExpandTemplate(app.Executable, out var expandedExecutable))
        {
            denialMessage = "The registry executable template contains an unknown token.";
            return false;
        }

        var resolved = ResolveExecutable(expandedExecutable);
        if (resolved is null)
        {
            denialMessage = "The registered executable could not be resolved to an existing file.";
            return false;
        }

        var canonical = SystemProcessIdentityResolver.CanonicalizeExecutable(resolved);
        if (!_options.AllowedExecutableRoots.Any(root => IsUnderRoot(canonical, root)))
        {
            denialMessage = "The registered executable is outside the configured allowlisted roots.";
            return false;
        }

        var expandedArguments = new List<string>(app.Arguments.Count);
        foreach (var argumentTemplate in app.Arguments)
        {
            if (!TryExpandTemplate(argumentTemplate, out var argument))
            {
                denialMessage = "The registry argument template contains an unknown token.";
                return false;
            }

            expandedArguments.Add(argument);
        }

        var expandedWorkingDirectory = app.WorkingDirectory is null
            ? AppContext.BaseDirectory
            : TryExpandTemplate(app.WorkingDirectory, out var expandedDirectory)
                ? expandedDirectory
                : "";

        if (string.IsNullOrWhiteSpace(expandedWorkingDirectory) || !Directory.Exists(expandedWorkingDirectory))
        {
            denialMessage = "The registered working directory does not exist.";
            return false;
        }

        executable = canonical;
        arguments = expandedArguments;
        workingDirectory = Path.GetFullPath(expandedWorkingDirectory);
        return true;
    }

    private bool TryExpandTemplate(string template, out string expanded)
    {
        try
        {
            expanded = TokenPattern().Replace(template, match =>
            {
                var key = match.Groups[1].Value;
                if (!_options.LaunchTokens.TryGetValue(key, out var value))
                {
                    throw new InvalidOperationException($"Unknown launch token '{key}'.");
                }

                return value;
            });
            return !expanded.Contains('{', StringComparison.Ordinal) && !expanded.Contains('}', StringComparison.Ordinal);
        }
        catch (InvalidOperationException)
        {
            expanded = "";
            return false;
        }
    }

    private static string? ResolveExecutable(string executable)
    {
        if (Path.IsPathRooted(executable))
        {
            return File.Exists(executable) ? Path.GetFullPath(executable) : null;
        }

        if (executable.Contains(Path.DirectorySeparatorChar) || executable.Contains(Path.AltDirectorySeparatorChar))
        {
            // Relative paths are ambiguous; only absolute paths or bare names are accepted.
            return null;
        }

        var pathVariable = Environment.GetEnvironmentVariable("PATH") ?? "";
        var extensions = OperatingSystem.IsWindows()
            ? (Environment.GetEnvironmentVariable("PATHEXT") ?? ".EXE;.CMD;.BAT").Split(';', StringSplitOptions.RemoveEmptyEntries)
            : new[] { "" };

        foreach (var directory in pathVariable.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            foreach (var extension in extensions)
            {
                var candidate = Path.Combine(directory, executable + extension);
                if (File.Exists(candidate))
                {
                    return Path.GetFullPath(candidate);
                }
            }
        }

        return null;
    }

    private static bool IsUnderRoot(string canonicalPath, string root)
    {
        var canonicalRoot = Path.GetFullPath(root);
        if (ProcessPathComparer.PathsEqual(canonicalPath, canonicalRoot))
        {
            return true;
        }

        var rootWithSeparator = canonicalRoot.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            + Path.DirectorySeparatorChar;
        return canonicalPath.StartsWith(rootWithSeparator, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
    }

    private async Task StopProcessAsync(LaunchedApp app, CancellationToken cancellationToken)
    {
        try
        {
            if (app.HasExited)
            {
                return;
            }

            app.Process.Kill(entireProcessTree: true);
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(5));
            try
            {
                await app.Process.WaitForExitAsync(timeout.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                // The process did not exit in time; the kill request was already issued.
            }
        }
        catch (InvalidOperationException)
        {
        }
        catch (System.ComponentModel.Win32Exception)
        {
        }
        finally
        {
            app.Process.Dispose();
        }
    }

    private void PruneExitedLocked()
    {
        foreach (var (instanceId, app) in _launched.ToArray())
        {
            if (app.HasExited)
            {
                _launched.Remove(instanceId);
                _sessions.RevokeInstance(instanceId);
            }
        }
    }

    private void AuditLaunch(string traceId, string appId, string outcome, string? code)
    {
        _auditSink.Append(new AuditEventDraft
        {
            TraceId = traceId,
            EventType = AuditEventTypes.BrokerLaunch,
            Outcome = outcome,
            Code = code,
            BrokeredAppId = appId,
        });
    }

    private static readonly object ChildLogGate = new();

    private static async Task DrainAsync(StreamReader reader, string appId, string instanceId, bool isError, string? childLogDirectory)
    {
        try
        {
            while (true)
            {
                var line = await reader.ReadLineAsync().ConfigureAwait(false);
                if (line is null)
                {
                    return;
                }

                if (isError && line.Length > 0)
                {
                    Console.Error.WriteLine($"[app {appId}/{instanceId}] {line}");
                }

                if (!string.IsNullOrWhiteSpace(childLogDirectory) && line.Length > 0)
                {
                    AppendChildLog(childLogDirectory, appId, instanceId, isError ? "stderr" : "stdout", line);
                }
            }
        }
        catch (ObjectDisposedException)
        {
        }
        catch (IOException)
        {
        }
    }

    private static void AppendChildLog(string directory, string appId, string instanceId, string stream, string line)
    {
        try
        {
            Directory.CreateDirectory(directory);
            var path = Path.Combine(directory, $"{appId}-{instanceId}.log");
            lock (ChildLogGate)
            {
                File.AppendAllText(path, $"[{stream}] {line}{Environment.NewLine}");
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private static void TryKill(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch (InvalidOperationException)
        {
        }
        catch (System.ComponentModel.Win32Exception)
        {
        }
    }

    public async ValueTask DisposeAsync()
    {
        await StopAllAsync().ConfigureAwait(false);
    }

    [GeneratedRegex("\\{([A-Za-z0-9_]+)\\}", RegexOptions.CultureInvariant)]
    private static partial Regex TokenPattern();
}
