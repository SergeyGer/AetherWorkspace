using Aether.Contracts.Errors;
using Xunit;

namespace Aether.Contracts.Tests;

public class ErrorCodeTests
{
    [Theory]
    [InlineData(AetherErrorCodes.PlanAppUnknown, DenialCategory.UnauthorizedApp)]
    [InlineData(AetherErrorCodes.PlanActionUnknown, DenialCategory.UnsupportedAction)]
    [InlineData(AetherErrorCodes.PlanStepsTooMany, DenialCategory.InvalidPlan)]
    [InlineData(AetherErrorCodes.PlanUnsafeContent, DenialCategory.InputRejected)]
    [InlineData(AetherErrorCodes.AuthCapabilityMissing, DenialCategory.CapabilityMissing)]
    [InlineData(AetherErrorCodes.AuthClassificationDenied, DenialCategory.UnsafeClassification)]
    [InlineData(AetherErrorCodes.PolicyClassificationUnknown, DenialCategory.UnsafeClassification)]
    [InlineData(AetherErrorCodes.AiProviderUnavailable, DenialCategory.ProviderUnavailable)]
    [InlineData(AetherErrorCodes.ExecutionDeadlineExceeded, DenialCategory.InternalError)]
    public void ErrorCodesMapToStableDenialCategories(string code, DenialCategory expected)
    {
        Assert.Equal(expected, AetherErrorCodes.GetDenialCategory(code));
    }

    [Fact]
    public void UnknownCodeMapsToUnknownCategory()
    {
        Assert.Equal(DenialCategory.Unknown, AetherErrorCodes.GetDenialCategory(null));
        Assert.Equal(DenialCategory.Unknown, AetherErrorCodes.GetDenialCategory("something.unexpected"));
    }
}
