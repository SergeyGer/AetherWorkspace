using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Aether.Broker.Client;
using Aether.Contracts.Broker;
using Aether.Contracts.Errors;
using Aether.Contracts.V1;
using Aether.Security.Audit;
using Aether.Testing;

namespace Aether.Desktop.E2E.Tests;

/// <summary>
/// Starts the real <c>Aether.BrokerApp</c> process over the Windows named-pipe transport and
/// exposes the artifacts the reference-desktop tests need: the operator handshake file
/// (endpoint + operator token), the broker audit JSONL, child-process logs and an
/// authenticated operator <see cref="BrokerClient"/>.
/// </summary>
/// <remarks>
/// <para>
/// The harness is intentionally self-contained: <see cref="EndToEndHarness"/> in
/// <c>tests/Aether.Testing</c> force-selects the Unix-domain-socket transport, so it cannot
/// prove the Windows named-pipe GUI path. This helper launches the broker with an explicit
/// <c>--transport named-pipe</c>, waits for both the machine-readable
/// <c>AETHER_BROKER_READY</c> line and the atomically written handshake file, and validates
/// that the two agree.
/// </para>
/// <para>
/// Every wait is bounded. On disposal the broker process tree (broker plus the sample app
/// processes it launched) is killed and the temporary run directory is removed, unless
/// <c>AETHER_DESKTOP_E2E_KEEP_ARTIFACTS=1</c> is set for post-mortem inspection.
/// </para>
/// </remarks>
internal sealed class NamedPipeBrokerHarness : IAsyncDisposable
{
    internal const string ReadyPrefix = "AETHER_BROKER_READY ";
    private const string KeepArtifactsVariable = "AETHER_DESKTOP_E2E_KEEP_ARTIFACTS";
    private const string ArtifactsRootVariable = "AETHER_DESKTOP_E2E_ARTIFACTS";
    private static readonly TimeSpan ReadyTimeout = TimeSpan.FromSeconds(60);
    private static readonly TimeSpan ShutdownTimeout = TimeSpan.FromSeconds(15);

    private readonly Process _process;
    private readonly CancellationTokenSource _pumpCancellation;
    private readonly Task _stdoutPump;
    private readonly Task _stderrPump;
    private bool _disposed;

    private NamedPipeBrokerHarness(
        Process process,
        BrokerEndpointInfo endpoint,
        string readyLine,
        string runDirectory,
        string endpointFile,
        string brokerAuditFile,
        string childLogDirectory,
        string layoutStoreDirectory,
        string brokerStdOutFile,
        string brokerStdErrFile,
        CancellationTokenSource pumpCancellation,
        Task stdoutPump,
        Task stderrPump)
    {
        _process = process;
        _pumpCancellation = pumpCancellation;
        _stdoutPump = stdoutPump;
        _stderrPump = stderrPump;
        Endpoint = endpoint;
        ReadyLine = readyLine;
        RunDirectory = runDirectory;
        EndpointFile = endpointFile;
        BrokerAuditFile = brokerAuditFile;
        ChildLogDirectory = childLogDirectory;
        LayoutStoreDirectory = layoutStoreDirectory;
        BrokerStdOutFile = brokerStdOutFile;
        BrokerStdErrFile = brokerStdErrFile;
    }

    /// <summary>Temporary directory holding the handshake file, audit log and child logs.</summary>
    public string RunDirectory { get; }

    /// <summary>Operator handshake file written by the broker (<c>--endpoint-file</c>).</summary>
    public string EndpointFile { get; }

    /// <summary>Broker audit chain written by the broker (<c>--audit-file</c>).</summary>
    public string BrokerAuditFile { get; }

    /// <summary>Directory with per-sample-app stdout/stderr capture (<c>--child-log-dir</c>).</summary>
    public string ChildLogDirectory { get; }

    /// <summary>Trusted directory for durable workspace layout files (<c>--layout-store</c>, v0.5).</summary>
    public string LayoutStoreDirectory { get; }

