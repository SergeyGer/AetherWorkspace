using Aether.Contracts.Models;

namespace Aether.Execution.Actions;

/// <summary>Typed request handed to a deterministic action handler.</summary>
public sealed record ActionExecutionRequest(
    string PlanId,
    string TraceId,
    string SpanId,
    string StepId,
    int StepIndex,
    ActionPlanStep Step);

public sealed record HandlerResult(
    bool Success,
    string? ErrorCode,
    string Message,
    IReadOnlyDictionary<string, string>? Artifacts)
{
    public static HandlerResult Ok(IReadOnlyDictionary<string, string>? artifacts = null) =>
        new(true, null, "", artifacts ?? new Dictionary<string, string>());

    public static HandlerResult Fail(string errorCode, string message) =>
        new(false, errorCode, message, null);
}

public sealed record CompensationResult(bool Success, string Message)
{
    public static CompensationResult Succeeded() => new(true, "");

    public static CompensationResult Failed(string message) => new(false, message);
}

/// <summary>
/// Bound typed action implementation. Handlers are wired only by the composition
/// root; untrusted AI output cannot instantiate or select handlers.
/// </summary>
public interface IActionHandler
{
    ActionKind Kind { get; }

    Task<HandlerResult> ExecuteAsync(ActionExecutionRequest request, CancellationToken cancellationToken);

    Task<CompensationResult> CompensateAsync(ActionExecutionRequest request, CancellationToken cancellationToken);
}
