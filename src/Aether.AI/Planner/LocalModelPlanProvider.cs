using Aether.AI.Providers;
using Aether.Contracts;
using Aether.Contracts.Errors;
using Aether.Security.Egress;
using Aether.Security.Policy;

namespace Aether.AI.Planner;

/// <summary>
/// v1.3 local-model planning adapter. It uses the same bounded planning prompt and strict plan
/// parser as the cloud adapter, but calls only the configured loopback transport and passes through
/// the egress gateway as a local (non-cloud) attempt. A missing local model fails with a provider
/// error; it never falls through to a cloud transport.
/// </summary>
public sealed class LocalModelPlanProvider : IPlanGenerator
{
    private readonly ILocalModelTransport _transport;
    private readonly IEgressGateway _egressGateway;

    public LocalModelPlanProvider(ILocalModelTransport transport, IEgressGateway egressGateway)
    {
        _transport = transport ?? throw new ArgumentNullException(nameof(transport));
        _egressGateway = egressGateway ?? throw new ArgumentNullException(nameof(egressGateway));
    }

    public string ProviderId => "local-plan";

    public async Task<PlanGenerationResult> GeneratePlanAsync(PlanningRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var payloadBytes = System.Text.Encoding.UTF8.GetByteCount(request.Command);
        var outcome = await _egressGateway.SendAsync(
            new EgressRequest(
                request.TraceId,
                _transport.ProviderId,
                AiProviderKind.Local,
                AiTaskKind.PlanGeneration,
                request.Classification,
                CloudEnabled: false,
                CloudConsentGranted: false,
                SanitizationReviewed: false,
                PayloadKind: "command",
                PayloadBytes: payloadBytes),
            token => _transport.SendAsync(
                new CloudModelRequest(
                    request.TraceId,
                    AiTaskKind.PlanGeneration,
                    request.Classification,
                    "command",
                    payloadBytes,
                    CloudPlanProvider.PlannerPrompt,
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
                "The local model provider did not return a usable plan. Install and configure the supported local profile, or use the deterministic planner.",
                ProviderId);
        }

        if (!CloudPlanProvider.TryExtractPlan(outcome.Value.Content, out var plan, out var parseError))
        {
            return PlanGenerationResult.Fail(AetherErrorCodes.AiOutputInvalid, parseError!, ProviderId);
        }

        return PlanGenerationResult.Ok(plan!, ProviderId, usedModel: true);
    }
}
