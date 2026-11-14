using Aether.Contracts.Errors;
using Aether.Contracts.Models;
using Aether.Execution.Actions;

namespace Aether.Workflow.Handlers;

/// <summary>Local, trace-safe status display. No IPC, process or network side effect.</summary>
public sealed class ShowStatusHandler : IActionHandler
{
    public ActionKind Kind => ActionKind.ShowStatus;

    public Task<HandlerResult> ExecuteAsync(ActionExecutionRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var text = request.Step.StatusText ?? "";
        if (string.IsNullOrWhiteSpace(text))
        {
            return Task.FromResult(HandlerResult.Fail(
                AetherErrorCodes.PlanStatusInvalid,
                "Status text is required."));
        }

        return Task.FromResult(HandlerResult.Ok(new Dictionary<string, string> { ["statusText"] = text }));
    }

    public Task<CompensationResult> CompensateAsync(ActionExecutionRequest request, CancellationToken cancellationToken) =>
        Task.FromResult(CompensationResult.Succeeded());
}
