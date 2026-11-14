using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Aether.Contracts;
using Aether.Contracts.Errors;
using Aether.Contracts.Models;

namespace Aether.AI.Planner;

public sealed record PlanningRequest(string Command, DataClassification Classification, string TraceId);

public sealed record PlanGenerationResult(
    bool Success,
    ActionPlan? Plan,
    string Provider,
    bool UsedModel,
    string? ErrorCode,
    string? Message)
{
    public static PlanGenerationResult Ok(ActionPlan plan, string provider, bool usedModel) =>
        new(true, plan, provider, usedModel, null, null);

    public static PlanGenerationResult Fail(string errorCode, string message, string provider) =>
        new(false, null, provider, false, errorCode, message);
}

/// <summary>Untrusted plan proposal source. Output always passes through validation.</summary>
public interface IPlanGenerator
{
    string ProviderId { get; }

    Task<PlanGenerationResult> GeneratePlanAsync(PlanningRequest request, CancellationToken cancellationToken = default);
}

/// <summary>
/// Deterministic grammar parser for the reference workflow command. This is the
/// primary v0.1 planning path: no model call is needed and no external egress can
/// occur. Unknown commands are rejected instead of being guessed.
/// </summary>
public sealed class DeterministicCommandPlanner : IPlanGenerator
{
    public const string ProviderName = "deterministic-grammar";

    public string ProviderId => ProviderName;

    public Task<PlanGenerationResult> GeneratePlanAsync(PlanningRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        var command = request.Command?.Trim() ?? "";
        if (command.Length < 4)
        {
            return Task.FromResult(PlanGenerationResult.Fail(
                AetherErrorCodes.AiCommandUnsupported,
                "The command is too short to be recognized. Provide a supported workspace command.",
                ProviderName));
        }

        var normalized = command.ToLowerInvariant();
        var wantsWatchlist = ContainsAny(normalized, "watchlist", "bond analytics", "analytics", "open ");
        var wantsRisk = ContainsAny(normalized, "risk");
        var wantsSynchronization = ContainsAny(normalized, "synchron", "sync ", "across the workspace", "instrument");
        var wantsSummary = ContainsAny(normalized, "summar", "exposure", "privately");

        if (!(wantsWatchlist && wantsRisk && wantsSynchronization && wantsSummary))
        {
            return Task.FromResult(PlanGenerationResult.Fail(
                AetherErrorCodes.AiCommandUnsupported,
                "The command is not one of the supported deterministic workflows. No model was called and nothing was executed.",
                ProviderName));
        }

        var plan = new ActionPlan
        {
            SchemaVersion = AetherVersions.ContractSchemaVersion,
            PlanId = BuildPlanId(command),
            Description = "Deterministic reference workflow plan (synthetic data only).",
            Steps =
            [
                new ActionPlanStep { Action = ActionKinds.LaunchApp, AppId = "sample.watchlist" },
                new ActionPlanStep { Action = ActionKinds.LaunchApp, AppId = "sample.risk" },
                new ActionPlanStep { Action = ActionKinds.LaunchApp, AppId = "sample.chart" },
                new ActionPlanStep { Action = ActionKinds.PublishContext, ContextRef = "synthetic.instrument.northstar" },
                new ActionPlanStep { Action = ActionKinds.ArrangeWindows, Layout = Layouts.SideBySide },
                new ActionPlanStep
                {
                    Action = ActionKinds.SummarizeContext,
                    ContextRef = "synthetic.exposure.northstar",
                    Mode = SummarizeModes.Private,
                },
            ],
        };

        return Task.FromResult(PlanGenerationResult.Ok(plan, ProviderName, usedModel: false));
    }

    internal static string BuildPlanId(string command)
    {
        var normalized = command.Trim().ToLowerInvariant();
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(normalized));
        return "plan-" + Convert.ToHexString(hash, 0, 8).ToLower(CultureInfo.InvariantCulture);
    }

    private static bool ContainsAny(string haystack, params string[] needles) =>
        needles.Any(needle => haystack.Contains(needle, StringComparison.Ordinal));
}
