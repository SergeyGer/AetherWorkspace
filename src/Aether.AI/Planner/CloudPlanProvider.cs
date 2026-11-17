using System.Text.Json;
using Aether.AI.Providers;
using Aether.Contracts;
using Aether.Contracts.Errors;
using Aether.Contracts.Json;
using Aether.Contracts.Models;
using Aether.Security.Audit;
using Aether.Security.Egress;
using Aether.Security.Policy;

namespace Aether.AI.Planner;

/// <summary>
/// Optional cloud planning adapter. It always sends through the egress gateway,
/// so RESTRICTED/UNKNOWN classifications, disabled cloud mode and missing consent
/// deny the request before the transport is invoked. Model output is untrusted:
/// strict JSON is parsed here and the plan is later validated by the executor.
/// </summary>
public sealed class CloudPlanProvider : IPlanGenerator
{
    public const string PlannerPrompt =
        "You are a bounded planning adapter for a synthetic desktop demo. Return ONLY a JSON object: " +
        "{\"schemaVersion\":\"1.0\",\"planId\":\"plan-1\",\"steps\":[...]}. " +
        "Allowed actions and fields: " +
        "LaunchApp requires appId (one of sample.watchlist, sample.risk, sample.chart); " +
        "PublishContext requires contextRef (use synthetic.instrument.vega); " +
        "ArrangeWindows requires layout \"side-by-side\"; SummarizeContext requires contextRef synthetic.exposure.vega and mode \"private\". " +
        "Produce exactly six steps in this order: LaunchApp sample.watchlist, LaunchApp sample.risk, LaunchApp sample.chart, " +
        "PublishContext with contextRef \"synthetic.instrument.vega\", ArrangeWindows with layout \"side-by-side\", " +
        "SummarizeContext with contextRef \"synthetic.exposure.vega\" and mode \"private\". No executable paths, URLs, shell commands, prose or markdown fences; " +
        "answer with JSON only.";

    private readonly ICloudModelTransport _transport;
    private readonly IEgressGateway _egressGateway;
    private readonly PlanProviderOptions _options;

    public CloudPlanProvider(ICloudModelTransport transport, IEgressGateway egressGateway, PlanProviderOptions options)
    {
        _transport = transport ?? throw new ArgumentNullException(nameof(transport));
        _egressGateway = egressGateway ?? throw new ArgumentNullException(nameof(egressGateway));
        _options = options ?? throw new ArgumentNullException(nameof(options));
    }

    public string ProviderId => "cloud-plan";

    public async Task<PlanGenerationResult> GeneratePlanAsync(PlanningRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var payloadBytes = System.Text.Encoding.UTF8.GetByteCount(request.Command);
        var egressRequest = new EgressRequest(
            request.TraceId,
            _transport.ProviderId,
            AiProviderKind.Cloud,
            AiTaskKind.PlanGeneration,
            request.Classification,
            _options.CloudEnabled,
            _options.CloudConsentGranted,
            SanitizationReviewed: false,
            PayloadKind: "command",
            PayloadBytes: payloadBytes);

        var outcome = await _egressGateway.SendAsync(
            egressRequest,
            token => _transport.SendAsync(
                new CloudModelRequest(
                    request.TraceId,
                    AiTaskKind.PlanGeneration,
                    request.Classification,
                    "command",
                    payloadBytes,
                    PlannerPrompt,
                    request.Command),
                token),
            cancellationToken).ConfigureAwait(false);

        if (!outcome.Sent)
        {
            return PlanGenerationResult.Fail(
                string.IsNullOrEmpty(outcome.Decision.Code) ? AetherErrorCodes.EgressDenied : outcome.Decision.Code,
                outcome.Decision.Explanation,
                ProviderId);
        }

        if (outcome.ErrorCode is not null || outcome.Value is null || !outcome.Value.Success)
        {
            return PlanGenerationResult.Fail(
                outcome.Value?.ErrorCode ?? outcome.ErrorCode ?? AetherErrorCodes.AiProviderUnavailable,
                "The cloud planning provider did not return a usable response.",
                ProviderId);
        }

        if (!TryExtractPlan(outcome.Value.Content, out var plan, out var parseError))
        {
            return PlanGenerationResult.Fail(AetherErrorCodes.AiOutputInvalid, parseError!, ProviderId);
        }

        return PlanGenerationResult.Ok(plan!, ProviderId, usedModel: true);
    }

    internal static bool TryExtractPlan(string? content, out ActionPlan? plan, out string? error)
    {
        plan = null;
        if (string.IsNullOrWhiteSpace(content))
        {
            error = "The provider returned an empty response.";
            return false;
        }

        var candidate = content.Trim();
        var start = candidate.IndexOf('{', StringComparison.Ordinal);
        var end = candidate.LastIndexOf('}');
        if (start < 0 || end <= start)
        {
            error = "The provider response does not contain a JSON object.";
            return false;
        }

        candidate = candidate[start..(end + 1)];
        try
        {
            using var document = JsonDocument.Parse(candidate, new JsonDocumentOptions { MaxDepth = AetherJson.MaxDocumentDepth });
            var planElement = document.RootElement;
            if (planElement.TryGetProperty("plan", out var nested) && nested.ValueKind == JsonValueKind.Object)
            {
                planElement = nested;
            }

            plan = AetherJson.DeserializeStrict<ActionPlan>(planElement.GetRawText());
            error = null;
            return true;
        }
        catch (JsonException exception)
        {
            error = $"The provider response is not valid JSON: {exception.Message}";
            return false;
        }
        catch (AetherContractException exception)
        {
            error = exception.Message;
            return false;
        }
    }
}
