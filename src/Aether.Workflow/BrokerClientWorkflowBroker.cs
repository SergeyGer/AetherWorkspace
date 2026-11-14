using Aether.Broker.Client;
using Aether.Contracts;
using Aether.Contracts.V1;

namespace Aether.Workflow;

/// <summary>Adapts the typed broker client to the workflow broker contract.</summary>
public sealed class BrokerClientWorkflowBroker : IWorkflowBroker
{
    private readonly BrokerClient _client;

    public BrokerClientWorkflowBroker(BrokerClient client)
    {
        _client = client ?? throw new ArgumentNullException(nameof(client));
        Registration = client.Registration ?? throw new InvalidOperationException("The broker client is not registered.");
    }

    public RegisterReply Registration { get; }

    public Task<SnapshotReply> GetSnapshotAsync(string traceId, CancellationToken cancellationToken = default) =>
        _client.GetSnapshotAsync(traceId, cancellationToken);

    public Task<LaunchAppReply> LaunchAppAsync(string appId, string traceId, CancellationToken cancellationToken = default) =>
        _client.LaunchAppAsync(appId, traceId, cancellationToken);

    public Task<StopAppReply> StopAppAsync(string appId, string? instanceId, string traceId, CancellationToken cancellationToken = default) =>
        _client.StopAppAsync(appId, instanceId, traceId, cancellationToken);

    public Task<PublishContextReply> PublishContextAsync(
        string contextRef,
        DataClassification classification,
        string traceId,
        CancellationToken cancellationToken = default) =>
        _client.PublishContextAsync(contextRef, classification, traceId, cancellationToken: cancellationToken);

    public Task<ResolveContextReply> ResolveContextAsync(string contextRef, string traceId, CancellationToken cancellationToken = default) =>
        _client.ResolveContextAsync(contextRef, traceId, cancellationToken);

    public Task<ArrangeWindowsReply> ArrangeWindowsAsync(string layout, string traceId, CancellationToken cancellationToken = default) =>
        _client.ArrangeWindowsAsync(layout, traceId, cancellationToken);

    public Task<RestartAppReply> RestartAppAsync(string appId, string traceId, CancellationToken cancellationToken = default) =>
        _client.RestartAppAsync(appId, traceId, cancellationToken);
}
