using System.Text.Json;
using Aether.AI.Providers;
using Aether.Contracts;
using Aether.Contracts.Errors;
using Aether.Contracts.Json;
using Aether.Security.Egress;
using Aether.Security.Policy;

namespace Aether.AI.Summarization;

/// <summary>
/// Optional cloud summarization adapter. Every call is gated by the egress
/// gateway; RESTRICTED, UNKNOWN and disabled-consent requests never reach the
/// transport. Only explicitly permitted classifications use this provider.
/// </summary>
public sealed class CloudSummaryProvider : ISummaryProvider
{
    public const string SummaryPrompt =
        "Summarize the supplied synthetic workspace context. Return only JSON of the form " +
        "{\"summary\":\"...\"}. Do not include account identifiers or raw sensitive values.";

    private readonly ICloudModelTransport _transport;
    private readonly IEgressGateway _egressGateway;
    private readonly PlanProviderOptions _options;

    public CloudSummaryProvider(ICloudModelTransport transport, IEgressGateway egressGateway, PlanProviderOptions options)
    {
        _transport = transport ?? throw new ArgumentNullException(nameof(transport));
        _egressGateway = egressGateway ?? throw new ArgumentNullException(nameof(egressGateway));
        _options = options ?? throw new ArgumentNullException(nameof(options));
    }

    public string ProviderId => "cloud-summary";

    public AiProviderKind ProviderKind => AiProviderKind.Cloud;

    public async Task<SummaryResult> SummarizeAsync(SummaryRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var payloadBytes = System.Text.Encoding.UTF8.GetByteCount(request.PayloadJson);
        var egressRequest = new EgressRequest(
            request.TraceId,
            _transport.ProviderId,
            AiProviderKind.Cloud,
            AiTaskKind.Summarization,
            request.Classification,
            _options.CloudEnabled,
            _options.CloudConsentGranted,
            SanitizationReviewed: false,
            PayloadKind: request.ContextType,
            PayloadBytes: payloadBytes);

        var outcome = await _egressGateway.SendAsync(
            egressRequest,
            token => _transport.SendAsync(
                new CloudModelRequest(
                    request.TraceId,
                    AiTaskKind.Summarization,
                    request.Classification,
                    request.ContextType,
                    payloadBytes,
                    SummaryPrompt,
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
                "The cloud summarization provider did not return a usable response.",
                ProviderId,
                request.Classification);
        }

        if (!TryExtractSummary(outcome.Value.Content, out var summary, out var error))
        {
            return SummaryResult.Fail(AetherErrorCodes.AiOutputInvalid, error!, ProviderId, request.Classification);
        }

        return SummaryResult.Ok(summary!, ProviderId, request.Classification, 0);
    }

    internal static bool TryExtractSummary(string? content, out string? summary, out string? error)
    {
        summary = null;
        if (string.IsNullOrWhiteSpace(content))
        {
            error = "The provider returned an empty response.";
            return false;
        }

        var trimmed = content.Trim();
        if (!trimmed.StartsWith('{'))
        {
            summary = trimmed;
            error = null;
            return true;
        }

        try
        {
            using var document = JsonDocument.Parse(trimmed, new JsonDocumentOptions { MaxDepth = AetherJson.MaxDocumentDepth });
            if (document.RootElement.TryGetProperty("summary", out var summaryElement) && summaryElement.ValueKind == JsonValueKind.String)
            {
                summary = summaryElement.GetString() ?? "";
                error = string.IsNullOrWhiteSpace(summary) ? "The provider returned an empty summary." : null;
                return error is null;
            }

            error = "The provider response is missing a summary field.";
            return false;
        }
        catch (JsonException exception)
        {
            error = $"The provider response is not valid JSON: {exception.Message}";
            return false;
        }
    }
}
