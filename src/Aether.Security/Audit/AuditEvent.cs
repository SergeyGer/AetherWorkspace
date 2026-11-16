namespace Aether.Security.Audit;

/// <summary>Stable audit event type names.</summary>
public static class AuditEventTypes
{
    public const string Classification = "classification";
    public const string Policy = "policy";
    public const string Egress = "egress";
    public const string PlanValidation = "plan.validation";
    public const string ExecutionStep = "execution.step";
    public const string ExecutionPlan = "execution.plan";
    public const string BrokerLaunch = "broker.launch";
    public const string BrokerSession = "broker.session";
    public const string ContextPublish = "context.publish";
    public const string WindowArrange = "window.arrange";
    public const string WorkflowResult = "workflow.result";
    public const string AuditChain = "audit.chain";
    public const string IntentResolve = "fdc3.intent.resolve";
    public const string WorkspaceLayoutSave = "workspace.layout.save";
    public const string WorkspaceLayoutRestore = "workspace.layout.restore";
}

public static class AuditOutcomes
{
    public const string Allowed = "allowed";
    public const string Denied = "denied";
    public const string Succeeded = "succeeded";
    public const string Failed = "failed";
    public const string Cancelled = "cancelled";
    public const string Info = "info";
}

/// <summary>
/// Metadata-only audit event. Raw prompt text, payloads, identifiers and model
/// credentials are deliberately excluded from this schema.
/// </summary>
public sealed record AuditEventDraft
{
    public required string TraceId { get; init; }

    public required string EventType { get; init; }

    public required string Outcome { get; init; }

    public string? Code { get; init; }

    public string? PolicyVersion { get; init; }

    /// <summary>Classification wire name, when applicable.</summary>
    public string? Classification { get; init; }

    /// <summary>"local" or "cloud" provider category, when applicable.</summary>
    public string? ProviderCategory { get; init; }

    public int RedactionCount { get; init; }

    public long PayloadBytes { get; init; }

    public long? DurationMs { get; init; }

    public string? BrokeredAppId { get; init; }
}

/// <summary>Chained audit record persisted locally.</summary>
public sealed record AuditEvent
{
    public long Sequence { get; init; }

    public DateTimeOffset TimestampUtc { get; init; }

    public required string TraceId { get; init; }

    public required string EventType { get; init; }

    public required string Outcome { get; init; }

    public string? Code { get; init; }

    public string? PolicyVersion { get; init; }

    public string? Classification { get; init; }

    public string? ProviderCategory { get; init; }

    public int RedactionCount { get; init; }

    public long PayloadBytes { get; init; }

    public long? DurationMs { get; init; }

    public string? BrokeredAppId { get; init; }

    public string PreviousHash { get; init; } = "";

    public string Hash { get; init; } = "";
}

/// <summary>Audit sink contract. Append must be thread-safe and non-blocking for callers.</summary>
public interface IAuditSink
{
    void Append(AuditEventDraft draft);

    IReadOnlyList<AuditEvent> Snapshot();
}

/// <summary>Optional durable storage for chained audit events.</summary>
public interface IAuditEventStore
{
    void Append(AuditEvent auditEvent);
}

/// <summary>A durable audit store that can be read back to continue an existing chain.</summary>
public interface IAuditEventReader
{
    IReadOnlyList<AuditEvent> ReadAllRotated();
}
