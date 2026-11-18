using Aether.Contracts;
using Aether.Contracts.Errors;

namespace Aether.Security.Policy;

public sealed record PolicyRequest(
    DataClassification Classification,
    AiProviderKind ProviderKind,
    AiTaskKind TaskKind,
    bool CloudEnabled,
    bool CloudConsentGranted,
    bool SanitizationReviewed = false);

public sealed record PolicyDecision(bool Allowed, string Code, string Explanation, string PolicyVersion)
{
    public static PolicyDecision Allow(string policyVersion) => new(true, "", "", policyVersion);

    public static PolicyDecision Deny(string code, string explanation, string policyVersion) =>
        new(false, code, explanation, policyVersion);
}

public interface IPolicyEngine
{
    PolicyDocument Document { get; }

    PolicyDecision Evaluate(PolicyRequest request);
}

/// <summary>
/// Deny-by-default policy engine. The cloud path requires an enabled cloud
/// mode, explicit consent and an appropriate classification rule. UNKNOWN and
/// RESTRICTED never reach cloud providers; failed/ambiguous policy state denies.
/// </summary>
public sealed class PolicyEngine : IPolicyEngine
{
    /// <summary>
    /// v1.4 ADR-011 hard gate: sanitized INTERNAL cloud routing is rejected for the v1.x line.
    /// A policy document flag cannot enable it; re-enabling requires a superseding ADR and the
    /// full classifier/vault/egress/de-mask review described there.
    /// </summary>
    public const bool SanitizedInternalCloudRoutingEnabled = false;

    public PolicyEngine(PolicyDocument? document = null)
    {
        Document = (document ?? PolicyDocument.CreateDefault()).EnsureValid();
    }

    public PolicyDocument Document { get; }

    public PolicyDecision Evaluate(PolicyRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        var policyVersion = Document.PolicyVersion;

        if (request.ProviderKind == AiProviderKind.Local)
        {
            // Private/local models are permitted for every classification,
            // including UNKNOWN (treated as RESTRICTED: private only).
            return PolicyDecision.Allow(policyVersion);
        }

        if (request.ProviderKind != AiProviderKind.Cloud)
        {
            return PolicyDecision.Deny(
                AetherErrorCodes.PolicyCloudDenied,
                "Unsupported provider category; the request was denied (fail-closed).",
                policyVersion);
        }

        var cloud = Document.Cloud;
        if (!cloud.EnabledByDefault && !request.CloudEnabled)
        {
            return PolicyDecision.Deny(
                AetherErrorCodes.PolicyCloudDisabled,
                "Cloud mode is disabled. Enable it explicitly in the workspace settings.",
                policyVersion);
        }

        if (cloud.RequireExplicitConsent && !request.CloudConsentGranted)
        {
            return PolicyDecision.Deny(
                AetherErrorCodes.PolicyCloudConsentRequired,
                "Cloud processing requires explicit user consent.",
                policyVersion);
        }

        var classificationName = request.Classification.ToWireName();
        if (!Document.DataClasses.TryGetValue(classificationName, out var rule) || rule is null)
        {
            return PolicyDecision.Deny(
                AetherErrorCodes.PolicyCloudDenied,
                "No policy rule matches this data classification; the request was denied.",
                policyVersion);
        }

        var cloudRule = rule.Cloud;
        if (request.Classification == DataClassification.Unknown)
        {
            return PolicyDecision.Deny(
                AetherErrorCodes.PolicyClassificationUnknown,
                "Data classification is unknown. Cloud processing is denied and the data is treated as restricted.",
                policyVersion);
        }

        return cloudRule switch
        {
            PolicyWireNames.OptIn when request.Classification == DataClassification.Public =>
                PolicyDecision.Allow(policyVersion),
            PolicyWireNames.OptIn => PolicyDecision.Deny(
                AetherErrorCodes.PolicyCloudDenied,
                "Cloud mode does not permit this classification.",
                policyVersion),
            PolicyWireNames.DenyByDefault when SanitizedInternalCloudRoutingEnabled && Document.Cloud.AllowSanitizedInternal && request.SanitizationReviewed =>
                PolicyDecision.Allow(policyVersion),
            PolicyWireNames.DenyByDefault => PolicyDecision.Deny(
                AetherErrorCodes.PolicyCloudDeniedByDefault,
                "This data classification is denied for cloud processing by default.",
                policyVersion),
            PolicyWireNames.Deny => PolicyDecision.Deny(
                AetherErrorCodes.PolicyCloudDenied,
                "This data classification is restricted to private processing.",
                policyVersion),
            _ => PolicyDecision.Deny(
                AetherErrorCodes.PolicyCloudDenied,
                "Unrecognised cloud policy rule; the request was denied (fail-closed).",
                policyVersion),
        };
    }
}
