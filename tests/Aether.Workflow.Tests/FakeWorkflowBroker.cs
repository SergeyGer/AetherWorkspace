using Aether.Broker.Client;
using Aether.Contracts;
using Aether.Contracts.Errors;
using Aether.Contracts.V1;

namespace Aether.Workflow.Tests;

/// <summary>Configurable fake broker for orchestration-only tests.</summary>
public sealed class FakeWorkflowBroker : IWorkflowBroker
{
    public FakeWorkflowBroker(RegisterReply registration)
    {
        Registration = registration;
        Snapshot = Aether.Testing.TestData.CreateSnapshotReply();
    }

    public RegisterReply Registration { get; }

    public SnapshotReply Snapshot { get; set; }

    public List<string> LaunchedApps { get; } = [];

    public List<string> PublishedContexts { get; } = [];

    /// <summary>Optional override for publish denial code (default simulates the real broker rules).</summary>
    public string? PublishDenialCode { get; set; }

    public bool DenyLaunch { get; set; }

    public bool DenyPublish { get; set; }

    public bool DenySummaryResolution { get; set; }

    public bool FailSnapshot { get; set; }

    public Exception? ThrowOnSnapshot { get; set; }

    public Task<SnapshotReply> GetSnapshotAsync(string traceId, CancellationToken cancellationToken = default)
    {
        if (ThrowOnSnapshot is not null)
        {
            throw ThrowOnSnapshot;
        }

        return FailSnapshot
            ? throw new BrokerRpcException(AetherErrorCodes.BrokerNotReady, DenialCategory.PolicyDenied, Grpc.Core.StatusCode.Unavailable, "broker not ready")
            : Task.FromResult(Snapshot);
    }

    public Task<LaunchAppReply> LaunchAppAsync(string appId, string traceId, CancellationToken cancellationToken = default)
    {
        LaunchedApps.Add(appId);
        return Task.FromResult(new LaunchAppReply
        {
            Accepted = !DenyLaunch,
            AppId = appId,
            InstanceId = "inst-" + appId,
            ProcessId = 4242,
            Registered = !DenyLaunch,
            DenialCode = DenyLaunch ? AetherErrorCodes.BrokerLaunchFailed : "",
            DenialMessage = DenyLaunch ? "simulated launch failure" : "",
        });
    }

    public Task<StopAppReply> StopAppAsync(string appId, string? instanceId, string traceId, CancellationToken cancellationToken = default) =>
        Task.FromResult(new StopAppReply { Stopped = true });

    public Task<PublishContextReply> PublishContextAsync(
        string contextRef, DataClassification classification, string traceId, CancellationToken cancellationToken = default)
    {
        PublishedContexts.Add(contextRef);
        var denialCode = PublishDenialCode
            ?? (DenyPublish ? AetherErrorCodes.AuthClassificationDenied
                : classification == DataClassification.Unknown ? AetherErrorCodes.PolicyClassificationUnknown : null);
        return Task.FromResult(new PublishContextReply
        {
            Accepted = denialCode is null,
            MessageId = denialCode is null ? "msg-" + contextRef : "",
            DeliveredCount = denialCode is null ? 3 : 0,
            DenialCode = denialCode ?? "",
            DenialMessage = denialCode is null ? "" : "simulated publish denial",
        });
    }

    public Task<ResolveContextReply> ResolveContextAsync(string contextRef, string traceId, CancellationToken cancellationToken = default)
    {
        if (DenySummaryResolution)
        {
            return Task.FromResult(new ResolveContextReply
            {
                Allowed = false,
                DenialCode = AetherErrorCodes.AuthClassificationDenied,
                DenialMessage = "simulated resolve denial",
            });
        }

        var payload = contextRef.Contains("exposure", StringComparison.Ordinal)
            ? "{\"type\":\"aether.exposure\",\"name\":\"synthetic\",\"currency\":\"USD\",\"asOf\":\"2026-11-14\",\"positions\":[]}"
            : "{\"type\":\"fdc3.instrument\",\"name\":\"Northstar 2032 Bond\",\"id\":{\"ticker\":\"NRST\"}}";
        return Task.FromResult(new ResolveContextReply
        {
            Allowed = true,
            ContextRef = contextRef,
            ContextType = contextRef.Contains("exposure", StringComparison.Ordinal) ? "aether.exposure" : "fdc3.instrument",
            Classification = Classification.Restricted,
            PayloadJson = payload,
            Provenance = new Provenance { SourceAppId = "aether.broker", SourceInstanceId = "broker", TraceId = traceId },
        });
    }

    public Task<ArrangeWindowsReply> ArrangeWindowsAsync(string layout, string traceId, CancellationToken cancellationToken = default) =>
        Task.FromResult(new ArrangeWindowsReply
        {
            Applied = true,
            Layout = new WindowLayout
            {
                Layout = layout,
                MonitorId = "virtual-monitor-1",
                Placements =
                {
                    new WindowPlacement { AppId = "sample.watchlist", InstanceId = "inst-1", WindowId = "main", Title = "Watchlist", MonitorId = "virtual-monitor-1", Visible = true, Bounds = new WindowBounds { X = 0, Y = 0, Width = 800, Height = 600 } },
                },
            },
        });

    public Task<RestartAppReply> RestartAppAsync(string appId, string traceId, CancellationToken cancellationToken = default) =>
        Task.FromResult(new RestartAppReply { Accepted = true, InstanceId = "inst-" + appId });
}
