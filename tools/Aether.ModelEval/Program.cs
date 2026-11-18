using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Aether.AI.Planner;
using Aether.AI.Providers;
using Aether.Contracts;
using Aether.Contracts.Json;
using Aether.Contracts.Models;
using Aether.Security.Policy;
using Aether.Security.Secrets;

namespace Aether.ModelEval;

/// <summary>
/// v1.3 model evaluation harness (AW-060). Runs a versioned, labelled synthetic corpus through one
/// deterministic or model provider, compares plan structure with canonical gold plans, checks
/// denial behaviour for adversarial negatives, repeats trials and writes a sanitized report.
///
/// Provider credentials are loaded only from a local .env file/environment; the report never
/// includes key values or raw content beyond a short synthetic preview. Cost/token values are
/// explicitly labelled estimates. TTFT is reported only when a transport exposes streaming; the
/// current non-streaming adapters report it as a limitation, never as a fabricated number.
/// </summary>
internal static class Program
{
    private static readonly HashSet<string> AllowedActions = new(StringComparer.Ordinal)
    {
        ActionKinds.LaunchApp, ActionKinds.PublishContext, ActionKinds.ArrangeWindows,
        ActionKinds.SummarizeContext, ActionKinds.ShowStatus,
    };

    private sealed record CorpusDocument(
        string SchemaVersion,
        string Description,
        Dictionary<string, CanonicalPlan>? CanonicalPlans,
        CorpusItem[] Items);

    private sealed record CanonicalPlan(string Description, CanonicalStep[] Steps);

    private sealed record CanonicalStep(string Action, string? AppId = null, string? Layout = null, string? Mode = null);

    private sealed record CorpusItem(
        string Id,
        string Category,
        string Command,
        string? Expectation,
        string? CanonicalPlan,
        string? Reason);

    private sealed record Attempt(
        bool Success,
        string? Content,
        string? ErrorCode,
        string Provider,
        bool UsedModel,
        double LatencyMs,
        int InputTokens,
        int OutputTokens,
        decimal EstimatedCostUsd,
        double? TtftMs);

    private sealed record ItemResult(
        string Id,
        string Category,
        string Expectation,
        bool Success,
        bool ValidPlan,
        bool SemanticCorrect,
        string Provider,
        bool UsedModel,
        double LatencyMs,
        int InputTokens,
        int OutputTokens,
        decimal EstimatedCostUsd,
        double? TtftMs,
        string? ErrorCode,
        string? ContentPreview);

    private sealed record RunConfiguration(
        string Provider,
        string Model,
        string EndpointHost,
        bool UsedModel,
        string CredentialSource,
        IPlanGenerator? DeterministicPlanner,
        ICloudModelTransport? CloudTransport,
        ILocalModelTransport? LocalTransport);

