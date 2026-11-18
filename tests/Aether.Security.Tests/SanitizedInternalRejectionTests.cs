using Aether.Contracts;
using Aether.Contracts.Errors;
using Aether.Security.Audit;
using Aether.Security.Egress;
using Aether.Security.Policy;
using Aether.Testing;
using Xunit;

namespace Aether.Security.Tests;

/// <summary>v1.4 AW-061/AW-062: ADR-011 rejects sanitized INTERNAL cloud routing; PUBLIC-only default.</summary>
public sealed class SanitizedInternalRejectionTests
{
    [Fact]
    public void PolicyEngineHardDisablesSanitizedInternalCloudRouting()
    {
        Assert.False(PolicyEngine.SanitizedInternalCloudRoutingEnabled);
        Assert.False(PolicyDocument.CreateDefault().Cloud.AllowSanitizedInternal);
    }

    [Fact]
    public void InternalIsDeniedByDefaultEvenWithSanitizationReviewClaimed()
    {
        var policyEngine = new PolicyEngine(PolicyDocument.CreateDefault());
        var decision = policyEngine.Evaluate(new PolicyRequest(
            DataClassification.Internal,
            AiProviderKind.Cloud,
            AiTaskKind.PlanGeneration,
            CloudEnabled: true,
            CloudConsentGranted: true,
            SanitizationReviewed: true));

        Assert.False(decision.Allowed);
        Assert.Equal(AetherErrorCodes.PolicyCloudDeniedByDefault, decision.Code);
    }

    [Fact]
    public void CustomPolicyFlagCannotEnableTheRejectedRoute()
    {
        var document = PolicyDocument.CreateDefault();
        document = new PolicyDocument
        {
            PolicyVersion = document.PolicyVersion,
            DataClasses = document.DataClasses,
            Cloud = new PolicyDocument.CloudPolicy
            {
                EnabledByDefault = true,
                RequireExplicitConsent = true,
                AllowSanitizedInternal = true,
            },
        }.EnsureValid();
        var policyEngine = new PolicyEngine(document);

        var decision = policyEngine.Evaluate(new PolicyRequest(
            DataClassification.Internal,
            AiProviderKind.Cloud,
            AiTaskKind.Summarization,
            CloudEnabled: true,
            CloudConsentGranted: true,
            SanitizationReviewed: true));

        Assert.False(decision.Allowed);
        Assert.Equal(AetherErrorCodes.PolicyCloudDeniedByDefault, decision.Code);
    }

    [Fact]
    public async Task EgressGatewayNeverInvokesTheTransportForSanitizedInternal()
    {
        var policy = PolicyDocument.CreateDefault();
        var document = new PolicyDocument
        {
            PolicyVersion = policy.PolicyVersion,
            DataClasses = policy.DataClasses,
            Cloud = new PolicyDocument.CloudPolicy { AllowSanitizedInternal = true },
        };
        var gateway = new EgressGateway(new PolicyEngine(document), new RecordingAuditSink());
        var calls = 0;

        var outcome = await gateway.SendAsync(
            new EgressRequest(
                "trace-sanitized-internal",
                "cloud-openai-compatible",
                AiProviderKind.Cloud,
                AiTaskKind.Summarization,
                DataClassification.Internal,
                CloudEnabled: true,
                CloudConsentGranted: true,
                SanitizationReviewed: true,
                PayloadKind: "context",
                PayloadBytes: 128),
            _ =>
            {
                calls++;
                return Task.FromResult("must-not-be-called");
            });

        Assert.False(outcome.Sent);
        Assert.Equal(0, calls);
        Assert.Equal(AetherErrorCodes.PolicyCloudDeniedByDefault, outcome.Decision.Code);
    }

    [Theory]
    [InlineData(DataClassification.Restricted)]
    [InlineData(DataClassification.Unknown)]
    public async Task RestrictedAndUnknownNeverReachCloudTransport(DataClassification classification)
    {
        var gateway = new EgressGateway(new PolicyEngine(PolicyDocument.CreateDefault()));
        var calls = 0;

        var outcome = await gateway.SendAsync(
            new EgressRequest(
                "trace-private",
                "cloud-openai-compatible",
                AiProviderKind.Cloud,
                AiTaskKind.PlanGeneration,
                classification,
                CloudEnabled: true,
                CloudConsentGranted: true,
                SanitizationReviewed: true,
                PayloadKind: "command",
                PayloadBytes: 64),
            _ =>
            {
                calls++;
                return Task.FromResult("must-not-be-called");
            });

        Assert.False(outcome.Sent);
        Assert.Equal(0, calls);
    }
}
