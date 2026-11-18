using System.Globalization;
using System.Text;
using Aether.AI.Planner;
using Aether.AI.Providers;
using Aether.AI.Routing;
using Aether.AI.Summarization;
using Aether.Broker.Client;
using Aether.Contracts;
using Aether.Contracts.Broker;
using Aether.Contracts.Errors;
using Aether.Contracts.Json;
using Aether.Contracts.Models;
using Aether.Contracts.Models.Diagnostics;
using Aether.Execution.Authorization;
using Aether.Execution.Executor;
using Aether.Execution.Results;
using Aether.Execution.Validation;
using Aether.Security.Audit;
using Aether.Security.Classification;
using Aether.Security.Egress;
using Aether.Security.Policy;
using Aether.Security.Diagnostics;
using Aether.Security.Secrets;
using Aether.Workflow;

namespace Aether.Shell.Headless;

/// <summary>
/// Headless reference-workflow host. It registers as the operator app, builds
/// the policy-aware orchestrator, runs the deterministic reference workflow (or
/// a supplied untrusted plan for denial demonstrations) and emits a structured
/// result. This is the CI/E2E host twin of the WPF shell.
/// </summary>
public static class Program
{
    public static async Task<int> Main(string[] args)
    {
        ShellOptions options;
        try
        {
            options = ShellOptions.Parse(args);
        }
        catch (ArgumentException exception)
        {
            Console.Error.WriteLine(exception.Message);
            Console.Error.WriteLine(ShellOptions.HelpText);
            return 2;
        }

        if (options.Offline && options.Planner == "cloud")
        {
            Console.Error.WriteLine("shell.invalid_options: --offline cannot be combined with --planner cloud (offline forbids cloud routing).");
            return 2;
        }

        using var timeoutSource = new CancellationTokenSource(TimeSpan.FromSeconds(options.TimeoutSeconds));
        var cloudEnabled = options.CloudEnabled && !options.Offline;
        var cloudConsent = options.CloudConsent && !options.Offline;
        HashChainAuditLog? auditLog = null;
        try
        {
            var endpointInfo = BrokerEndpointInfo.Read(options.EndpointFile!);
            var envOverrides = options.EnvFile is null ? null : DotEnvFile.Load(options.EnvFile);
            var secretProvider = SecretProviderFactory.Create(envOverrides);
            using var channel = BrokerChannelFactory.CreateChannel(endpointInfo.Transport, endpointInfo.Endpoint);
            await using var client = new BrokerClient(channel);

            var registration = await client.RegisterAsync(
                endpointInfo.OperatorAppId,
                endpointInfo.OperatorToken,
                "shell-" + Environment.ProcessId.ToString(CultureInfo.InvariantCulture),
                timeoutSource.Token).ConfigureAwait(false);

            auditLog = options.AuditFile is null
                ? new HashChainAuditLog()
                : new HashChainAuditLog(
                    store: options.AuditMaxFileBytes > 0
                        ? new RotatingJsonlAuditStore(options.AuditFile, options.AuditMaxFileBytes, options.AuditMaxFiles)
                        : new JsonlAuditStore(options.AuditFile),
                    continueExistingChain: true);

            var policy = options.PolicyPath is null
                ? PolicyDocument.CreateDefault().EnsureValid()
                : PolicyLoader.LoadFromFile(options.PolicyPath);
            var policyEngine = new PolicyEngine(policy);
            var egressGateway = new EgressGateway(policyEngine, auditLog);
            var detector = new RuleBasedSensitiveEntityDetector(
                RuleBasedSensitiveEntityDetector.FromPolicy(policy.Patterns.AccountId));
            var classifier = new SourceFirstClassifier([detector]);

            var deterministicPlanner = new DeterministicCommandPlanner();
            var providerCatalog = ModelProviderCatalog.FromEnvironment(envOverrides);
            var plannerSettings = providerCatalog.Planner;
            var summarySettings = providerCatalog.Summarization;
            var cloudPlannerConfigured = !options.Offline && plannerSettings.IsCloud && plannerSettings.IsValid;
            var localPlannerConfigured = LoopbackLocalModelTransport.IsEndpointConfigured(envOverrides);
            var requestedRoute = ModelRouter.Decide(
                options.Classification,
                cloudEnabled,
                cloudConsent,
                cloudPlannerConfigured,
                localPlannerConfigured || (plannerSettings.ProviderId == "local" && plannerSettings.IsValid),
                plannerSettings);
            IPlanGenerator? modelPlanner = null;
            if (options.Planner == "cloud" && !options.Offline && plannerSettings.IsCloud)
            {
                // Constructing the adapter does not grant egress: a RESTRICTED/disabled-consent
                // request is denied by the gateway before the transport is invoked. Keeping the
                // adapter available preserves the explicit policy-denial evidence path.
                var transport = CreateCloudTransport(plannerSettings, secretProvider, envOverrides);
                modelPlanner = new CloudPlanProvider(
                    transport,
                    egressGateway,
                    new PlanProviderOptions(cloudEnabled, cloudConsent));
            }
            else if (options.Planner == "local" && !options.Offline && localPlannerConfigured)
            {
                modelPlanner = new LocalModelPlanProvider(
                    new LoopbackLocalModelTransport(
                        endpoint: GetOverride(envOverrides, LoopbackLocalModelTransport.DefaultEndpointEnvironmentVariable),
                        modelId: GetOverride(envOverrides, LoopbackLocalModelTransport.DefaultModelEnvironmentVariable)),
                    egressGateway);
            }
            else if (options.Planner == "cloud" && !options.Offline && requestedRoute.Kind == ModelRouteKind.LocalModel && localPlannerConfigured)
            {
                modelPlanner = new LocalModelPlanProvider(
                    new LoopbackLocalModelTransport(
                        endpoint: GetOverride(envOverrides, LoopbackLocalModelTransport.DefaultEndpointEnvironmentVariable),
                        modelId: GetOverride(envOverrides, LoopbackLocalModelTransport.DefaultModelEnvironmentVariable)),
                    egressGateway);
            }

            // The explicit --use-local-model switch keeps its original safety semantics: it selects
            // the local adapter even when configuration is missing, so the run fails with a typed
            // AiProviderMissing error instead of silently using the deterministic summary.
            var localSummary = options.UseLocalModel
                ? (ISummaryProvider)new LocalModelSummaryProvider(
                    new LoopbackLocalModelTransport(
                        endpoint: GetOverride(envOverrides, LoopbackLocalModelTransport.DefaultEndpointEnvironmentVariable),
                        modelId: GetOverride(envOverrides, LoopbackLocalModelTransport.DefaultModelEnvironmentVariable)),
                    egressGateway)
                : new LocalDeterministicSummaryProvider(detector);
            ISummaryProvider? cloudSummary = null;
            if (summarySettings.IsCloud
                && summarySettings.IsValid
                && !options.Offline
                && cloudEnabled
                && cloudConsent)
            {
                cloudSummary = new CloudSummaryProvider(
                    CreateCloudTransport(summarySettings, secretProvider, envOverrides),
                    egressGateway,
                    new PlanProviderOptions(cloudEnabled, cloudConsent));
            }

            var summaryService = new SummaryService(
                localSummary,
                cloudSummary,
                cloudSummary is null ? SummaryServiceOptions.LocalOnly : new SummaryServiceOptions(PreferCloudForPublic: true));

            var validator = new ActionPlanValidator(new ActionPlanValidationOptions { MaxActions = options.MaxActions });
            var authorizationPolicy = new CapabilityPlanAuthorizationPolicy();
            var workflowOptions = new WorkflowOrchestratorOptions
            {
                MaxActionsPerPlan = options.MaxActions,
                PlanningMode = options.Planner is "cloud" or "local"
                    ? PlanGenerationMode.CloudPreferred
                    : PlanGenerationMode.DeterministicPreferred,
                Execution = new ExecutionOptions
                {
                    MaxActionsPerPlan = options.MaxActions,
                    StepTimeout = TimeSpan.FromSeconds(20),
                    PlanTimeout = TimeSpan.FromSeconds(options.TimeoutSeconds - 5 > 5 ? options.TimeoutSeconds - 5 : 5),
                },
            };

            var broker = new BrokerClientWorkflowBroker(client);
            var orchestrator = new AetherWorkflowOrchestrator(
                classifier,
                deterministicPlanner,
                modelPlanner,
                broker,
                summaryService,
                validator,
                authorizationPolicy,
                workflowOptions,
                auditLog);

            ActionPlan? suppliedPlan = null;
            IReadOnlyList<PlanValidationIssue> suppliedPlanIssues = [];
            if (options.PlanFile is not null)
            {
                var planJson = await File.ReadAllTextAsync(options.PlanFile, timeoutSource.Token).ConfigureAwait(false);
                (suppliedPlan, suppliedPlanIssues) = ActionPlanParser.Parse(planJson);
                if (suppliedPlan is null)
                {
                    var invalidOutput = BuildParseFailureOutput(options, suppliedPlanIssues, envOverrides);
                    return Emit(invalidOutput, options);
                }
            }

            var request = new WorkflowRequest(
                options.Command ?? "",
                options.Classification,
                TraceId: TraceIdGenerator.NewTraceId(),
                SuppliedPlan: suppliedPlan);

            var result = await orchestrator.ExecuteAsync(request, timeoutSource.Token).ConfigureAwait(false);
            ShellDiagnosticsOutput? diagnostics = null;
            if (!string.IsNullOrWhiteSpace(options.ExportDiagnosticsPath))
            {
                diagnostics = await ExportDiagnosticsAsync(result, auditLog, client, options, cloudEnabled, cloudConsent, envOverrides);
            }

            var output = BuildOutput(result, egressGateway, auditLog, options, cloudEnabled, cloudConsent, envOverrides, diagnostics);
            return Emit(output, options);
        }
        catch (BrokerRpcException exception)
        {
            Console.Error.WriteLine($"shell.broker_error {exception.Code}: {exception.Message}");
            return 4;
        }
        catch (OperationCanceledException)
        {
            Console.Error.WriteLine("shell.cancelled: the workflow exceeded the configured timeout.");
            return 4;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine("shell.failed: " + exception.Message);
            return 4;
        }
    }