    private static int Main(string[] args)
    {
        string? envFile = null, provider = null, corpusPath = null, outPath = null, modelOverride = null;
        var trials = 1;
        for (var index = 0; index < args.Length; index++)
        {
            switch (args[index])
            {
                case "--env-file": envFile = Next(args, ref index); break;
                case "--provider": provider = Next(args, ref index); break;
                case "--corpus": corpusPath = Next(args, ref index); break;
                case "--out": outPath = Next(args, ref index); break;
                case "--model": modelOverride = Next(args, ref index); break;
                case "--trials":
                    if (!int.TryParse(Next(args, ref index), NumberStyles.Integer, CultureInfo.InvariantCulture, out trials) || trials is < 1 or > 5)
                    {
                        Console.Error.WriteLine("--trials must be an integer from 1 to 5.");
                        return 2;
                    }

                    break;
                default:
                    Console.Error.WriteLine($"unknown argument: {args[index]}");
                    return 2;
            }
        }

        if (provider is null || corpusPath is null || outPath is null)
        {
            Console.Error.WriteLine("usage: Aether.ModelEval --provider deterministic|local|openai|gemini --corpus <json> --out <json> [--trials 1..5] [--env-file <path>] [--model <id>]");
            return 2;
        }

        if (!File.Exists(corpusPath))
        {
            Console.Error.WriteLine($"corpus not found: {corpusPath}");
            return 2;
        }

        IReadOnlyDictionary<string, string> values = envFile is null
            ? new Dictionary<string, string>(StringComparer.Ordinal)
            : LoadEnv(envFile);
        var withProvider = new Dictionary<string, string>(values, StringComparer.Ordinal)
        {
            [ModelProviderSettings.DefaultProviderEnvironmentVariable] = provider,
            [ModelProviderSettings.PlannerProviderEnvironmentVariable] = provider,
        };
        var settings = ModelProviderSettings.FromEnvironment(withProvider, ModelCapability.Planning);
        if (modelOverride is not null)
        {
            settings = settings with { Model = modelOverride };
        }

        RunConfiguration configuration;
        try
        {
            configuration = BuildConfiguration(provider, settings, withProvider);
        }
        catch (InvalidOperationException exception)
        {
            Console.Error.WriteLine(exception.Message);
            return 3;
        }

        CorpusDocument corpus;
        try
        {
            corpus = JsonSerializer.Deserialize<CorpusDocument>(File.ReadAllText(corpusPath), new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
                      ?? throw new InvalidOperationException("corpus is empty");
        }
        catch (Exception exception) when (exception is JsonException or InvalidOperationException)
        {
            Console.Error.WriteLine("invalid corpus: " + exception.Message);
            return 2;
        }

        if (corpus.Items.Length == 0 || corpus.CanonicalPlans is null)
        {
            Console.Error.WriteLine("the corpus must contain items and canonicalPlans.");
            return 2;
        }

        var results = new List<ItemResult>();
        var failures = 0;
        try
        {
            foreach (var item in corpus.Items)
            {
                for (var trial = 0; trial < trials; trial++)
                {
                    var expected = item.Expectation ?? "plan";
                    var attempt = Execute(configuration, item.Command, out var plan);
                    var isPlanExpectation = string.Equals(expected, "plan", StringComparison.Ordinal);
                    var canonical = isPlanExpectation && item.CanonicalPlan is not null
                        ? corpus.CanonicalPlans.GetValueOrDefault(item.CanonicalPlan)
                        : null;
                    ActionPlan? parsedPlan = null;
                    var validPlan = attempt.Success && TryParsePlan(attempt.Content, out parsedPlan, out _);
                    var semanticCorrect = isPlanExpectation
                        ? validPlan && canonical is not null && StepsMatch(parsedPlan!, canonical)
                        : !validPlan;
                    if (!attempt.Success && isPlanExpectation)
                    {
                        failures++;
                    }

                    results.Add(new ItemResult(
                        item.Id,
                        item.Category,
                        expected,
                        attempt.Success,
                        validPlan,
                        semanticCorrect,
                        attempt.Provider,
                        attempt.UsedModel,
                        Math.Round(attempt.LatencyMs, 1),
                        attempt.InputTokens,
                        attempt.OutputTokens,
                        attempt.EstimatedCostUsd,
                        attempt.TtftMs,
                        attempt.ErrorCode,
                        attempt.Content is null ? null : attempt.Content[..Math.Min(240, attempt.Content.Length)]));
                }
            }
        }
        finally
        {
            (configuration.CloudTransport as IDisposable)?.Dispose();
            (configuration.LocalTransport as IDisposable)?.Dispose();
        }

        var planResults = results.Where(result => result.Expectation == "plan").ToArray();
        var negativeResults = results.Where(result => result.Expectation != "plan").ToArray();
        var latencies = results.Where(result => result.LatencyMs > 0).Select(result => result.LatencyMs).ToArray();
        var ttftValues = results.Where(result => result.TtftMs is not null).Select(result => result.TtftMs!.Value).ToArray();
        Array.Sort(ttftValues);
        var inputTokenValues = results.Select(result => (double)result.InputTokens).ToArray();
        var outputTokenValues = results.Select(result => (double)result.OutputTokens).ToArray();
        Array.Sort(inputTokenValues);
        Array.Sort(outputTokenValues);
        var hardware = BuildHardwareProfile();
        var corpusSha256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(corpusPath))).ToLowerInvariant();
        var providerEndpointHost = SanitizeEndpointHost(configuration.EndpointHost);
        var totalEstimatedCost = results.Sum(result => result.EstimatedCostUsd);
        var report = new
        {
            schemaVersion = "1.1",
            kind = "model-evaluation",
            generatedAtUtc = DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture),
            purpose = "v1.3 labelled synthetic gold-corpus evaluation; no raw customer data and no quality/cost claim beyond this run",
            provider = new
            {
                id = configuration.Provider,
                model = configuration.Model,
                endpointHost = providerEndpointHost,
                usedModel = configuration.UsedModel,
                credentialSource = configuration.CredentialSource,
            },
            corpus = new
            {
                path = Path.GetFullPath(corpusPath),
                sha256 = corpusSha256,
                itemCount = corpus.Items.Length,
                planItems = corpus.Items.Count(item => (item.Expectation ?? "plan") == "plan"),
                negativeItems = corpus.Items.Count(item => (item.Expectation ?? "plan") != "plan"),
                passedForProduction = false,
            },
            hardware,
            metrics = new
            {
                trials,
                requests = results.Count,
                providerFailures = failures,
                semanticPlanAccuracy = planResults.Length == 0 ? (double?)null : Math.Round(planResults.Count(result => result.SemanticCorrect) / (double)planResults.Length, 4),
                denialAccuracy = negativeResults.Length == 0 ? (double?)null : Math.Round(negativeResults.Count(result => result.SemanticCorrect) / (double)negativeResults.Length, 4),
                syntacticPlanRate = results.Count == 0 ? 0 : Math.Round(results.Count(result => result.ValidPlan) / (double)results.Count, 4),
                latencyMs = Percentiles(latencies),
                ttftMs = new
                {
                    p50 = ttftValues.Length == 0 ? (double?)null : Math.Round(Percentile(ttftValues, 0.50), 1),
                    p95 = ttftValues.Length == 0 ? (double?)null : Math.Round(Percentile(ttftValues, 0.95), 1),
                    p99 = ttftValues.Length == 0 ? (double?)null : Math.Round(Percentile(ttftValues, 0.99), 1),
                    measuredRequests = ttftValues.Length,
                    limitation = ttftValues.Length == 0
                        ? "This provider/transport is non-streaming, so true time-to-first-token is not measured; no value is fabricated."
                        : "Measured from the first non-empty SSE content delta for the OpenAI-compatible streaming transport. Gemini/local adapters remain non-streaming and are excluded.",
                },
                tokens = new
                {
                    kind = "estimated-character-based",
                    estimatedInput = results.Sum(result => result.InputTokens),
                    estimatedOutput = results.Sum(result => result.OutputTokens),
                    inputPerRequest = Percentiles(inputTokenValues),
                    outputPerRequest = Percentiles(outputTokenValues),
                },
                cost = new
                {
                    kind = "estimated",
                    estimatedTotalUsd = Math.Round(totalEstimatedCost, 8),
                    providerBilledUsd = (decimal?)null,
                    note = "Estimated from the configured token price table; not a provider invoice. Actual cost requires the provider usage/billing export.",
                },
            },
            results,
            limitations = new[]
            {
                "Character-based token/cost estimates are not provider billing.",
                "TTFT is reported only where the provider transport streams tokens; non-streaming adapters report it as unavailable instead of estimating.",
                "The corpus is synthetic and bounded; no general, compliance or production-quality claim is made.",
                "Local hardware latency/memory is only authoritative when the operator attaches the physical hardware report.",
            },
        };

        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(outPath))!);
        File.WriteAllText(outPath, JsonSerializer.Serialize(report, AetherJson.Indented) + "\n");
        Console.WriteLine(
            $"report: {outPath} requests={results.Count} planAccuracy={report.metrics.semanticPlanAccuracy} "
            + $"denialAccuracy={report.metrics.denialAccuracy} providerFailures={failures}");
        return failures == 0 ? 0 : 1;
    }

    private static RunConfiguration BuildConfiguration(
        string provider,
        ModelProviderSettings settings,
        IReadOnlyDictionary<string, string> values)
    {
        switch (provider)
        {
            case "deterministic":
                return new RunConfiguration(
                    "deterministic-grammar",
                    "deterministic-grammar",
                    "",
                    UsedModel: false,
                    "none",
                    DeterministicPlanner: new DeterministicCommandPlanner(),
                    CloudTransport: null,
                    LocalTransport: null);
            case "local":
                if (!settings.IsValid || settings.ProviderId != "local")
                {
                    throw new InvalidOperationException(
                        "local provider is not configured or invalid: " + (settings.ConfigurationError ?? "set AETHER_LOCAL_MODEL_ENDPOINT"));
                }

                return new RunConfiguration(
                    "local-loopback",
                    settings.Model,
                    settings.ReadEndpoint(values) ?? "",
                    UsedModel: true,
                    "environment",
                    DeterministicPlanner: null,
                    CloudTransport: null,
                    LocalTransport: new LoopbackLocalModelTransport(settings.ReadEndpoint(values), settings.Model));
            case "openai" or "gemini":
                if (!settings.IsValid || !settings.IsCloud)
                {
                    throw new InvalidOperationException(
                        $"provider '{settings.ProviderId}' is not fully configured or invalid: {settings.ConfigurationError ?? "endpoint/key missing"}");
                }

                ICloudModelTransport transport = settings.ProviderId == "gemini"
                    ? new GeminiCloudModelTransport(
                        new EnvironmentSecretProvider(values),
                        settings.ReadEndpoint(values),
                        settings.Model,
                        settings.ApiKeyEnvironmentVariable)
                    : new StreamingOpenAiTransport(
                        new EnvironmentSecretProvider(values),
                        settings.ReadEndpoint(values),
                        settings.Model,
                        settings.ApiKeyEnvironmentVariable);
                return new RunConfiguration(
                    transport.ProviderId,
                    settings.Model,
                    settings.ReadEndpoint(values) ?? "",
                    UsedModel: true,
                    "environment",
                    DeterministicPlanner: null,
                    CloudTransport: transport,
                    LocalTransport: null);
            default:
                throw new InvalidOperationException($"unsupported provider '{provider}' (expected deterministic, local, openai or gemini).");
        }
    }

    private static Attempt Execute(RunConfiguration configuration, string command, out ActionPlan? plan)
    {
        plan = null;
        var request = new PlanningRequest(command, DataClassification.Public, "eval-" + Guid.NewGuid().ToString("n"));
        var stopwatch = Stopwatch.StartNew();
        if (configuration.DeterministicPlanner is not null)
        {
            var result = configuration.DeterministicPlanner.GeneratePlanAsync(request, CancellationToken.None).GetAwaiter().GetResult();
            stopwatch.Stop();
            plan = result.Plan;
            var content = result.Success && result.Plan is not null ? AetherJson.Serialize(result.Plan) : null;
            return new Attempt(
                result.Success, content, result.ErrorCode, result.Provider, result.UsedModel,
                stopwatch.Elapsed.TotalMilliseconds, 0, 0, 0m, null);
        }

        var modelRequest = new CloudModelRequest(
            request.TraceId,
            AiTaskKind.PlanGeneration,
            DataClassification.Public,
            "command",
            Encoding.UTF8.GetByteCount(command),
            CloudPlanProvider.PlannerPrompt,
            command);
        CloudModelResponse response;
        double? ttftMs = null;
        try
        {
            if (configuration.CloudTransport is StreamingOpenAiTransport streamingTransport)
            {
                var streamingResult = streamingTransport.SendStreamingAsync(modelRequest, CancellationToken.None).GetAwaiter().GetResult();
                response = streamingResult.Response;
                ttftMs = streamingResult.TimeToFirstTokenMs;
            }
            else
            {
                response = configuration.CloudTransport is not null
                    ? configuration.CloudTransport.SendAsync(modelRequest, CancellationToken.None).GetAwaiter().GetResult()
                    : configuration.LocalTransport!.SendAsync(modelRequest, CancellationToken.None).GetAwaiter().GetResult();
            }
        }
        catch (Exception exception)
        {
            response = CloudModelResponse.Fail("eval." + exception.GetType().Name.ToLowerInvariant());
        }

        stopwatch.Stop();
        var inputTokens = Math.Max(1, (command.Length + CloudPlanProvider.PlannerPrompt.Length) / 4);
        var outputTokens = string.IsNullOrEmpty(response.Content) ? 0 : Math.Max(1, response.Content.Length / 4);
        var providerId = configuration.CloudTransport?.ProviderId ?? configuration.LocalTransport!.ProviderId;
        return new Attempt(
            response.Success,
            response.Content,
            response.ErrorCode,
            providerId,
            UsedModel: true,
            stopwatch.Elapsed.TotalMilliseconds,
            inputTokens,
            outputTokens,
            EstimateCost(configuration.Model, inputTokens, outputTokens),
            ttftMs is null ? null : Math.Round(ttftMs.Value, 2));
    }

    private static bool TryParsePlan(string? content, out ActionPlan? plan, out string? error)
    {
        plan = null;
        error = null;
        if (string.IsNullOrWhiteSpace(content))
        {
            error = "empty response";
            return false;
        }

        var trimmed = content.Trim();
        if (trimmed.StartsWith("```", StringComparison.Ordinal))
        {
            var firstNewline = trimmed.IndexOf('\n');
            var lastFence = trimmed.LastIndexOf("```", StringComparison.Ordinal);
            if (firstNewline > 0 && lastFence > firstNewline)
            {
                trimmed = trimmed[(firstNewline + 1)..lastFence].Trim();
            }
        }

        var start = trimmed.IndexOf('{');
        var end = trimmed.LastIndexOf('}');
        if (start < 0 || end <= start)
        {
            error = "no JSON object";
            return false;
        }

        try
        {
            plan = AetherJson.DeserializeStrict<ActionPlan>(trimmed[start..(end + 1)]);
            if (plan.Steps.Count is < 1 or > 6 || plan.Steps.Any(step => !AllowedActions.Contains(step.Action)))
            {
                plan = null;
                error = "plan shape is outside the bounded action set";
                return false;
            }

            return true;
        }
        catch (Exception)
        {
            error = "invalid strict plan JSON";
            return false;
        }
    }

    private static bool StepsMatch(ActionPlan actual, CanonicalPlan canonical)
    {
        if (actual.Steps.Count != canonical.Steps.Length)
        {
            return false;
        }

        for (var index = 0; index < canonical.Steps.Length; index++)
        {
            var expected = canonical.Steps[index];
            var step = actual.Steps[index];
            if (!string.Equals(step.Action, expected.Action, StringComparison.Ordinal)
                || (expected.AppId is not null && !string.Equals(step.AppId, expected.AppId, StringComparison.Ordinal))
                || (expected.Layout is not null && !string.Equals(step.Layout, expected.Layout, StringComparison.Ordinal))
                || (expected.Mode is not null && !string.Equals(step.Mode, expected.Mode, StringComparison.Ordinal)))
            {
                return false;
            }
        }

        return true;
    }

    private static object Percentiles(double[] values)
    {
        if (values.Length == 0)
        {
            return new { p50 = (double?)null, p95 = (double?)null, p99 = (double?)null };
        }

        Array.Sort(values);
        return new
        {
            p50 = Math.Round(Percentile(values, 0.50), 1),
            p95 = Math.Round(Percentile(values, 0.95), 1),
            p99 = Math.Round(Percentile(values, 0.99), 1),
        };
    }

    private static double Percentile(double[] sorted, double percentile)
    {
        var rank = (int)Math.Ceiling(percentile * sorted.Length) - 1;
        return sorted[Math.Clamp(rank, 0, sorted.Length - 1)];
    }

    private static object BuildHardwareProfile()
    {
        var memory = GC.GetGCMemoryInfo();
        return new
        {
            os = RuntimeInformation.OSDescription,
            architecture = RuntimeInformation.ProcessArchitecture.ToString(),
            logicalCpus = Environment.ProcessorCount,
            availableMemoryMb = Math.Round(memory.TotalAvailableMemoryBytes / (1024.0 * 1024.0), 1),
            runtime = RuntimeInformation.FrameworkDescription,
            profile = Environment.GetEnvironmentVariable("AETHER_EVAL_HARDWARE_PROFILE") ?? "unspecified/portable-host",
            gpu = Environment.GetEnvironmentVariable("AETHER_EVAL_GPU_PROFILE") ?? "not-detected-by-harness",
        };
    }

    private static string SanitizeEndpointHost(string endpoint)
    {
        if (string.IsNullOrWhiteSpace(endpoint) || !Uri.TryCreate(endpoint, UriKind.Absolute, out var uri))
        {
            return "";
        }

        return uri.IsDefaultPort ? uri.Host : $"{uri.Host}:{uri.Port}";
    }

    private static decimal EstimateCost(string model, int inputTokens, int outputTokens)
    {
        var (inputPerMillion, outputPerMillion) = model switch
        {
            "gpt-4o-mini" => (0.15m, 0.60m),
            "gemini-3.1-flash-lite" => (0.25m, 1.50m),
            "gemini-2.5-flash-lite" => (0.10m, 0.40m),
            _ => (0.50m, 2.00m),
        };
        return Math.Round((inputTokens * inputPerMillion + outputTokens * outputPerMillion) / 1_000_000m, 8);
    }

    private static IReadOnlyDictionary<string, string> LoadEnv(string path)
    {
        try
        {
            return DotEnvFile.Load(path);
        }
        catch (Exception exception)
        {
            throw new InvalidOperationException($"cannot load env file: {exception.GetType().Name}");
        }
    }

    private static string Next(string[] args, ref int index) =>
        index + 1 < args.Length ? args[++index] : throw new ArgumentException("missing option value");
}
