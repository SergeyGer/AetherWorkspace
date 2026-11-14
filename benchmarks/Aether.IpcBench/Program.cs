using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Nodes;
using Aether.Broker.Client;
using Aether.Contracts;
using Aether.Contracts.Broker;
using Aether.Contracts.Json;
using Aether.Contracts.Models.Contexts;

namespace Aether.IpcBench;

/// <summary>
/// Measured end-to-end IPC harness: starts a real local broker process, launches
/// the three synthetic sample processes, then measures 1 KB typed-context
/// publish round-trips (including delivery accounting to three subscribers).
/// Results are raw Stopwatch samples on the machine that runs the harness; the
/// benchmark never claims a universal latency guarantee.
/// </summary>
public static class Program
{
    public static async Task<int> Main(string[] args)
    {
        var options = IpcBenchOptions.Parse(args);
        var repoRoot = FindRepoRoot();
        var configuration = AppContext.BaseDirectory.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}Debug{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase)
            ? "Debug"
            : "Release";
        var dotnet = FindDotnet() ?? throw new InvalidOperationException(".NET muxer not found in PATH/DOTNET_ROOT.");
        var brokerDll = Path.Combine(repoRoot, "src", "Aether.BrokerApp", "bin", configuration, "net10.0", "Aether.BrokerApp.dll");
        var samplesDll = Path.Combine(repoRoot, "src", "Aether.SampleApps.Headless", "bin", configuration, "net10.0", "Aether.SampleApps.Headless.dll");
        if (!File.Exists(brokerDll) || !File.Exists(samplesDll))
        {
            Console.Error.WriteLine("Build the solution in the same configuration before running the IPC benchmark.");
            return 2;
        }

        var runDirectory = Path.Combine(Path.GetTempPath(), "aether-ipcbench-" + Guid.NewGuid().ToString("n")[..8]);
        Directory.CreateDirectory(runDirectory);
        var endpointFile = Path.Combine(runDirectory, "broker-endpoint.json");
        var contextsPath = Path.Combine(runDirectory, "contexts.json");
        PrepareContexts(repoRoot, contextsPath, options.PayloadBytes);
        var brokerLog = Path.Combine(runDirectory, "broker.log");