    private static ShellOutput BuildOutput(
        WorkflowResult result,
        EgressGateway egress,
        HashChainAuditLog auditLog,
        ShellOptions options,
        bool cloudEnabled,
        bool cloudConsent,
        IReadOnlyDictionary<string, string>? envOverrides,
        ShellDiagnosticsOutput? diagnostics = null)
    {
        var egressSnapshot = egress.Snapshot();
        var verification = auditLog.Verify();
        var (rotatedFiles, fileChainValid) = VerifyAuditFile(options);
        var summary = result.Summary is null
            ? null
            : new ShellSummaryOutput(
                result.Summary.Text ?? "",
                result.Summary.Provider,
                result.Summary.OutputClassification.ToWireName(),
                result.Summary.RedactionCount);

        return new ShellOutput(
            result.Success,
            result.Success ? "succeeded" : "denied",
            result.TraceId,
            result.PlanId,
            result.Classification.ToWireName(),
            result.ClassificationReasons,
            result.Execution?.Steps.Select(step => new ShellStepOutput(
                step.Action,
                step.State.ToString().ToLowerInvariant(),
                step.Code,
                step.Artifacts)).ToArray() ?? [],
            result.Execution?.ValidationIssues.Select(issue => $"{issue.Code}: {issue.Message}").ToArray() ?? [],
            summary,
            result.DenialCode,
            result.DenialMessage,
            result.DenialCategory.ToString(),
            result.PlannerProvider,
            result.UsedModel,
            result.DurationMs,
            new ShellEgressOutput(
                egressSnapshot.Attempts,
                egressSnapshot.ExternalAttempts,
                egressSnapshot.Denied,
                egressSnapshot.DenialsByCode),
            new ShellAuditOutput(
                verification.EntryCount,
                verification.Valid,
                options.AuditFile is null ? null : Path.GetFullPath(options.AuditFile),
                rotatedFiles,
                fileChainValid),
            BuildProviders(options, cloudEnabled, cloudConsent, envOverrides, result.Classification),
            BuildLimitations(),
            result.Execution?.Steps.Select((step, index) => new ShellTimelineEntry(
                index + 1,
                step.Action,
                step.State.ToString().ToLowerInvariant(),
                step.Code)).ToArray() ?? [],
            diagnostics,
            result.PlanningNotes);
    }