    public string BrokerStdOutFile { get; }

    public string BrokerStdErrFile { get; }

    /// <summary>Parsed handshake: named-pipe endpoint, operator identity and operator token.</summary>
    public BrokerEndpointInfo Endpoint { get; }

    /// <summary>Payload of the broker's <c>AETHER_BROKER_READY</c> stdout line.</summary>
    public string ReadyLine { get; }

    /// <summary>Operating-system process id of the broker process.</summary>
    public int ProcessId => _process.Id;

    /// <summary>Starts the broker and waits until both readiness signals are consistent.</summary>
    public static async Task<NamedPipeBrokerHarness> StartAsync(string testName, CancellationToken cancellationToken = default)
    {
        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException(
                "The reference-desktop named-pipe harness requires Windows: named pipes and WPF UI automation are Windows-only.");
        }

        var runDirectory = CreateRunDirectory(testName);
        Directory.CreateDirectory(runDirectory);
        var endpointFile = Path.Combine(runDirectory, "broker-endpoint.json");
        var brokerAuditFile = Path.Combine(runDirectory, "broker-audit.jsonl");
        var childLogDirectory = Path.Combine(runDirectory, "child-logs");
        var layoutStoreDirectory = Path.Combine(runDirectory, "layouts");
        var brokerStdOutFile = Path.Combine(runDirectory, "broker.out");
        var brokerStdErrFile = Path.Combine(runDirectory, "broker.err");