        Process? broker = null;
        BrokerClient? client = null;
        try
        {
            broker = StartBroker(dotnet, brokerDll, repoRoot, contextsPath, endpointFile, dotnet, samplesDll, brokerLog);
            var endpoint = await WaitForEndpointAsync(endpointFile, broker, TimeSpan.FromSeconds(20)).ConfigureAwait(false);
            client = new BrokerClient(BrokerChannelFactory.CreateChannel(endpoint.Transport, endpoint.Endpoint));
            await client.RegisterAsync(endpoint.OperatorAppId, endpoint.OperatorToken, "ipc-bench-operator").ConfigureAwait(false);

            var launched = new List<string>();
            foreach (var appId in new[] { "sample.watchlist", "sample.risk", "sample.chart" })
            {
                var reply = await client.LaunchAppAsync(appId, TraceIdGenerator.NewTraceId()).ConfigureAwait(false);
                if (!reply.Accepted)
                {
                    Console.Error.WriteLine($"launch failed for {appId}: {reply.DenialCode} {reply.DenialMessage}");
                    return 3;
                }

                launched.Add(reply.InstanceId);
            }

            var traceId = TraceIdGenerator.NewTraceId();
            for (var index = 0; index < options.Warmup; index++)
            {
                var reply = await client.PublishContextAsync("synthetic.instrument.bench", DataClassification.Internal, traceId).ConfigureAwait(false);
                if (!reply.Accepted)
                {
                    Console.Error.WriteLine($"warmup publish denied: {reply.DenialCode} {reply.DenialMessage}");
                    return 3;
                }
            }

            var publishLatencies = new List<double>(options.Samples);
            var delivered = 0;
            for (var index = 0; index < options.Samples; index++)
            {
                var stopwatch = Stopwatch.StartNew();
                var reply = await client.PublishContextAsync("synthetic.instrument.bench", DataClassification.Internal, traceId).ConfigureAwait(false);
                stopwatch.Stop();
                if (!reply.Accepted)
                {
                    Console.Error.WriteLine($"publish denied: {reply.DenialCode}");
                    return 3;
                }

                delivered = reply.DeliveredCount;
                publishLatencies.Add(stopwatch.Elapsed.TotalMilliseconds);
            }

            var healthLatencies = new List<double>(options.Samples);
            for (var index = 0; index < options.Samples; index++)
            {
                var stopwatch = Stopwatch.StartNew();
                await client.HealthProbeAsync().ConfigureAwait(false);
                stopwatch.Stop();
                healthLatencies.Add(stopwatch.Elapsed.TotalMilliseconds);
            }

            var payloadBytes = new FileInfo(contextsPath).Length;
            Report(repoRoot, options, publishLatencies, healthLatencies, delivered, payloadBytes);
            return 0;
        }
        finally
        {
            client?.DisposeAsync().AsTask().GetAwaiter().GetResult();
            if (broker is { HasExited: false })
            {
                try
                {
                    broker.Kill(entireProcessTree: true);
                    broker.WaitForExit(5000);
                }
                catch (InvalidOperationException)
                {
                }
            }

            broker?.Dispose();
            try
            {
                Directory.Delete(runDirectory, recursive: true);
            }
            catch (IOException)
            {
            }
        }
    }

    private static void Report(
        string repoRoot,
        IpcBenchOptions options,
        IReadOnlyList<double> publish,
        IReadOnlyList<double> health,
        int delivered,
        long contextsFileBytes)
    {
        var machine = new
        {
            os = RuntimeInformation.OSDescription,
            architecture = RuntimeInformation.ProcessArchitecture.ToString(),
            framework = RuntimeInformation.FrameworkDescription,
            processors = Environment.ProcessorCount,
            totalMemoryMb = GC.GetGCMemoryInfo().TotalAvailableMemoryBytes / (1024 * 1024),
            transport = OperatingSystem.IsWindows() ? "named-pipe" : "unix-domain-socket",
            configuration = AppContext.BaseDirectory.Contains("Debug", StringComparison.OrdinalIgnoreCase) ? "Debug" : "Release",
        };

        Console.WriteLine("## AetherWorkspace IPC benchmark (raw measured baseline)");
        Console.WriteLine();
        Console.WriteLine("Machine / runtime:");
        Console.WriteLine("```json");
        Console.WriteLine(AetherJson.SerializeIndented(machine));
        Console.WriteLine("```");
        Console.WriteLine();
        Console.WriteLine($"Transport: {machine.transport}; subscribers: 3; samples per metric: {options.Samples}; warmup: {options.Warmup}; contexts file: {contextsFileBytes} bytes; delivered per publish: {delivered}");
        Console.WriteLine();
        Console.WriteLine("| metric | p50 ms | p95 ms | p99 ms | max ms |");
        Console.WriteLine("|---|---|---|---|---|");
        Console.WriteLine($"| publish 1 KB context round-trip (3 subscribers) | {Percentile(publish, 50):0.000} | {Percentile(publish, 95):0.000} | {Percentile(publish, 99):0.000} | {publish.Max():0.000} |");
        Console.WriteLine($"| health probe round-trip | {Percentile(health, 50):0.000} | {Percentile(health, 95):0.000} | {Percentile(health, 99):0.000} | {health.Max():0.000} |");
        Console.WriteLine();
        Console.WriteLine("Raw samples (ms, publish):");
        Console.WriteLine("```");
        Console.WriteLine(string.Join(", ", publish.Select(value => value.ToString("0.000", CultureInfo.InvariantCulture))));
        Console.WriteLine("```");
        Console.WriteLine();
        Console.WriteLine($"Repository: {repoRoot}");
    }

    private static double Percentile(IReadOnlyList<double> values, int percentile)
    {
        var sorted = values.OrderBy(value => value).ToArray();
        var index = (int)Math.Ceiling((percentile / 100.0) * sorted.Length) - 1;
        return sorted[Math.Clamp(index, 0, sorted.Length - 1)];
    }

    private static void PrepareContexts(string repoRoot, string destination, int targetPayloadBytes)
    {
        var source = Path.Combine(repoRoot, "samples", "contexts", "contexts.json");
        var root = JsonNode.Parse(File.ReadAllText(source))!.AsObject();
        var contexts = root["contexts"]!.AsArray();
        var filler = new string('x', Math.Max(16, targetPayloadBytes - 220));
        contexts.Add(JsonNode.Parse(AetherJson.Serialize(new
        {
            contextRef = "synthetic.instrument.bench",
            contextType = InstrumentContext.ContextType,
            classification = "INTERNAL",
            payload = new
            {
                type = InstrumentContext.ContextType,
                name = "Benchmark instrument " + filler,
                id = new { ticker = "BNCH", isin = "SYN00000BNCH4" },
            },
        })));
        File.WriteAllText(destination, root.ToJsonString(new JsonSerializerOptions { WriteIndented = false }));
    }

    private static Process StartBroker(
        string dotnet,
        string brokerDll,
        string repoRoot,
        string contextsPath,
        string endpointFile,
        string dotnetToken,
        string samplesDll,
        string logFile)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = dotnet,
            WorkingDirectory = repoRoot,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        startInfo.ArgumentList.Add(brokerDll);
        startInfo.ArgumentList.Add("--registry");
        startInfo.ArgumentList.Add(Path.Combine(repoRoot, "samples", "registry", "apps.json"));
        startInfo.ArgumentList.Add("--contexts");
        startInfo.ArgumentList.Add(contextsPath);
        startInfo.ArgumentList.Add("--token");
        startInfo.ArgumentList.Add("DOTNET=" + dotnetToken);
        startInfo.ArgumentList.Add("--token");
        startInfo.ArgumentList.Add("SAMPLES_DLL=" + samplesDll);
        startInfo.ArgumentList.Add("--token");
        startInfo.ArgumentList.Add("AETHER_HOME=" + repoRoot);
        startInfo.ArgumentList.Add("--token");
        startInfo.ArgumentList.Add("FIXTURES_DIR=" + Path.Combine(repoRoot, "samples", "fixtures"));
        startInfo.ArgumentList.Add("--allowed-root");
        startInfo.ArgumentList.Add(Path.GetDirectoryName(dotnetToken)!);
        startInfo.ArgumentList.Add("--allowed-root");
        startInfo.ArgumentList.Add(repoRoot);
        startInfo.ArgumentList.Add("--endpoint-file");
        startInfo.ArgumentList.Add(endpointFile);
        startInfo.ArgumentList.Add("--audit-file");
        startInfo.ArgumentList.Add(Path.Combine(Path.GetDirectoryName(endpointFile)!, "audit.jsonl"));
        startInfo.ArgumentList.Add("--child-log-dir");
        startInfo.ArgumentList.Add(Path.Combine(Path.GetDirectoryName(endpointFile)!, "child-logs"));
        startInfo.ArgumentList.Add("--max-actions");
        startInfo.ArgumentList.Add("6");

        var process = Process.Start(startInfo) ?? throw new InvalidOperationException("Could not start broker process.");
        var writer = new StreamWriter(logFile) { AutoFlush = true };
        process.OutputDataReceived += (_, eventArgs) =>
        {
            if (eventArgs.Data is not null)
            {
                writer.WriteLine(eventArgs.Data);
            }
        };
        process.ErrorDataReceived += (_, eventArgs) =>
        {
            if (eventArgs.Data is not null)
            {
                writer.WriteLine("ERR " + eventArgs.Data);
            }
        };
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        return process;
    }

    private static async Task<BrokerEndpointInfo> WaitForEndpointAsync(string endpointFile, Process broker, TimeSpan timeout)
    {
        var deadline = DateTimeOffset.UtcNow + timeout;
        while (DateTimeOffset.UtcNow < deadline)
        {
            if (File.Exists(endpointFile))
            {
                try
                {
                    return BrokerEndpointInfo.Read(endpointFile);
                }
                catch (IOException)
                {
                }
            }

            if (broker.HasExited)
            {
                throw new InvalidOperationException($"Broker exited before becoming ready (code {broker.ExitCode}).");
            }

            await Task.Delay(50).ConfigureAwait(false);
        }

        throw new TimeoutException("Broker did not write its endpoint file in time.");
    }

    private static string? FindDotnet()
    {
        var dotnetRoot = Environment.GetEnvironmentVariable("DOTNET_ROOT");
        if (!string.IsNullOrWhiteSpace(dotnetRoot))
        {
            var named = Path.Combine(dotnetRoot, OperatingSystem.IsWindows() ? "dotnet.exe" : "dotnet");
            if (File.Exists(named))
            {
                return named;
            }
        }

        var processPath = Environment.ProcessPath;
        if (processPath is not null && Path.GetFileNameWithoutExtension(processPath).Equals("dotnet", StringComparison.OrdinalIgnoreCase))
        {
            return processPath;
        }

        var pathVariable = Environment.GetEnvironmentVariable("PATH") ?? "";
        foreach (var directory in pathVariable.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            var candidate = Path.Combine(directory, OperatingSystem.IsWindows() ? "dotnet.exe" : "dotnet");
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        return null;
    }

    private static string FindRepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "AetherWorkspace.slnx")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException("Could not locate the repository root.");
    }
}
