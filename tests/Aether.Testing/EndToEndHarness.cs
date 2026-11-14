using System.Diagnostics;
using Aether.Broker.Client;
using Aether.Contracts.Broker;
using Aether.Security.Audit;

namespace Aether.Testing;

/// <summary>Result of one headless-shell process invocation.</summary>
public sealed record ShellRun(int ExitCode, string? ResultJson, string StdOut, string StdErr);

/// <summary>
/// Multi-process end-to-end harness: a real broker process, the shell as a
/// separate process and the three headless sample processes launched by the
/// broker. Used to prove the reference workflow across independent processes.
/// </summary>
public sealed class EndToEndHarness : IAsyncDisposable
{
    private readonly Process _brokerProcess;
    private BrokerClient? _operatorClient;
    private bool _disposed;

    private EndToEndHarness(string runDirectory, Process brokerProcess, BrokerEndpointInfo endpoint)
    {
        RunDirectory = runDirectory;
        _brokerProcess = brokerProcess;
        Endpoint = endpoint;
        EndpointFile = Path.Combine(runDirectory, "broker-endpoint.json");
        BrokerAuditFile = Path.Combine(runDirectory, "broker-audit.jsonl");
        ShellAuditFile = Path.Combine(runDirectory, "shell-audit.jsonl");
        ChildLogDirectory = Path.Combine(runDirectory, "child-logs");
        BrokerStdOutFile = Path.Combine(runDirectory, "broker.out");
        BrokerStdErrFile = Path.Combine(runDirectory, "broker.err");
    }

    public string RunDirectory { get; }

    public string EndpointFile { get; }

    public string BrokerAuditFile { get; }

    public string ShellAuditFile { get; }

    public string ChildLogDirectory { get; }

    public string BrokerStdOutFile { get; }

    public string BrokerStdErrFile { get; }

    public BrokerEndpointInfo Endpoint { get; }

    public static string ShellDll => TestRepo.FindProjectOutput("src/Aether.Shell.Headless", "Aether.Shell.Headless.dll");

    public static string BrokerAppDll => TestRepo.FindProjectOutput("src/Aether.BrokerApp", "Aether.BrokerApp.dll");

