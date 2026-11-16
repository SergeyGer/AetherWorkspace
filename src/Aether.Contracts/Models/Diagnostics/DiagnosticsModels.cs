namespace Aether.Contracts.Models.Diagnostics;

/// <summary>
/// Sanitized, operator-initiated diagnostics bundle (v0.5 AW-033). Contains only
/// metadata/timeline/aggregates: never raw context payloads, commands, summaries, secrets or
/// customer content, and it is never uploaded automatically.
/// </summary>
public sealed record DiagnosticsBundle(
    string SchemaVersion,
    DateTimeOffset GeneratedAtUtc,
    string AppVersion,
    string BrokerVersion,
    string PolicyVersion,
    DiagnosticsRun Run,
    IReadOnlyList<DiagnosticsTimelineEntry> Timeline,
    IReadOnlyList<DiagnosticsApp> Apps,
    DiagnosticsEnvironment Environment,
    DiagnosticsAudit Audit,
    DiagnosticsProviders Providers,
    IReadOnlyDictionary<string, string> Flags);

/// <summary>Reference-workflow outcome metadata (no payload/command/summary content).</summary>
public sealed record DiagnosticsRun(
    string Status,
    string TraceId,
    string? PlanId,
    string Classification,
    string? DenialCode,
    long DurationMs,
    string Planner,
    bool UsedModel);

/// <summary>One redacted timeline row for the Inspector.</summary>
public sealed record DiagnosticsTimelineEntry(
    int Sequence,
    string Action,
    string State,
    string? Code,
    long? DurationMs);

/// <summary>Registered app and health/window counts (identifier level only).</summary>
public sealed record DiagnosticsApp(string AppId, string Health, long Windows);

/// <summary>Safe runtime environment facts (no user/machine names).</summary>
public sealed record DiagnosticsEnvironment(
    string Os,
    string Architecture,
    string Framework,
    string ProcessArchitecture);

/// <summary>Aggregated audit-chain facts (no event payloads or IDs).</summary>
public sealed record DiagnosticsAudit(
    int EntryCount,
    bool ChainValid,
    int RotatedFiles,
    bool FileChainValid,
    IReadOnlyDictionary<string, int> EventTypeCounts);

/// <summary>Provider routing metadata (no keys or secrets).</summary>
public sealed record DiagnosticsProviders(
    string Mode,
    string RouteKind,
    string? RouteReason,
    string? Provider,
    string? Model,
    bool CloudEnabled,
    bool CloudConsentGranted,
    bool CloudAdapterConfigured,
    bool LocalModelAdapterConfigured);
