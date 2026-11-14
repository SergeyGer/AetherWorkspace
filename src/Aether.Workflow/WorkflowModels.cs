using Aether.Contracts;
using Aether.Contracts.Errors;
using Aether.Contracts.Models;
using Aether.Execution.Results;
using Aether.Execution.Executor;

namespace Aether.Workflow;

/// <summary>One operator-requested workflow run.</summary>
public sealed record WorkflowRequest(
    string Command,
    DataClassification SourceClassification,
    string? TraceId = null,
    ActionPlan? SuppliedPlan = null,
    string SourceAppId = "aether.shell");

/// <summary>Safe summary output returned to the shell (never written to audit).</summary>
public sealed record SummaryOutcome(
    string? Text,
    string Provider,
    DataClassification OutputClassification,
    int RedactionCount);

/// <summary>Structured workflow outcome for the shell, tests and evidence capture.</summary>
public sealed record WorkflowResult(
    bool Success,
    string TraceId,
    string? PlanId,
    ActionPlan? Plan,
    DataClassification Classification,
    IReadOnlyList<string> ClassificationReasons,
    PlanExecutionResult? Execution,
    SummaryOutcome? Summary,
    string? DenialCode,
    string? DenialMessage,
    DenialCategory DenialCategory,
    string PlannerProvider,
    bool UsedModel,
    long DurationMs)
{
    public static WorkflowResult Denied(
        string traceId,
        DataClassification classification,
        IReadOnlyList<string> reasons,
        string denialCode,
        string denialMessage,
        string plannerProvider,
        bool usedModel,
        long durationMs,
        ActionPlan? plan = null,
        PlanExecutionResult? execution = null) =>
        new(false, traceId, plan?.PlanId, plan, classification, reasons, execution, null, denialCode, denialMessage,
            AetherErrorCodes.GetDenialCategory(denialCode), plannerProvider, usedModel, durationMs);
}

public sealed record WorkflowOrchestratorOptions
{
    /// <summary>
    /// Configured per-deployment plan action budget. The library default is 5
    /// (spec 5.3); the reference workflow configuration uses 6 because it
    /// launches three independent sample processes plus publish/arrange/summarize.
    /// </summary>
    public int MaxActionsPerPlan { get; init; } = 6;

    public ExecutionOptions Execution { get; init; } = new();

    public bool PreferCloudForPublicSummaries { get; init; }
}
