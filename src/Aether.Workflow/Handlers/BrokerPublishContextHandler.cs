using System.Globalization;
using Aether.Contracts;
using Aether.Contracts.Errors;
using Aether.Contracts.Models;
using Aether.Execution.Actions;

namespace Aether.Workflow.Handlers;

/// <summary>
/// Publishes a catalog-referenced typed context on the workspace channel. The
/// plan only references a trusted catalog id; payload content is resolved by the
/// broker, so untrusted plan text cannot inject data into the context stream.
/// </summary>
public sealed class BrokerPublishContextHandler : IActionHandler
{
    private readonly IWorkflowBroker _broker;
    private readonly DataClassification _sourceClassification;

    public BrokerPublishContextHandler(IWorkflowBroker broker, DataClassification sourceClassification)
    {
        _broker = broker ?? throw new ArgumentNullException(nameof(broker));
        _sourceClassification = sourceClassification;
    }

    public ActionKind Kind => ActionKind.PublishContext;

    public async Task<HandlerResult> ExecuteAsync(ActionExecutionRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var contextRef = request.Step.ContextRef ?? "";
        try
        {
            var reply = await _broker
                .PublishContextAsync(contextRef, _sourceClassification, request.TraceId, cancellationToken)
                .ConfigureAwait(false);
            if (!reply.Accepted)
            {
                return HandlerResult.Fail(
                    string.IsNullOrWhiteSpace(reply.DenialCode) ? AetherErrorCodes.ExecutionDenied : reply.DenialCode,
                    string.IsNullOrWhiteSpace(reply.DenialMessage) ? "The context could not be published." : reply.DenialMessage);
            }

            return HandlerResult.Ok(new Dictionary<string, string>
            {
                ["contextRef"] = contextRef,
                ["messageId"] = reply.MessageId,
                ["deliveredCount"] = reply.DeliveredCount.ToString(CultureInfo.InvariantCulture),
                ["droppedCount"] = reply.DroppedCount.ToString(CultureInfo.InvariantCulture),
                ["overflowed"] = reply.Overflowed ? "true" : "false",
                ["classification"] = _sourceClassification.ToWireName(),
            });
        }
        catch (Broker.Client.BrokerRpcException exception)
        {
            return HandlerResult.Fail(exception.Code, exception.Message);
        }
    }

    public Task<CompensationResult> CompensateAsync(ActionExecutionRequest request, CancellationToken cancellationToken)
    {
        // Published events are at-most-once and cannot be retracted; there is no
        // safe compensating action for an already delivered context event.
        return Task.FromResult(CompensationResult.Failed("Published context events are not reversible."));
    }
}
