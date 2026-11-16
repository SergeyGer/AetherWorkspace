namespace Aether.Shell.Headless;

/// <summary>Structured shell result used by deterministic E2E assertions and demo scripts.</summary>
public sealed record ShellOutput(
    bool Success,
    string Status,
    string TraceId,
    string? PlanId,
    string Classification,
    IReadOnlyList<string> ClassificationReasons,
    IReadOnlyList<ShellStepOutput> Steps,
    IReadOnlyList<string> ValidationIssues,
    ShellSummaryOutput? Summary,
    string? DenialCode,
    string? DenialMessage,
    string DenialCategory,
    string Planner,
    bool UsedModel,
    long DurationMs,
    ShellEgressOutput Egress,
    ShellAuditOutput Audit,
    ShellProvidersOutput Providers,
    IReadOnlyList<string> Limitations,
    IReadOnlyList<ShellTimelineEntry>? Timeline = null,
    ShellDiagnosticsOutput? Diagnostics = null);

/// <summary>Redacted Inspector timeline entry for one workflow step (v0.5 AW-032).</summary>
public sealed record ShellTimelineEntry(int Sequence, string Action, string State, string? Code);

/// <summary>Result of an explicit sanitized diagnostics export (v0.5 AW-033).</summary>
public sealed record ShellDiagnosticsOutput(string Path, long Bytes, bool Redacted);

public sealed record ShellStepOutput(
    string Action,
    string State,
    string? Code,
    IReadOnlyDictionary<string, string>? Artifacts);

public sealed record ShellSummaryOutput(
    string Text,
    string Provider,
    string Classification,
    int RedactionCount);

public sealed record ShellEgressOutput(
    long Attempts,
    long ExternalAttempts,
    long Denied,
    IReadOnlyDictionary<string, long> DenialsByCode);

public sealed record ShellAuditOutput(
    int EntryCount,
    bool ChainValid,
    string? AuditFile,
    int RotatedFiles,
    bool FileChainValid);

/// <summary>Provider/mode health snapshot without performing any network I/O.</summary>
public sealed record ShellProvidersOutput(
    string Mode,
    bool DeterministicPlanner,
    bool LocalDeterministicSummary,
    bool CloudAdapterConfigured,
    bool LocalModelAdapterConfigured,
    bool CloudEnabled,
    bool CloudConsentGranted,
    string? ModelProvider = null,
    string? ModelName = null,
    string RouteKind = "DeterministicLocal",
    string? RouteReason = null,
    bool CloudAttemptAllowed = false);
