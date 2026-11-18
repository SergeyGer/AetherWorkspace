using Aether.Contracts;
using Aether.Contracts.Errors;
using Aether.Security.Policy;
using Xunit;

namespace Aether.Security.Tests;

public class PolicyEngineTests
{
    private static readonly PolicyEngine Engine = new(PolicyDocument.CreateDefault());

    private static PolicyRequest Cloud(DataClassification classification, bool enabled = true, bool consent = true, bool sanitized = false) =>
        new(classification, AiProviderKind.Cloud, AiTaskKind.Summarization, enabled, consent, sanitized);

    [Theory]
    [InlineData(DataClassification.Public)]
    [InlineData(DataClassification.Internal)]
    [InlineData(DataClassification.Restricted)]
    [InlineData(DataClassification.Unknown)]
    public void LocalProviderIsAllowedForAllClassifications(DataClassification classification)
    {
        var decision = Engine.Evaluate(new PolicyRequest(classification, AiProviderKind.Local, AiTaskKind.Summarization, false, false));
        Assert.True(decision.Allowed);
    }

    [Fact]
    public void CloudIsDisabledByDefault()
    {
        var decision = Engine.Evaluate(new PolicyRequest(
            DataClassification.Public, AiProviderKind.Cloud, AiTaskKind.PlanGeneration, CloudEnabled: false, CloudConsentGranted: true));

        Assert.False(decision.Allowed);
        Assert.Equal(AetherErrorCodes.PolicyCloudDisabled, decision.Code);
    }

    [Fact]
    public void CloudRequiresExplicitConsent()
    {
        var decision = Engine.Evaluate(Cloud(DataClassification.Public, consent: false));

        Assert.False(decision.Allowed);
        Assert.Equal(AetherErrorCodes.PolicyCloudConsentRequired, decision.Code);
    }

    [Fact]
    public void PublicDataIsAllowedForCloudWithConsent()
    {
        var decision = Engine.Evaluate(Cloud(DataClassification.Public));
        Assert.True(decision.Allowed);
    }

    [Fact]
    public void InternalDataIsDeniedByDefaultForCloud()
    {
        var decision = Engine.Evaluate(Cloud(DataClassification.Internal));

        Assert.False(decision.Allowed);
        Assert.Equal(AetherErrorCodes.PolicyCloudDeniedByDefault, decision.Code);
    }

    [Fact]
    public void SanitizedInternalIsRejectedEvenWithExplicitReviewFlagAndPolicyOptIn()
    {
        // v1.4 ADR-010: sanitized INTERNAL cloud routing is rejected for the v1.x line.
        Assert.False(Engine.Evaluate(Cloud(DataClassification.Internal, sanitized: true)).Allowed);

        var permissive = PolicyDocument.CreateDefault();
        permissive = new PolicyDocument
        {
            Cloud = new PolicyDocument.CloudPolicy { AllowSanitizedInternal = true, RequireExplicitConsent = true },
            DataClasses = permissive.DataClasses,
        };
        var reviewEngine = new PolicyEngine(permissive);

        Assert.False(reviewEngine.Evaluate(Cloud(DataClassification.Internal, sanitized: false)).Allowed);
        var reviewed = reviewEngine.Evaluate(Cloud(DataClassification.Internal, sanitized: true));
        Assert.False(reviewed.Allowed);
        Assert.Equal(AetherErrorCodes.PolicyCloudDeniedByDefault, reviewed.Code);
    }

    [Fact]
    public void RestrictedDataIsNeverAllowedForCloudEvenWithConsent()
    {
        var decision = Engine.Evaluate(Cloud(DataClassification.Restricted));

        Assert.False(decision.Allowed);
        Assert.Equal(AetherErrorCodes.PolicyCloudDenied, decision.Code);
    }

    [Fact]
    public void UnknownAlwaysDeniesCloud()
    {
        foreach (var enabled in new[] { true, false })
        {
            foreach (var consent in new[] { true, false })
            {
                foreach (var sanitized in new[] { true, false })
                {
                    var decision = Engine.Evaluate(Cloud(DataClassification.Unknown, enabled, consent, sanitized));
                    Assert.False(decision.Allowed);
                }
            }
        }

        var enabledWithConsent = Engine.Evaluate(Cloud(DataClassification.Unknown, enabled: true, consent: true));
        Assert.Equal(AetherErrorCodes.PolicyClassificationUnknown, enabledWithConsent.Code);
    }

    [Fact]
    public void SanitizedPermitCannotUnlockRestrictedOrUnknown()
    {
        var permissive = new PolicyDocument
        {
            Cloud = new PolicyDocument.CloudPolicy { AllowSanitizedInternal = true },
            DataClasses = PolicyDocument.CreateDefault().DataClasses,
        };
        var engine = new PolicyEngine(permissive);

        Assert.False(engine.Evaluate(Cloud(DataClassification.Restricted, sanitized: true)).Allowed);
        Assert.False(engine.Evaluate(Cloud(DataClassification.Unknown, sanitized: true)).Allowed);
    }

    [Fact]
    public void InvalidFailureModeRejectsTheWholePolicy()
    {
        var invalid = new PolicyDocument { FailureMode = "BEST_EFFORT" };
        var exception = Assert.Throws<AetherContractException>(() => new PolicyEngine(invalid));
        Assert.Equal(AetherErrorCodes.PolicyInvalidDocument, exception.Code);
    }

    [Fact]
    public void MalformedCloudRuleRejectsThePolicy()
    {
        var document = PolicyDocument.CreateDefault();
        document.DataClasses["RESTRICTED"] = new PolicyDocument.DataClassRule { Cloud = "ALLOW" };
        Assert.Throws<AetherContractException>(() => new PolicyEngine(document));
    }

    [Fact]
    public void UnsupportedProviderCategoryFailsClosed()
    {
        var decision = Engine.Evaluate(new PolicyRequest(
            DataClassification.Public, (AiProviderKind)99, AiTaskKind.Summarization, true, true));

        Assert.False(decision.Allowed);
    }
}
