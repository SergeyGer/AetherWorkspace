using Aether.Contracts;
using Aether.Contracts.Capabilities;
using Aether.Contracts.Errors;
using Aether.Contracts.Models;
using Aether.Execution.Authorization;
using Aether.Execution.Catalog;
using Aether.Testing;
using Xunit;

namespace Aether.Execution.Tests;

public class AuthorizationTests
{
    private static readonly CapabilityPlanAuthorizationPolicy Policy = new();
    private static readonly Aether.Execution.Catalog.PlanCatalog Catalog = TestData.CreatePlanCatalog();
    private static readonly CapabilityGrant FullGrant = TestData.OperatorGrant();
    private static readonly AuthorizationContext Context = new(FullGrant, Catalog);

    private static CapabilityGrant GrantWithCapabilities(params string[] capabilities) => FullGrant with
    {
        Capabilities = new HashSet<string>(capabilities, StringComparer.Ordinal),
    };

    [Fact]
    public void PlanExecutionRequiresPlanCapability()
    {
        Assert.True(Policy.AuthorizePlan(FullGrant).Authorized);
        Assert.False(Policy.AuthorizePlan(GrantWithCapabilities(CapabilityNames.ContextRead)).Authorized);
    }

    [Fact]
    public void LaunchRequiresBrokerLaunchCapability()
    {
        var step = new ActionPlanStep { Action = ActionKinds.LaunchApp, AppId = "sample.watchlist" };
        Assert.True(Policy.AuthorizeStep(0, step, Context).Authorized);

        var restricted = new AuthorizationContext(GrantWithCapabilities(CapabilityNames.PlanExecute), Catalog);
        var decision = Policy.AuthorizeStep(0, step, restricted);
        Assert.False(decision.Authorized);
        Assert.Equal(AetherErrorCodes.AuthCapabilityMissing, decision.Code);
    }

    [Fact]
    public void PublishRequiresCapabilityChannelTypeAndClassificationClearance()
    {
        var step = new ActionPlanStep { Action = ActionKinds.PublishContext, ContextRef = "synthetic.instrument.northstar" };
        Assert.True(Policy.AuthorizeStep(0, step, Context).Authorized);

        var noPublish = new AuthorizationContext(GrantWithCapabilities(CapabilityNames.PlanExecute, CapabilityNames.BrokerLaunch), Catalog);
        Assert.Equal(AetherErrorCodes.AuthCapabilityMissing, Policy.AuthorizeStep(0, step, noPublish).Code);

        var noChannel = new AuthorizationContext(FullGrant with
        {
            AllowedChannels = new HashSet<string>(StringComparer.Ordinal),
        }, Catalog);
        Assert.Equal(AetherErrorCodes.AuthChannelDenied, Policy.AuthorizeStep(0, step, noChannel).Code);

        var noRestricted = new AuthorizationContext(FullGrant with
        {
            AllowedClassifications = new HashSet<DataClassification> { DataClassification.Public },
        }, Catalog);
        Assert.Equal(AetherErrorCodes.AuthClassificationDenied, Policy.AuthorizeStep(0, step, noRestricted).Code);
    }

    [Fact]
    public void SummarizeRequiresContextReadCapability()
    {
        var step = new ActionPlanStep { Action = ActionKinds.SummarizeContext, ContextRef = "synthetic.exposure.northstar", Mode = SummarizeModes.Private };
        Assert.True(Policy.AuthorizeStep(0, step, Context).Authorized);

        var withoutRead = new AuthorizationContext(FullGrant with
        {
            Capabilities = new HashSet<string>(StringComparer.Ordinal) { CapabilityNames.PlanExecute, CapabilityNames.BrokerLaunch, CapabilityNames.ContextPublish, CapabilityNames.BrokerArrange },
        }, Catalog);
        Assert.Equal(AetherErrorCodes.AuthCapabilityMissing, Policy.AuthorizeStep(0, step, withoutRead).Code);
    }

    [Fact]
    public void ArrangeRequiresBrokerArrangeCapability()
    {
        var step = new ActionPlanStep { Action = ActionKinds.ArrangeWindows, Layout = Layouts.SideBySide };
        Assert.True(Policy.AuthorizeStep(0, step, Context).Authorized);

        var withoutArrange = new AuthorizationContext(GrantWithCapabilities(CapabilityNames.PlanExecute), Catalog);
        Assert.Equal(AetherErrorCodes.AuthCapabilityMissing, Policy.AuthorizeStep(0, step, withoutArrange).Code);
    }

    [Fact]
    public void ShowStatusNeedsNoExtraCapability()
    {
        var step = new ActionPlanStep { Action = ActionKinds.ShowStatus, StatusText = "hi" };
        Assert.True(Policy.AuthorizeStep(0, step, Context).Authorized);
    }

    [Fact]
    public void UnknownActionsAreDenied()
    {
        var step = new ActionPlanStep { Action = "ExecShell" };
        var decision = Policy.AuthorizeStep(0, step, Context);
        Assert.False(decision.Authorized);
        Assert.Equal(AetherErrorCodes.PlanActionUnknown, decision.Code);
    }
}
