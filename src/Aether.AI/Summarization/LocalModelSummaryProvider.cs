using System.Text.Json;
using Aether.AI.Providers;
using Aether.Contracts;
using Aether.Contracts.Errors;
using Aether.Contracts.Json;
using Aether.Security.Egress;
using Aether.Security.Policy;

namespace Aether.AI.Summarization;

/// <summary>
/// Optional local-model summary provider. Requests are categorized as local, so
/// policy always permits them, but they still pass through the egress gateway so
/// that all provider attempts (including local ones) are instrumented and
/// audited. Missing local model yields a clear error; no cloud fallback occurs.
/// </summary>
public sealed class LocalModelSummaryProvider : ISummaryProvider
{
    public const string LocalSummaryPrompt =
        "Summarize the supplied synthetic workspace context privately. Return only JSON of the form " +
        "{\"summary\":\"...\"}. Do not include account identifiers.";

    private readonly ILocalModelTransport _transport;
    private readonly IEgressGateway _egressGateway;

    public LocalModelSummaryProvider(ILocalModelTransport transport, IEgressGateway egressGateway)
    {
        _transport = transport ?? throw new ArgumentNullException(nameof(transport));
        _egressGateway = egressGateway ?? throw new ArgumentNullException(nameof(egressGateway));
    }

    public string ProviderId => "local-model-summary";

    public AiProviderKind ProviderKind => AiProviderKind.Local;

    public async Task<SummaryResult> SummarizeAsync(SummaryRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var payloadBytes = System.Text.Encoding.UTF8.GetByteCount(request.PayloadJson);
        var outcome = await _egressGateway.SendAsync(
            new EgressRequest(
                request.TraceId,
                _transport.ProviderId,
                AiProviderKind.Local,
                AiTaskKind.Summarization,
                request.Classification,
                CloudEnabled: false,
                CloudConsentGranted: false,
                SanitizationReviewed: false,
                PayloadKind: request.ContextType,
                PayloadBytes: payloadBytes),
            token => _transport.SendAsync(
                new CloudModelRequest(
                    request.TraceId,
                    AiTaskKind.Summarization,
                    request.Classification,
                    request.ContextType,
                    payloadBytes,
                    LocalSummaryPrompt,
                    request.PayloadJson),
                token),
            cancellationToken).ConfigureAwait(false);

        if (!outcome.Sent)
        {
            return SummaryResult.Fail(
                string.IsNullOrEmpty(outcome.Decision.Code) ? AetherErrorCodes.EgressDenied : outcome.Decision.Code,
                outcome.Decision.Explanation,
                ProviderId,
                request.Classification);
        }

        if (outcome.ErrorCode is not null || outcome.Value is null || !outcome.Value.Success)
        {
            return SummaryResult.Fail(
                outcome.Value?.ErrorCode ?? outcome.ErrorCode ?? AetherErrorCodes.AiProviderUnavailable,
                "The local model provider did not return a usable response. Install and configure a local model, or use the deterministic private summary.",
                ProviderId,
                request.Classification);
        }

        var summary = ExtractSummary(outcome.Value.Content);
        return summary is null
            ? SummaryResult.Fail(
                AetherErrorCodes.AiOutputInvalid,
                "The local model provider returned an unusable response.",
                ProviderId,
                request.Classification)
            : SummaryResult.Ok(summary, ProviderId, request.Classification, 0);
    }

    private static string? ExtractSummary(string? content)
    {
        if (string.IsNullOrWhiteSpace(content))
        {
            return null;
        }

        var trimmed = content.Trim();
        if (!trimmed.StartsWith('{'))
        {
            return trimmed;
        }

        try
        {
            using var document = JsonDocument.Parse(trimmed, new JsonDocumentOptions { MaxDepth = AetherJson.MaxDocumentDepth });
            return document.RootElement.TryGetProperty("summary", out var summary) && summary.ValueKind == JsonValueKind.String
                ? summary.GetString()
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
