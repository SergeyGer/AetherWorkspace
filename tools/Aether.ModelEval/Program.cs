using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using Aether.AI.Planner;
using Aether.AI.Providers;
using Aether.Contracts;
using Aether.Contracts.Json;
using Aether.Security.Policy;
using Aether.Security.Secrets;

namespace Aether.ModelEval;

/// <summary>
/// v0.4 bounded model evaluation harness (synthetic PUBLIC data only).
/// Loads provider credentials exclusively from an explicit local `.env` file,
/// records latency/usage estimates/plan validity per command, and writes a
/// sanitized JSON report. Never prints or persists API keys.
/// </summary>
internal static class Program
{
    private sealed record CorpusItem(string Id, string Category, string Command);
    private sealed record EvalResult(string Id, string Category, bool Success, string Provider, string Model,
        double? LatencyMs, int EstimatedInputTokens, int EstimatedOutputTokens, decimal EstimatedCostUsd,
        bool ValidPlan, int Steps, string? ErrorCode, string? ContentPreview);

    private static int Main(string[] args)
    {
        string? envFile = null, provider = null, corpusPath = null, outPath = null, modelOverride = null;
        for (var index = 0; index < args.Length; index++)
        {
            switch (args[index])
            {
                case "--env-file": envFile = args[++index]; break;
                case "--provider": provider = args[++index]; break;
                case "--corpus": corpusPath = args[++index]; break;
                case "--out": outPath = args[++index]; break;
                case "--model": modelOverride = args[++index]; break;
                default:
                    Console.Error.WriteLine($"unknown argument: {args[index]}");
                    return 2;
            }
        }

        if (envFile is null || provider is null || corpusPath is null || outPath is null)
        {
            Console.Error.WriteLine("usage: Aether.ModelEval --env-file <path> --provider openai|gemini|local --corpus <json> --out <json> [--model <id>]");
            return 2;
        }

        IReadOnlyDictionary<string, string> values;
        try
        {
            values = DotEnvFile.Load(envFile);
        }
        catch (Exception exception) when (exception is IOException or InvalidOperationException or Aether.Contracts.Errors.AetherContractException)
        {
            Console.Error.WriteLine($"cannot load env file: {exception.GetType().Name}");
            return 2;
        }

        var withProvider = new Dictionary<string, string>(values, StringComparer.Ordinal)
        {
            [ModelProviderSettings.DefaultProviderEnvironmentVariable] = provider,
        };
        var settings = ModelProviderSettings.FromEnvironment(withProvider);
        if (modelOverride is not null)
        {
            settings = settings with { Model = modelOverride };
        }

        if (!settings.IsConfigured)
        {
            Console.Error.WriteLine($"provider '{settings.ProviderId}' is not fully configured in the supplied env file (endpoint/key missing).");
            return 3;
        }

        var secretProvider = new EnvironmentSecretProvider(withProvider);
        ICloudModelTransport transport = settings.ProviderId == "gemini"
            ? new GeminiCloudModelTransport(secretProvider, settings.ReadEndpoint(withProvider), settings.Model, settings.ApiKeyEnvironmentVariable)
            : new HttpCloudModelTransport(secretProvider, settings.ReadEndpoint(withProvider), settings.Model, settings.ApiKeyEnvironmentVariable);

        var corpus = JsonSerializer.Deserialize<CorpusDocument>(File.ReadAllText(corpusPath), new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
                     ?? throw new InvalidOperationException("invalid corpus");
        var results = new List<EvalResult>();
        try
        {
        foreach (var item in corpus.Items.Take(10))
        {
            var request = new CloudModelRequest(
                "eval-" + item.Id,
                AiTaskKind.PlanGeneration,
                DataClassification.Public,
                "command",
                System.Text.Encoding.UTF8.GetByteCount(item.Command),
                CloudPlanProvider.PlannerPrompt,
                item.Command);
            var stopwatch = Stopwatch.StartNew();
            CloudModelResponse response;
            try
            {
                response = transport.SendAsync(request, CancellationToken.None).GetAwaiter().GetResult();
            }
            catch (Exception exception)
            {
                response = CloudModelResponse.Fail($"eval.{exception.GetType().Name.ToLowerInvariant()}");
            }

            stopwatch.Stop();
            var inputTokens = Math.Max(1, (item.Command.Length + CloudPlanProvider.PlannerPrompt.Length) / 4);
            var outputTokens = string.IsNullOrEmpty(response.Content) ? 0 : Math.Max(1, response.Content.Length / 4);
            var (validPlan, steps) = ValidatePlan(response.Content);
            results.Add(new EvalResult(item.Id, item.Category, response.Success, transport.ProviderId, settings.Model,
                Math.Round(stopwatch.Elapsed.TotalMilliseconds, 1), inputTokens, outputTokens,
                EstimateCost(settings.Model, inputTokens, outputTokens), validPlan, steps, response.ErrorCode,
                response.Content is null ? null : response.Content[..Math.Min(300, response.Content.Length)]));
            Console.WriteLine($"{item.Id,-24} {(response.Success ? "ok " : "ERR")} {stopwatch.Elapsed.TotalMilliseconds,7:F0} ms  validPlan={validPlan} steps={steps} {(response.ErrorCode ?? "")}");
        }

        }
        finally
        {
            (transport as IDisposable)?.Dispose();
        }

        var report = new
        {
            generatedAt = DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture),
            purpose = "v0.4 bounded model evaluation (synthetic PUBLIC commands only)",
            provider = transport.ProviderId,
            model = settings.Model,
            corpusPath,
            items = results.Select(r => new
            {
                r.Id, r.Category, r.Success, r.Provider, r.Model, r.LatencyMs,
                r.EstimatedInputTokens, r.EstimatedOutputTokens, r.EstimatedCostUsd, r.ValidPlan, r.Steps, r.ErrorCode, r.ContentPreview,
            }),
            summary = new
            {
                total = results.Count,
                succeeded = results.Count(r => r.Success),
                validPlans = results.Count(r => r.ValidPlan),
                avgLatencyMs = results.Where(r => r.LatencyMs is not null).Select(r => r.LatencyMs!.Value).DefaultIfEmpty(0).Average(),
                estimatedCostUsd = results.Sum(r => r.EstimatedCostUsd),
            },
            notes = "Cost/token counts are character-based estimates, not provider billing. No API keys or raw secrets are recorded.",
        };
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(outPath))!);
        File.WriteAllText(outPath, JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }) + "\n");
        Console.WriteLine($"report: {outPath}");
        return results.All(r => r.Success) ? 0 : 1;
    }

    private static (bool Valid, int Steps) ValidatePlan(string? content)
    {
        if (string.IsNullOrWhiteSpace(content))
        {
            return (false, 0);
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

        var allowed = new HashSet<string>(StringComparer.Ordinal)
        {
            "LaunchApp", "PublishContext", "ArrangeWindows", "SummarizeContext", "ShowStatus",
        };
        try
        {
            using var document = JsonDocument.Parse(trimmed);
            if (document.RootElement.TryGetProperty("steps", out var steps) && steps.ValueKind == JsonValueKind.Array)
            {
                var length = steps.GetArrayLength();
                var actions = 0;
                foreach (var step in steps.EnumerateArray())
                {
                    if (step.TryGetProperty("action", out var action) && action.ValueKind == JsonValueKind.String &&
                        allowed.Contains(action.GetString() ?? ""))
                    {
                        actions++;
                    }
                }

                return (actions == length && length is >= 1 and <= 6, length);
            }
        }
        catch (JsonException)
        {
        }

        return (false, 0);
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

    private sealed record CorpusDocument(string SchemaVersion, string Description, CorpusItem[] Items);
}
