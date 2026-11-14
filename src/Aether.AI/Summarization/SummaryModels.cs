using Aether.Contracts;
using Aether.Security.Policy;

namespace Aether.AI.Summarization;

/// <summary>Request to summarize one resolved typed context.</summary>
public sealed record SummaryRequest(
    string TraceId,
    string PlanId,
    string ContextRef,
    string ContextType,
    DataClassification Classification,
    string PayloadJson,
    string Mode);

public sealed record SummaryResult(
    bool Success,
    string? Summary,
    string Provider,
    DataClassification OutputClassification,
    int RedactionCount,
    string? ErrorCode,
    string? Message)
{
    public static SummaryResult Ok(string summary, string provider, DataClassification classification, int redactionCount) =>
        new(true, summary, provider, classification, redactionCount, null, null);

    public static SummaryResult Fail(string errorCode, string message, string provider, DataClassification classification) =>
        new(false, null, provider, classification, 0, errorCode, message);
}

/// <summary>
/// Summarization provider. Provider routing is decided by policy, never by
/// untrusted plan text: a plan "private" mode can only select the private path.
/// </summary>
public interface ISummaryProvider
{
    string ProviderId { get; }

    AiProviderKind ProviderKind { get; }

    Task<SummaryResult> SummarizeAsync(SummaryRequest request, CancellationToken cancellationToken = default);
}
