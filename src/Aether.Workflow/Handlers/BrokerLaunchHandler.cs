using Aether.Contracts.Errors;
using Aether.Execution.Actions;
using Aether.Contracts.Models;

namespace Aether.Workflow.Handlers;

/// <summary>
/// Executes an allowlisted LaunchApp step via the broker. The plan only carries
/// an app id; the broker resolves the executable and arguments from its trusted
/// registry. Compensation stops the launched process.
/// </summary>
public sealed class BrokerLaunchHandler : IActionHandler
{
    private readonly IWorkflowBroker _broker;

    public BrokerLaunchHandler(IWorkflowBroker broker) => _broker = broker ?? throw new ArgumentNullException(nameof(broker));

    public ActionKind Kind => ActionKind.LaunchApp;

    public async Task<HandlerResult> ExecuteAsync(ActionExecutionRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var appId = request.Step.AppId ?? "";
        try
        {
            var reply = await _broker.LaunchAppAsync(appId, request.TraceId, cancellationToken).ConfigureAwait(false);
            if (!reply.Accepted)
            {
                return HandlerResult.Fail(
                    string.IsNullOrWhiteSpace(reply.DenialCode) ? AetherErrorCodes.BrokerLaunchFailed : reply.DenialCode,
                    string.IsNullOrWhiteSpace(reply.DenialMessage) ? "The application could not be launched." : reply.DenialMessage);
            }

            return HandlerResult.Ok(new Dictionary<string, string>
            {
                ["appId"] = appId,
                ["instanceId"] = reply.InstanceId,
                ["processId"] = reply.ProcessId.ToString(System.Globalization.CultureInfo.InvariantCulture),
                ["registered"] = reply.Registered ? "true" : "false",
            });
        }
        catch (Broker.Client.BrokerRpcException exception)
        {
            return HandlerResult.Fail(exception.Code, exception.Message);
        }
    }

    public async Task<CompensationResult> CompensateAsync(ActionExecutionRequest request, CancellationToken cancellationToken)
    {
        var appId = request.Step.AppId ?? "";
        try
        {
            var reply = await _broker.StopAppAsync(appId, null, request.TraceId, cancellationToken).ConfigureAwait(false);
            return reply.Stopped ? CompensationResult.Succeeded() : CompensationResult.Failed(reply.DenialMessage);
        }
        catch (Broker.Client.BrokerRpcException exception)
        {
            return CompensationResult.Failed(exception.Message);
        }
    }
}
