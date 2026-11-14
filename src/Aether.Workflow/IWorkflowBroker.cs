using Aether.Contracts;
using Aether.Contracts.V1;

namespace Aether.Workflow;

/// <summary>
/// Broker operations used by the deterministic executor handlers. The concrete
/// adapter wraps the typed gRPC client; tests provide fakes for orchestration-only
/// scenarios while end-to-end tests exercise the real broker.
/// </summary>
public interface IWorkflowBroker
{
    RegisterReply Registration { get; }

    Task<SnapshotReply> GetSnapshotAsync(string traceId, CancellationToken cancellationToken = default);

    Task<LaunchAppReply> LaunchAppAsync(string appId, string traceId, CancellationToken cancellationToken = default);

    Task<StopAppReply> StopAppAsync(string appId, string? instanceId, string traceId, CancellationToken cancellationToken = default);

    Task<PublishContextReply> PublishContextAsync(
        string contextRef,
        DataClassification classification,
        string traceId,
        CancellationToken cancellationToken = default);

    Task<ResolveContextReply> ResolveContextAsync(string contextRef, string traceId, CancellationToken cancellationToken = default);

    Task<ArrangeWindowsReply> ArrangeWindowsAsync(string layout, string traceId, CancellationToken cancellationToken = default);

    Task<RestartAppReply> RestartAppAsync(string appId, string traceId, CancellationToken cancellationToken = default);
}
