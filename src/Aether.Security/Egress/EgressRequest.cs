using Aether.Contracts;
using Aether.Security.Policy;

namespace Aether.Security.Egress;

/// <summary>
/// One outbound attempt descriptor. Every provider egress path, including
/// retries, errors and telemetry, must go through the egress gateway.
/// </summary>
public sealed record EgressRequest(
    string TraceId,
    string ProviderId,
    AiProviderKind ProviderKind,
    AiTaskKind TaskKind,
    DataClassification Classification,
    bool CloudEnabled,
    bool CloudConsentGranted,
    bool SanitizationReviewed,
    string PayloadKind,
    int PayloadBytes);

public sealed record EgressOutcome<T>(bool Sent, T? Value, PolicyDecision Decision, string? ErrorCode = null);

public sealed record EgressStatistics(
    long Attempts,
    long ExternalAttempts,
    long Denied,
    long Succeeded,
    long Failed,
    IReadOnlyDictionary<string, long> DenialsByCode);
