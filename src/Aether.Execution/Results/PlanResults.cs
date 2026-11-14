using Aether.Contracts.Errors;
using Aether.Contracts.Models;

namespace Aether.Execution.Results;

/// <summary>Explicit per-step lifecycle states.</summary>
public enum StepState
{
    Pending = 0,
    Authorized = 1,
    Running = 2,
    Succeeded = 3,
    Denied = 4,
    Failed = 5,
    Compensated = 6,
}

public sealed record PlanValidationIssue(string Code, string Message, int? StepIndex);

public sealed record ActionPlanValidationResult(bool IsValid, IReadOnlyList<PlanValidationIssue> Issues)
{
    public static ActionPlanValidationResult Valid { get; } = new(true, []);

    public static ActionPlanValidationResult Invalid(params PlanValidationIssue[] issues) => new(false, issues);

    public string? FirstCode => Issues.Count == 0 ? null : Issues[0].Code;
}

public sealed record StepExecutionResult(
    string StepId,
    int Index,
    ActionKind Kind,
    string Action,
    StepState State,
    string? Code,
    string Message,
    IReadOnlyDictionary<string, string>? Artifacts,
    IReadOnlyList<StepState> Transitions);

/// <summary>Structured, redacted plan outcome returned to the shell and audit.</summary>
public sealed record PlanExecutionResult(
    string PlanId,
    string TraceId,
    bool Succeeded,
    IReadOnlyList<StepExecutionResult> Steps,
    IReadOnlyList<PlanValidationIssue> ValidationIssues,
    string? DenialCode,
    string? DenialMessage,
    DenialCategory DenialCategory,
    DateTimeOffset StartedAtUtc,
    DateTimeOffset CompletedAtUtc,
    long DurationMs)
{
    public IReadOnlyDictionary<string, string>? FindStepArtifacts(string action)
    {
        foreach (var step in Steps)
        {
            if (string.Equals(step.Action, action, StringComparison.Ordinal) && step.Artifacts is not null)
            {
                return step.Artifacts;
            }
        }

        return null;
    }
}