    private static async Task<ShellDiagnosticsOutput> ExportDiagnosticsAsync(
        WorkflowResult result,
        HashChainAuditLog auditLog,
        BrokerClient client,
        ShellOptions options,
        bool cloudEnabled,
        bool cloudConsent,
        IReadOnlyDictionary<string, string>? envOverrides)
    {
        var traceId = TraceIdGenerator.NewTraceId();
        var snapshot = await client.GetSnapshotAsync(traceId).ConfigureAwait(false);
        var health = await client.HealthProbeAsync().ConfigureAwait(false);
        var providers = BuildProviders(options, cloudEnabled, cloudConsent, envOverrides, result.Classification);
        var auditEntries = auditLog.Snapshot();
        var counts = auditEntries
            .GroupBy(entry => entry.EventType, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Count(), StringComparer.Ordinal);
        var verification = auditLog.Verify();
        var (rotatedFiles, fileChainValid) = VerifyAuditFile(options);
        var timeline = result.Execution?.Steps
            .Select((step, index) => new DiagnosticsTimelineEntry(index + 1, step.Action, step.State.ToString().ToLowerInvariant(), step.Code, null))
            .ToArray() ?? [];
        var apps = snapshot.Apps
            .Select(app => new DiagnosticsApp(
                app.AppId,
                health.AppHealth.FirstOrDefault(entry => entry.StartsWith(app.AppId + "=", StringComparison.Ordinal)) ?? "UNKNOWN",
                0))
            .ToArray();
        var bundle = DiagnosticsBundleBuilder.Build(
            new DiagnosticsRun(
                result.Success ? "succeeded" : "denied",
                result.TraceId,
                result.PlanId,
                result.Classification.ToWireName(),
                result.DenialCode,
                result.DurationMs,
                result.PlannerProvider,
                result.UsedModel),
            timeline,
            apps,
            new DiagnosticsAudit(verification.EntryCount, verification.Valid, rotatedFiles, fileChainValid, counts),
            new DiagnosticsProviders(
                providers.Mode,
                providers.RouteKind,
                providers.RouteReason,
                providers.ModelProvider,
                providers.ModelName,
                providers.CloudEnabled,
                providers.CloudConsentGranted,
                providers.CloudAdapterConfigured,
                providers.LocalModelAdapterConfigured),
            options.Offline,
            TimeProvider.System.GetUtcNow());
        var path = DiagnosticsBundleBuilder.WriteFile(bundle, options.ExportDiagnosticsPath!);
        return new ShellDiagnosticsOutput(path, new FileInfo(path).Length, true);
    }

