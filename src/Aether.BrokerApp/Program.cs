using System.Globalization;
using Aether.Broker;
using Aether.Broker.Catalog;
using Aether.Broker.Registry;
using Aether.Contracts.Broker;
using Aether.Contracts.Errors;
using Aether.Contracts.Json;
using Aether.Contracts.Models;

namespace Aether.BrokerApp;

/// <summary>Local broker process entry point. No network listener is created.</summary>
public static class Program
{
    public static async Task<int> Main(string[] args)
    {
        BrokerAppOptions parsed;
        try
        {
            parsed = BrokerAppOptions.Parse(args);
        }
        catch (ArgumentException exception)
        {
            Console.Error.WriteLine(exception.Message);
            return 2;
        }

        try
        {
            var manifest = AetherJson.DeserializeStrict<AppManifestDocument>(File.ReadAllText(parsed.RegistryPath));
            var catalogDocument = AetherJson.DeserializeStrict<ContextCatalogDocument>(File.ReadAllText(parsed.ContextsPath));
            var registry = AppRegistry.Load(manifest);
            var contexts = ContextCatalog.Load(catalogDocument, parsed.MaxPayloadBytes);

            var tokens = new Dictionary<string, string>(parsed.Tokens, StringComparer.Ordinal);
            var roots = new List<string>(parsed.AllowedExecutableRoots);
            foreach (var tokenValue in tokens.Values.Where(Path.IsPathRooted))
            {
                var directory = Path.GetDirectoryName(Path.GetFullPath(tokenValue));
                if (!string.IsNullOrEmpty(directory) && Directory.Exists(directory))
                {
                    roots.Add(directory);
                }
            }

            var options = new BrokerOptions
            {
                Registry = registry,
                Contexts = contexts,
                Transport = parsed.Transport,
                Endpoint = parsed.Endpoint,
                LaunchTokens = tokens,
                AllowedExecutableRoots = roots.Distinct(ProcessPathComparer()).ToArray(),
                OperatorAppId = parsed.OperatorApp,
                OperatorToken = parsed.OperatorToken,
                MaxActionsPerPlan = parsed.MaxActions,
                MaxPayloadBytes = parsed.MaxPayloadBytes,
                WorkAreaWidth = parsed.WorkAreaWidth,
                WorkAreaHeight = parsed.WorkAreaHeight,
                AuditFile = parsed.AuditFile,
                ChildLogDirectory = parsed.ChildLogDirectory,
                RequireAppReadiness = parsed.RequireAppReadiness,
                EnableWin32WindowArrangement = parsed.EnableWin32WindowArrangement,
            };

            await using var host = BrokerHost.Create(options);
            using var shutdown = new CancellationTokenSource();
            Console.CancelKeyPress += (_, eventArgs) =>
            {
                eventArgs.Cancel = true;
                shutdown.Cancel();
            };
            using var sigterm = OperatingSystem.IsWindows()
                ? null
                : System.Runtime.InteropServices.PosixSignalRegistration.Create(
                    System.Runtime.InteropServices.PosixSignal.SIGTERM,
                    context =>
                    {
                        context.Cancel = true;
                        shutdown.Cancel();
                    });

            await host.StartAsync(shutdown.Token);
            if (!string.IsNullOrWhiteSpace(parsed.EndpointFile))
            {
                host.EndpointInfo.Write(parsed.EndpointFile);
            }

            var ready = new
            {
                transport = host.Endpoint.Transport,
                endpoint = host.Endpoint.Endpoint,
                operatorAppId = options.OperatorAppId,
                endpointFile = parsed.EndpointFile,
                auditFile = parsed.AuditFile,
                childLogDirectory = parsed.ChildLogDirectory,
                maxActionsPerPlan = options.MaxActionsPerPlan,
            };
            Console.Out.WriteLine("AETHER_BROKER_READY " + AetherJson.Serialize(ready));
            Console.Out.Flush();

            if (parsed.RunSeconds is not null)
            {
                shutdown.CancelAfter(TimeSpan.FromSeconds(parsed.RunSeconds.Value));
            }

            try
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, shutdown.Token);
            }
            catch (OperationCanceledException)
            {
            }

            Console.Out.WriteLine("AETHER_BROKER_STOPPING");
            await host.StopAsync();
            return 0;
        }
        catch (BrokerConfigurationException exception)
        {
            Console.Error.WriteLine($"broker.config.invalid ({exception.Code}): {exception.Message}");
            return 2;
        }
        catch (AetherContractException exception)
        {
            Console.Error.WriteLine($"broker.config.invalid ({exception.Code}): {exception.Message}");
            return 2;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine("broker.failed: " + exception);
            return 1;
        }
    }

    private static StringComparer ProcessPathComparer() =>
        OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
}