    public static async Task<EndToEndHarness> StartAsync(string testName, CancellationToken cancellationToken = default)
    {
        var runDirectory = Path.Combine(Path.GetTempPath(), "aether-e2e-" + testName[..Math.Min(6, testName.Length)] + "-" + Guid.NewGuid().ToString("n")[..6]);
        Directory.CreateDirectory(runDirectory);
        var endpointFile = Path.Combine(runDirectory, "broker-endpoint.json");
        var auditFile = Path.Combine(runDirectory, "broker-audit.jsonl");
        var childLogs = Path.Combine(runDirectory, "child-logs");
        var dotnet = TestData.FindDotnetExecutable();

        var startInfo = new ProcessStartInfo
        {
            FileName = dotnet,
            WorkingDirectory = TestRepo.Root,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        startInfo.ArgumentList.Add(BrokerAppDll);
        startInfo.ArgumentList.Add("--registry");
        startInfo.ArgumentList.Add(TestRepo.PathTo("samples", "registry", "apps.json"));
        startInfo.ArgumentList.Add("--contexts");
        startInfo.ArgumentList.Add(TestRepo.PathTo("samples", "contexts", "contexts.json"));
        startInfo.ArgumentList.Add("--token");
        startInfo.ArgumentList.Add("DOTNET=" + dotnet);
        startInfo.ArgumentList.Add("--token");
        startInfo.ArgumentList.Add("SAMPLES_DLL=" + TestData.SamplesDllPath);
        startInfo.ArgumentList.Add("--token");
        startInfo.ArgumentList.Add("AETHER_HOME=" + TestRepo.Root);
        startInfo.ArgumentList.Add("--token");
        startInfo.ArgumentList.Add("FIXTURES_DIR=" + TestData.FixturesDirectory);
        startInfo.ArgumentList.Add("--allowed-root");
        startInfo.ArgumentList.Add(Path.GetDirectoryName(dotnet)!);
        startInfo.ArgumentList.Add("--allowed-root");
        startInfo.ArgumentList.Add(TestRepo.Root);
        startInfo.ArgumentList.Add("--endpoint-file");
        startInfo.ArgumentList.Add(endpointFile);
        startInfo.ArgumentList.Add("--audit-file");
        startInfo.ArgumentList.Add(auditFile);
        startInfo.ArgumentList.Add("--child-log-dir");
        startInfo.ArgumentList.Add(childLogs);
        startInfo.ArgumentList.Add("--max-actions");
        startInfo.ArgumentList.Add("6");

        var broker = new Process { StartInfo = startInfo };
        var stdout = new StreamWriter(Path.Combine(runDirectory, "broker.out")) { AutoFlush = true };
        var stderr = new StreamWriter(Path.Combine(runDirectory, "broker.err")) { AutoFlush = true };
        broker.OutputDataReceived += (_, eventArgs) =>
        {
            if (eventArgs.Data is not null)
            {
                stdout.WriteLine(eventArgs.Data);
            }
        };
        broker.ErrorDataReceived += (_, eventArgs) =>
        {
            if (eventArgs.Data is not null)
            {
                stderr.WriteLine(eventArgs.Data);
            }
        };
        if (!broker.Start())
        {
            throw new InvalidOperationException("The broker process could not be started.");
        }

        broker.BeginOutputReadLine();
        broker.BeginErrorReadLine();

        var deadline = DateTimeOffset.UtcNow + TimeSpan.FromSeconds(30);
        BrokerEndpointInfo? endpoint = null;
        while (DateTimeOffset.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (File.Exists(endpointFile))
            {
                try
                {
                    endpoint = BrokerEndpointInfo.Read(endpointFile);
                    break;
                }
                catch (IOException)
                {
                }
            }

            if (broker.HasExited)
            {
                var brokerError = File.Exists(Path.Combine(runDirectory, "broker.err"))
                    ? File.ReadAllText(Path.Combine(runDirectory, "broker.err"))
                    : "";
                throw new InvalidOperationException($"Broker exited before becoming ready. {brokerError}");
            }

            await Task.Delay(50, cancellationToken).ConfigureAwait(false);
        }

        if (endpoint is null)
        {
            try
            {
                broker.Kill(entireProcessTree: true);
            }
            catch (InvalidOperationException)
            {
            }

            throw new TimeoutException("Broker did not write its endpoint file in time.");
        }

        return new EndToEndHarness(runDirectory, broker, endpoint);
    }

    public async Task<BrokerClient> GetOperatorClientAsync(CancellationToken cancellationToken = default)
    {
        if (_operatorClient is not null)
        {
            return _operatorClient;
        }

        _operatorClient = new BrokerClient(BrokerChannelFactory.CreateChannel(Endpoint.Transport, Endpoint.Endpoint));
        await _operatorClient.RegisterAsync(Endpoint.OperatorAppId, Endpoint.OperatorToken, "e2e-operator", cancellationToken)
            .ConfigureAwait(false);
        return _operatorClient;
    }

    /// <summary>Runs the headless shell process with the given extra arguments.</summary>
    public async Task<ShellRun> RunShellAsync(
        IEnumerable<string> extraArguments,
        string? outputFile = null,
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default,
        IReadOnlyDictionary<string, string>? environment = null)
    {
        var resultPath = outputFile ?? Path.Combine(RunDirectory, "shell-result-" + Guid.NewGuid().ToString("n")[..8] + ".json");
        var arguments = new List<string>
        {
            ShellDll,
            "--endpoint-file", EndpointFile,
            "--output-file", resultPath,
            "--max-actions", "6",
            "--timeout-seconds", "90",
        };
        arguments.AddRange(extraArguments);

        var startInfo = new ProcessStartInfo
        {
            FileName = TestData.FindDotnetExecutable(),
            WorkingDirectory = TestRepo.Root,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        if (environment is not null)
        {
            foreach (var pair in environment)
            {
                startInfo.Environment[pair.Key] = pair.Value;
            }
        }

        using var process = Process.Start(startInfo) ?? throw new InvalidOperationException("Could not start the shell process.");
        var stdoutTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var stderrTask = process.StandardError.ReadToEndAsync(cancellationToken);
        var effectiveTimeout = timeout ?? TimeSpan.FromSeconds(120);
        using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutSource.CancelAfter(effectiveTimeout);
        try
        {
            await process.WaitForExitAsync(timeoutSource.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            try
            {
                process.Kill(entireProcessTree: true);
            }
            catch (InvalidOperationException)
            {
            }

            throw;
        }

        var stdout = await stdoutTask.ConfigureAwait(false);
        var stderr = await stderrTask.ConfigureAwait(false);
        var resultJson = File.Exists(resultPath) ? await File.ReadAllTextAsync(resultPath, cancellationToken).ConfigureAwait(false) : null;
        return new ShellRun(process.ExitCode, resultJson, stdout, stderr);
    }

    /// <summary>Kills the broker process without graceful shutdown (fault injection).</summary>
    public async Task KillBrokerAsync()
    {
        try
        {
            if (!_brokerProcess.HasExited)
            {
                _brokerProcess.Kill(entireProcessTree: true);
                await _brokerProcess.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(false);
            }
        }
        catch (InvalidOperationException)
        {
        }
        catch (OperationCanceledException)
        {
        }
    }

    /// <summary>Kills orphaned headless sample processes left behind after a broker crash.</summary>
    public static void KillOrphanSampleApps()
    {
        if (!OperatingSystem.IsLinux())
        {
            return;
        }

        var marker = TestData.SamplesDllPath;
        foreach (var processDirectory in Directory.EnumerateDirectories("/proc"))
        {
            var name = Path.GetFileName(processDirectory);
            if (!int.TryParse(name, out var pid) || pid == Environment.ProcessId)
            {
                continue;
            }

            string? commandLine = null;
            try
            {
                commandLine = File.ReadAllText(Path.Combine(processDirectory, "cmdline")).Replace('\0', ' ');
            }
            catch (IOException)
            {
                continue;
            }
            catch (UnauthorizedAccessException)
            {
                continue;
            }

            if (commandLine is not null && commandLine.Contains(marker, StringComparison.Ordinal))
            {
                try
                {
                    using var process = Process.GetProcessById(pid);
                    process.Kill(entireProcessTree: true);
                }
                catch (ArgumentException)
                {
                }
                catch (InvalidOperationException)
                {
                }
            }
        }
    }

    public IReadOnlyList<string> ReadChildLogLines(string appId)
    {
        if (!Directory.Exists(ChildLogDirectory))
        {
            return [];
        }

        var lines = new List<string>();
        foreach (var path in Directory.EnumerateFiles(ChildLogDirectory, appId + "-*.log", SearchOption.TopDirectoryOnly))
        {
            lines.AddRange(File.ReadAllLines(path));
        }

        return lines;
    }

    public IReadOnlyList<AuditEvent> ReadBrokerAudit() => JsonlAuditStore.ReadAll(BrokerAuditFile);

    public AuditVerificationResult VerifyBrokerAudit() => AuditChainVerifier.VerifyFile(BrokerAuditFile);

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        if (_operatorClient is not null)
        {
            await _operatorClient.DisposeAsync().ConfigureAwait(false);
        }

        try
        {
            if (!_brokerProcess.HasExited)
            {
                _brokerProcess.Kill(entireProcessTree: true);
                await _brokerProcess.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (InvalidOperationException)
        {
        }
        finally
        {
            _brokerProcess.Dispose();
        }

        try
        {
            if (Directory.Exists(RunDirectory))
            {
                Directory.Delete(RunDirectory, recursive: true);
            }
        }
        catch (IOException)
        {
        }
    }
}