    private static ShellOutput BuildParseFailureOutput(ShellOptions options, IReadOnlyList<PlanValidationIssue> issues, IReadOnlyDictionary<string, string>? envOverrides)
    {
        var first = issues.Count > 0
            ? issues[0]
            : new PlanValidationIssue(AetherErrorCodes.PlanInvalidJson, "The supplied plan could not be parsed.", null);
        var (rotatedFiles, fileChainValid) = VerifyAuditFile(options);
        return new ShellOutput(
            false,
            "denied",
            TraceIdGenerator.NewTraceId(),
            null,
            options.Classification.ToWireName(),
            [],
            [],
            issues.Select(issue => $"{issue.Code}: {issue.Message}").ToArray(),
            null,
            first.Code,
            first.Message,
            AetherErrorCodes.GetDenialCategory(first.Code).ToString(),
            "supplied-plan",
            false,
            0,
            new ShellEgressOutput(0, 0, 0, new Dictionary<string, long>()),
            new ShellAuditOutput(0, true, options.AuditFile is null ? null : Path.GetFullPath(options.AuditFile), rotatedFiles, fileChainValid),
            BuildProviders(options, options.CloudEnabled && !options.Offline, options.CloudConsent && !options.Offline, envOverrides, options.Classification),
            BuildLimitations());
    }

