using Aether.AI.Summarization;
using Aether.Contracts;
using Aether.Contracts.Errors;
using Aether.Contracts.Models;
using Aether.Execution.Actions;

namespace Aether.Workflow.Handlers;

/// <summary>
/// Resolves a trusted context and summarizes it through the privacy-aware summary
/// service. "private" mode always uses the local provider; restricted and unknown
/// classifications never route to a cloud provider.
/// </summary>
public sealed class SummarizeContextActionHandler : IActionHandler
{
    private readonly IWorkflowBroker _broker;
    private readonly ISummaryService _summaryService;

    public SummarizeContextActionHandler(IWorkflowBroker broker, ISummaryService summaryService)
    {
        _broker = broker ?? throw new ArgumentNullException(nameof(broker));
        _summaryService = summaryService ?? throw new ArgumentNullException(nameof(summaryService));
    }

    public ActionKind Kind => ActionKind.SummarizeContext;

    public async Task<HandlerResult> ExecuteAsync(ActionExecutionRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var contextRef = request.Step.ContextRef ?? "";
        try
        {
            var resolved = await _broker.ResolveContextAsync(contextRef, request.TraceId, cancellationToken).ConfigureAwait(false);
            if (!resolved.Allowed)
            {
                return HandlerResult.Fail(
                    string.IsNullOrWhiteSpace(resolved.DenialCode) ? AetherErrorCodes.ExecutionDenied : resolved.DenialCode,
                    string.IsNullOrWhiteSpace(resolved.DenialMessage) ? "The context could not be resolved." : resolved.DenialMessage);
            }

            var summaryRequest = new SummaryRequest(
                request.TraceId,
                request.PlanId,
                resolved.ContextRef,
                resolved.ContextType,
                resolved.Classification.FromProto(),
                resolved.PayloadJson,
                request.Step.Mode ?? SummarizeModes.Private);

            var result = summaryRequest.Mode == SummarizeModes.Private
                ? await _summaryService.SummarizePrivatelyAsync(summaryRequest, cancellationToken).ConfigureAwait(false)
                : await _summaryService.SummarizeAsync(summaryRequest, cancellationToken).ConfigureAwait(false);

            if (!result.Success || result.Summary is null)
            {
                return HandlerResult.Fail(
                    string.IsNullOrWhiteSpace(result.ErrorCode) ? AetherErrorCodes.AiProviderUnavailable : result.ErrorCode,
                    string.IsNullOrWhiteSpace(result.Message) ? "The context could not be summarized." : result.Message);
            }

            return HandlerResult.Ok(new Dictionary<string, string>
            {
                ["summary"] = result.Summary,
                ["provider"] = result.Provider,
                ["classification"] = result.OutputClassification.ToWireName(),
                ["redactionCount"] = result.RedactionCount.ToString(System.Globalization.CultureInfo.InvariantCulture),
            });
        }
        catch (Broker.Client.BrokerRpcException exception)
        {
            return HandlerResult.Fail(exception.Code, exception.Message);
        }
    }

    public Task<CompensationResult> CompensateAsync(ActionExecutionRequest request, CancellationToken cancellationToken) =>
        Task.FromResult(CompensationResult.Succeeded());
}