        var pumpCancellation = new CancellationTokenSource();
        Process? process = null;
        Task? stdoutPump = null;
        Task? stderrPump = null;
        try
        {
            process = new Process
            {
                StartInfo = CreateBrokerStartInfo(endpointFile, brokerAuditFile, childLogDirectory, layoutStoreDirectory),
            };
            if (!process.Start())
            {
                throw new InvalidOperationException("The Aether.BrokerApp process could not be started.");
            }

            var ready = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
            stdoutPump = PumpAsync(process.StandardOutput, brokerStdOutFile, ready, pumpCancellation.Token);
            stderrPump = PumpAsync(process.StandardError, brokerStdErrFile, readySource: null, pumpCancellation.Token);

            var endpoint = await WaitForReadyAsync(
                process,
                endpointFile,
                ready.Task,
                brokerStdOutFile,
                brokerStdErrFile,
                cancellationToken).ConfigureAwait(false);
            var readyLine = await ready.Task.ConfigureAwait(false);
            ValidateHandshake(readyLine, endpoint, endpointFile, brokerAuditFile);

            return new NamedPipeBrokerHarness(
                process,
                endpoint,
                readyLine,
                runDirectory,
                endpointFile,
                brokerAuditFile,
                childLogDirectory,
                layoutStoreDirectory,
                brokerStdOutFile,
                brokerStdErrFile,
                pumpCancellation,
                stdoutPump,
                stderrPump);
        }
        catch
        {
            await StopProcessAsync(process).ConfigureAwait(false);
            pumpCancellation.Cancel();
            await AwaitPumpsAsync(stdoutPump, stderrPump).ConfigureAwait(false);
            pumpCancellation.Dispose();
            process?.Dispose();
            TryDeleteDirectory(runDirectory);
            throw;
        }
    }

    /// <summary>Registers an operator <see cref="BrokerClient"/> from the handshake file.</summary>
    public async Task<BrokerClient> CreateOperatorClientAsync(CancellationToken cancellationToken = default)
    {
        var client = new BrokerClient(BrokerChannelFactory.CreateChannel(Endpoint.Transport, Endpoint.Endpoint));
        try
        {
            await client.RegisterAsync(
                    Endpoint.OperatorAppId,
                    Endpoint.OperatorToken,
                    "desktop-e2e-operator-" + Environment.ProcessId,
                    cancellationToken)
                .ConfigureAwait(false);
            return client;
        }
        catch
        {
            await client.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    /// <summary>
    /// Polls the authenticated health probe until every expected sample app is reported
    /// <c>HEALTHY</c>. A healthy entry is produced from the broker's own launch/registration
    /// bookkeeping, so it doubles as evidence that the three independent sample processes ran.
    /// </summary>
    public async Task<HealthProbeReply> WaitForAppHealthAsync(
        BrokerClient operatorClient,
        IReadOnlyList<string> expectedAppIds,
        TimeSpan timeout,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(operatorClient);
        ArgumentNullException.ThrowIfNull(expectedAppIds);

        var deadline = DateTime.UtcNow + timeout;
        HealthProbeReply? lastReply = null;
        while (DateTime.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();
            lastReply = await operatorClient.HealthProbeAsync(cancellationToken).ConfigureAwait(false);
            if (expectedAppIds.All(appId => lastReply.AppHealth.Any(entry => IsHealthyEntry(entry, appId))))
            {
                return lastReply;
            }

            await Task.Delay(250, cancellationToken).ConfigureAwait(false);
        }

        throw new TimeoutException(
            $"The broker did not report all sample apps HEALTHY within {timeout.TotalSeconds:0}s. "
            + $"Last app_health: [{string.Join(", ", lastReply?.AppHealth ?? [])}]."
            + Environment.NewLine
            + ReadDiagnostics());
    }

    /// <summary>Reads the tamper-evident broker audit chain persisted by the broker.</summary>
    public IReadOnlyList<AuditEvent> ReadBrokerAudit() =>
        File.Exists(BrokerAuditFile) ? JsonlAuditStore.ReadAll(BrokerAuditFile) : [];

    /// <summary>Reads the captured stdout/stderr lines of one launched sample app.</summary>
    public IReadOnlyList<string> ReadChildLogLines(string appId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(appId);
        if (!Directory.Exists(ChildLogDirectory))
        {
            return [];
        }

        var lines = new List<string>();
        foreach (var path in Directory.EnumerateFiles(ChildLogDirectory, appId + "-*.log", SearchOption.TopDirectoryOnly))
        {
            try
            {
                lines.AddRange(File.ReadAllLines(path));
            }
            catch (IOException)
            {
                // The broker may still be flushing a just-launched child process.
            }
        }

        return lines;
    }

    /// <summary>Human-readable diagnostics for assertion messages (bounded tails only).</summary>
    public string ReadDiagnostics()
    {
        var builder = new StringBuilder();
        builder.AppendLine("Broker run directory: " + RunDirectory);
        builder.AppendLine($"Broker transport: {Endpoint.Transport} endpoint={Endpoint.Endpoint}");
        builder.AppendLine("Broker stdout tail: " + Environment.NewLine + ReadTail(BrokerStdOutFile));
        builder.AppendLine("Broker stderr tail: " + Environment.NewLine + ReadTail(BrokerStdErrFile));
        return builder.ToString();
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        await StopProcessAsync(_process).ConfigureAwait(false);
        _pumpCancellation.Cancel();
        await AwaitPumpsAsync(_stdoutPump, _stderrPump).ConfigureAwait(false);
        _pumpCancellation.Dispose();
        _process.Dispose();
        if (!ShouldKeepArtifacts())
        {
            TryDeleteDirectory(RunDirectory);
        }
    }

    private static ProcessStartInfo CreateBrokerStartInfo(string endpointFile, string auditFile, string childLogDirectory, string layoutStoreDirectory)
    {
        var (fileName, prefixAssembly) = DesktopExecutableLocator.ResolveBrokerHost();
        var startInfo = new ProcessStartInfo
        {
            FileName = fileName,
            WorkingDirectory = TestRepo.Root,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };

        if (prefixAssembly is not null)
        {
            startInfo.ArgumentList.Add(prefixAssembly);
        }

        AddArgument(startInfo, "--transport", BrokerTransports.NamedPipe);
        AddArgument(startInfo, "--registry", TestRepo.PathTo("samples", "registry", "apps.json"));
        AddArgument(startInfo, "--contexts", TestRepo.PathTo("samples", "contexts", "contexts.json"));
        foreach (var token in TestData.LaunchTokens())
        {
            AddArgument(startInfo, "--token", token.Key + "=" + token.Value);
        }

        foreach (var allowedRoot in TestData.AllowedExecutableRoots())
        {
            AddArgument(startInfo, "--allowed-root", allowedRoot);
        }

        AddArgument(startInfo, "--endpoint-file", endpointFile);
        AddArgument(startInfo, "--audit-file", auditFile);
        AddArgument(startInfo, "--child-log-dir", childLogDirectory);
        AddArgument(startInfo, "--layout-store", layoutStoreDirectory);
        AddArgument(startInfo, "--max-actions", "6");
        return startInfo;
    }

    private static void AddArgument(ProcessStartInfo startInfo, string name, string value)
    {
        startInfo.ArgumentList.Add(name);
        startInfo.ArgumentList.Add(value);
    }

    private static async Task<BrokerEndpointInfo> WaitForReadyAsync(
        Process process,
        string endpointFile,
        Task<string> readyLine,
        string stdoutFile,
        string stderrFile,
        CancellationToken cancellationToken)
    {
        var deadline = DateTime.UtcNow + ReadyTimeout;
        while (DateTime.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var endpoint = TryReadEndpointFile(endpointFile);
            if (endpoint is not null && readyLine.IsCompleted)
            {
                return endpoint;
            }

            if (process.HasExited)
            {
                throw new InvalidOperationException(
                    $"The Aether.BrokerApp process exited with code {process.ExitCode} before becoming ready."
                    + Environment.NewLine
                    + "Broker stdout tail:" + Environment.NewLine + ReadTail(stdoutFile)
                    + Environment.NewLine
                    + "Broker stderr tail:" + Environment.NewLine + ReadTail(stderrFile));
            }

            await Task.Delay(50, cancellationToken).ConfigureAwait(false);
        }

        throw new TimeoutException(
            $"The broker did not write both the AETHER_BROKER_READY line and '{endpointFile}' "
            + $"within {ReadyTimeout.TotalSeconds:0}s."
            + Environment.NewLine
            + "Broker stdout tail:" + Environment.NewLine + ReadTail(stdoutFile)
            + Environment.NewLine
            + "Broker stderr tail:" + Environment.NewLine + ReadTail(stderrFile));
    }

    private static BrokerEndpointInfo? TryReadEndpointFile(string path)
    {
        if (!File.Exists(path))
        {
            return null;
        }

        try
        {
            return BrokerEndpointInfo.Read(path);
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
        catch (AetherContractException)
        {
            return null;
        }
    }

    private static void ValidateHandshake(
        string readyLine,
        BrokerEndpointInfo endpoint,
        string endpointFile,
        string auditFile)
    {
        if (!string.Equals(endpoint.Transport, BrokerTransports.NamedPipe, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"The broker handshake file reports transport '{endpoint.Transport}', expected '{BrokerTransports.NamedPipe}'.");
        }

        if (string.IsNullOrWhiteSpace(endpoint.Endpoint) || string.IsNullOrWhiteSpace(endpoint.OperatorToken))
        {
            throw new InvalidOperationException("The broker handshake file is missing the named-pipe endpoint or operator token.");
        }

        if (endpoint.AuditFile is null
            || !string.Equals(Path.GetFullPath(endpoint.AuditFile), Path.GetFullPath(auditFile), StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"The broker handshake file reports audit file '{endpoint.AuditFile}', expected '{Path.GetFullPath(auditFile)}'.");
        }

        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(readyLine);
        }
        catch (JsonException exception)
        {
            throw new InvalidOperationException(
                "The AETHER_BROKER_READY payload is not valid JSON: " + readyLine,
                exception);
        }

        using (document)
        {
            var root = document.RootElement;
            RequireReadyField(root, "transport", BrokerTransports.NamedPipe);
            RequireReadyField(root, "endpoint", endpoint.Endpoint);
            RequireReadyField(root, "operatorAppId", endpoint.OperatorAppId);
            RequireReadyField(root, "endpointFile", endpointFile);
        }
    }

    private static void RequireReadyField(JsonElement root, string name, string expected)
    {
        if (!root.TryGetProperty(name, out var property)
            || !string.Equals(property.GetString(), expected, StringComparison.OrdinalIgnoreCase))
        {
            var actual = root.TryGetProperty(name, out var value) ? value.GetString() : "<missing>";
            throw new InvalidOperationException(
                $"The AETHER_BROKER_READY payload field '{name}' is '{actual}', expected '{expected}'.");
        }
    }

    private static bool IsHealthyEntry(string entry, string appId) =>
        entry.StartsWith(appId + "=", StringComparison.Ordinal)
        && entry.EndsWith("=HEALTHY", StringComparison.Ordinal);

    private static async Task PumpAsync(
        StreamReader reader,
        string destinationFile,
        TaskCompletionSource<string>? readySource,
        CancellationToken cancellationToken)
    {
        try
        {
            await using var writer = new StreamWriter(destinationFile, append: false) { AutoFlush = true };
            while (!cancellationToken.IsCancellationRequested)
            {
                var line = await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false);
                if (line is null)
                {
                    return;
                }

                await writer.WriteLineAsync(line).ConfigureAwait(false);
                if (readySource is not null && line.StartsWith(ReadyPrefix, StringComparison.Ordinal))
                {
                    readySource.TrySetResult(line[ReadyPrefix.Length..]);
                }
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (ObjectDisposedException)
        {
        }
        catch (IOException)
        {
        }
        catch (InvalidOperationException)
        {
            // The process exited while the stream was being read.
        }
    }

    private static async Task StopProcessAsync(Process? process)
    {
        if (process is null)
        {
            return;
        }

        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
                await process.WaitForExitAsync().WaitAsync(ShutdownTimeout).ConfigureAwait(false);
            }
        }
        catch (TimeoutException)
        {
        }
        catch (OperationCanceledException)
        {
        }
        catch (InvalidOperationException)
        {
        }
        catch (System.ComponentModel.Win32Exception)
        {
        }
    }

    private static async Task AwaitPumpsAsync(params Task?[] pumps)
    {
        var pending = pumps.Where(pump => pump is not null).Cast<Task>().ToArray();
        if (pending.Length == 0)
        {
            return;
        }

        try
        {
            await Task.WhenAll(pending).WaitAsync(ShutdownTimeout).ConfigureAwait(false);
        }
        catch (TimeoutException)
        {
        }
        catch (OperationCanceledException)
        {
        }
    }

    private static string CreateRunDirectory(string testName)
    {
        var safeName = new string(testName.Where(char.IsLetterOrDigit).ToArray());
        if (safeName.Length == 0)
        {
            safeName = "desktop";
        }

        var configuredRoot = Environment.GetEnvironmentVariable(ArtifactsRootVariable);
        var root = string.IsNullOrWhiteSpace(configuredRoot) ? Path.GetTempPath() : configuredRoot;
        Directory.CreateDirectory(root);
        return Path.Combine(root, $"aether-desktop-e2e-{safeName}-{Guid.NewGuid().ToString("n")[..8]}");
    }

    private static bool ShouldKeepArtifacts() =>
        string.Equals(Environment.GetEnvironmentVariable(KeepArtifactsVariable), "1", StringComparison.Ordinal);

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private static string ReadTail(string path, int maxLines = 40)
    {
        if (!File.Exists(path))
        {
            return "<missing: " + path + ">";
        }

        try
        {
            return string.Join(Environment.NewLine, File.ReadAllLines(path).TakeLast(maxLines));
        }
        catch (IOException exception)
        {
            return "<unreadable: " + exception.Message + ">";
        }
    }
}