    private static string? GetOverride(IReadOnlyDictionary<string, string>? values, string key) =>
        values is not null && values.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value) ? value : null;

    private static ICloudModelTransport CreateCloudTransport(
        ModelProviderSettings settings,
        Aether.Security.Secrets.ISecretProvider secretProvider,
        IReadOnlyDictionary<string, string>? envOverrides) =>
        settings.ProviderId == "gemini"
            ? new GeminiCloudModelTransport(
                secretProvider,
                baseUrl: settings.ReadEndpoint(envOverrides),
                model: settings.Model,
                apiKeyEnvironmentVariable: settings.ApiKeyEnvironmentVariable)
            : new HttpCloudModelTransport(
                secretProvider,
                endpoint: settings.ReadEndpoint(envOverrides),
                modelId: settings.Model,
                apiKeyEnvironmentVariable: settings.ApiKeyEnvironmentVariable);

    private static ShellProvidersOutput BuildProviders(
        ShellOptions options,
        bool cloudEnabled,
        bool cloudConsent,
        IReadOnlyDictionary<string, string>? envOverrides,
        DataClassification classification)
    {
        var providerCatalog = ModelProviderCatalog.FromEnvironment(envOverrides);
        var providerSettings = providerCatalog.Planner;
        var summarySettings = providerCatalog.Summarization;
        var genericCloudConfigured = !options.Offline && HttpCloudModelTransport.IsEndpointConfigured(envOverrides);
        var cloudConfigured = !options.Offline && ((providerSettings.IsCloud && providerSettings.IsValid) || genericCloudConfigured);
        var localModelConfigured = LoopbackLocalModelTransport.IsEndpointConfigured(envOverrides)
            || (providerSettings.ProviderId == "local" && providerSettings.IsValid)
            || (summarySettings.ProviderId == "local" && summarySettings.IsValid);
        var route = ModelRouter.Decide(classification, cloudEnabled, cloudConsent, cloudConfigured, localModelConfigured, providerSettings);
        var mode = options.Offline
            ? "offline"
            : route.Kind switch
            {
                ModelRouteKind.Cloud => "cloud-enabled",
                ModelRouteKind.LocalModel => "local-model",
                _ => "local-private",
            };
        return new ShellProvidersOutput(
            mode,
            DeterministicPlanner: true,
            LocalDeterministicSummary: summarySettings.ProviderId != "local" || !options.UseLocalModel,
            CloudAdapterConfigured: cloudConfigured,
            LocalModelAdapterConfigured: localModelConfigured,
            CloudEnabled: cloudEnabled,
            CloudConsentGranted: cloudConsent,
            ModelProvider: providerSettings.ProviderId,
            ModelName: providerSettings.Model,
            RouteKind: route.Kind.ToString(),
            RouteReason: route.ReasonCode,
            CloudAttemptAllowed: route.CloudAttemptAllowed,
            PlannerProvider: providerSettings.ProviderId,
            SummaryProvider: summarySettings.ProviderId,
            ProviderConfigurationError: providerCatalog.ConfigurationError);
    }

    private static (int RotatedFiles, bool FileChainValid) VerifyAuditFile(ShellOptions options)
    {
        if (options.AuditFile is null)
        {
            return (0, true);
        }

        var path = Path.GetFullPath(options.AuditFile);
        var rotated = 0;
        for (var index = 1; index <= options.AuditMaxFiles; index++)
        {
            if (File.Exists(path + "." + index.ToString(CultureInfo.InvariantCulture)))
            {
                rotated++;
            }
        }

        var valid = options.AuditMaxFileBytes > 0
            ? RotatingJsonlAuditStore.VerifyFamily(path, options.AuditMaxFiles).Valid
            : AuditChainVerifier.VerifyFile(path).Valid;
        return (rotated, valid);
    }

    private static IReadOnlyList<string> BuildLimitations()
    {
        var limitations = new List<string>
        {
            "Cloud and local-model adapters are optional and disabled unless explicitly configured.",
            "Cloud routing is allowed only for PUBLIC data with cloud enabled and recorded consent; INTERNAL/RESTRICTED/UNKNOWN never attempt a cloud call.",
            "Win32 top-level arrangement and WPF shell rendering are exercised only on Windows.",
        };
        if (!OperatingSystem.IsWindows())
        {
            limitations.Add("This run used Unix domain sockets and headless sample processes (non-Windows CI substitute for the WPF samples).");
        }

        return limitations;
    }

    private static int Emit(ShellOutput output, ShellOptions options)
    {
        if (options.OutputFile is not null)
        {
            var path = Path.GetFullPath(options.OutputFile);
            var directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            File.WriteAllText(path, AetherJson.SerializeIndented(output));
        }

        if (options.TextOutput)
        {
            Console.WriteLine($"trace: {output.TraceId}");
            Console.WriteLine($"classification: {output.Classification} ({string.Join(", ", output.ClassificationReasons)})");
            Console.WriteLine($"plan: {output.PlanId ?? "n/a"} planner={output.Planner} usedModel={output.UsedModel}");
            foreach (var step in output.Steps)
            {
                Console.WriteLine($"  [{step.State}] {step.Action}{(step.Code is null ? "" : " " + step.Code)}");
            }

            if (output.Summary is not null)
            {
                Console.WriteLine();
                Console.WriteLine(output.Summary.Text);
                Console.WriteLine($"(provider: {output.Summary.Provider}; classification: {output.Summary.Classification})");
            }

            if (!output.Success)
            {
                Console.WriteLine();
                Console.WriteLine($"denied: {output.DenialCode} ({output.DenialCategory})");
                Console.WriteLine($"  {output.DenialMessage}");
            }

            Console.WriteLine();
            Console.WriteLine($"egress: attempts={output.Egress.Attempts} external={output.Egress.ExternalAttempts} denied={output.Egress.Denied}");
            Console.WriteLine($"audit: entries={output.Audit.EntryCount} chainValid={output.Audit.ChainValid}" +
                              (output.Audit.AuditFile is null ? "" : $" file={output.Audit.AuditFile}"));
        }
        else
        {
            Console.Out.WriteLine(AetherJson.SerializeIndented(output));
        }

        Console.Out.Flush();
        return output.Success ? 0 : 3;
    }
}
