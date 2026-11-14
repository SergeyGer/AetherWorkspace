using Aether.Contracts.Errors;
using Aether.Contracts.Json;
using Aether.Contracts.Models;
using Aether.Execution.Actions;

namespace Aether.Workflow.Handlers;

/// <summary>
/// Requests deterministic top-level window arrangement from the broker. No
/// reparenting or third-party window hosting is involved.
/// </summary>
public sealed class BrokerArrangeWindowsHandler : IActionHandler
{
    private readonly IWorkflowBroker _broker;

    public BrokerArrangeWindowsHandler(IWorkflowBroker broker) => _broker = broker ?? throw new ArgumentNullException(nameof(broker));

    public ActionKind Kind => ActionKind.ArrangeWindows;

    public async Task<HandlerResult> ExecuteAsync(ActionExecutionRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var layout = request.Step.Layout ?? Layouts.SideBySide;
        try
        {
            var reply = await _broker.ArrangeWindowsAsync(layout, request.TraceId, cancellationToken).ConfigureAwait(false);
            if (!reply.Applied)
            {
                return HandlerResult.Fail(
                    string.IsNullOrWhiteSpace(reply.DenialCode) ? AetherErrorCodes.BrokerArrangeNoWindows : reply.DenialCode,
                    string.IsNullOrWhiteSpace(reply.DenialMessage) ? "The windows could not be arranged." : reply.DenialMessage);
            }

            var domainLayout = new WindowLayout(
                reply.Layout.Layout,
                reply.Layout.Placements
                    .Select(placement => new WindowPlacement(
                        placement.AppId,
                        placement.InstanceId,
                        placement.WindowId,
                        placement.Title,
                        new WindowBounds(
                            placement.Bounds.X,
                            placement.Bounds.Y,
                            placement.Bounds.Width,
                            placement.Bounds.Height),
                        placement.MonitorId,
                        placement.Visible))
                    .ToArray(),
                reply.Layout.MonitorId);

            return HandlerResult.Ok(new Dictionary<string, string>
            {
                ["layout"] = domainLayout.Layout,
                ["windowCount"] = domainLayout.Placements.Count.ToString(System.Globalization.CultureInfo.InvariantCulture),
                ["layoutJson"] = AetherJson.Serialize(domainLayout),
            });
        }
        catch (Broker.Client.BrokerRpcException exception)
        {
            return HandlerResult.Fail(exception.Code, exception.Message);
        }
    }

    public Task<CompensationResult> CompensateAsync(ActionExecutionRequest request, CancellationToken cancellationToken)
    {
        // Window arrangement is non-destructive and user-adjustable; the MVP does
        // not capture previous geometry, so no automatic compensation is claimed.
        return Task.FromResult(CompensationResult.Failed("Window arrangement is non-destructive and has no automatic compensation."));
    }
}
