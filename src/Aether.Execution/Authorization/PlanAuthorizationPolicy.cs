using Aether.Contracts.Capabilities;
using Aether.Contracts.Errors;
using Aether.Contracts.Models;
using Aether.Execution.Catalog;

namespace Aether.Execution.Authorization;

public sealed record StepAuthorizationResult(bool Authorized, string? Code, string? Message)
{
    public static StepAuthorizationResult Allow() => new(true, null, null);

    public static StepAuthorizationResult Deny(string code, string message) => new(false, code, message);
}

public sealed record AuthorizationContext(CapabilityGrant Grant, PlanCatalog Catalog);

/// <summary>
/// Capability authorization applied to every plan step. Denials are structured
/// and redacted; a denial never falls through to a higher-privilege path.
/// </summary>
public interface IPlanAuthorizationPolicy
{
    StepAuthorizationResult AuthorizePlan(CapabilityGrant grant);

    StepAuthorizationResult AuthorizeStep(int stepIndex, ActionPlanStep step, AuthorizationContext context);
}

public sealed class CapabilityPlanAuthorizationPolicy : IPlanAuthorizationPolicy
{
    public StepAuthorizationResult AuthorizePlan(CapabilityGrant grant)
    {
        ArgumentNullException.ThrowIfNull(grant);

        return grant.HasCapability(CapabilityNames.PlanExecute)
            ? StepAuthorizationResult.Allow()
            : StepAuthorizationResult.Deny(
                AetherErrorCodes.AuthCapabilityMissing,
                "The current session is not allowed to execute action plans.");
    }

    public StepAuthorizationResult AuthorizeStep(int stepIndex, ActionPlanStep step, AuthorizationContext context)
    {
        ArgumentNullException.ThrowIfNull(step);
        ArgumentNullException.ThrowIfNull(context);

        if (!ActionKinds.TryParse(step.Action, out var kind))
        {
            return StepAuthorizationResult.Deny(
                AetherErrorCodes.PlanActionUnknown,
                "The declared action is not supported.");
        }

        var grant = context.Grant;
        switch (kind)
        {
            case ActionKind.LaunchApp:
                return RequireCapability(grant, CapabilityNames.BrokerLaunch, "launch applications");
            case ActionKind.ArrangeWindows:
                return RequireCapability(grant, CapabilityNames.BrokerArrange, "arrange windows");
            case ActionKind.PublishContext:
                return AuthorizePublish(step, context);
            case ActionKind.SummarizeContext:
                return AuthorizeSummarize(step, context);
            case ActionKind.ShowStatus:
                return StepAuthorizationResult.Allow();
            default:
                return StepAuthorizationResult.Deny(
                    AetherErrorCodes.PlanActionUnknown,
                    "The declared action is not supported.");
        }
    }

    private static StepAuthorizationResult AuthorizePublish(ActionPlanStep step, AuthorizationContext context)
    {
        var grant = context.Grant;
        var capability = RequireCapability(grant, CapabilityNames.ContextPublish, "publish context");
        if (!capability.Authorized)
        {
            return capability;
        }

        if (!grant.AllowedChannels.Contains(ChannelNames.Default))
        {
            return StepAuthorizationResult.Deny(
                AetherErrorCodes.AuthChannelDenied,
                "The current session is not allowed to publish to the workspace channel.");
        }

        if (step.ContextRef is null || !context.Catalog.Contexts.TryGetValue(step.ContextRef, out var entry))
        {
            return StepAuthorizationResult.Deny(
                AetherErrorCodes.PlanContextUnknown,
                "The referenced context is not present in the trusted catalog.");
        }

        if (!grant.AllowedContextTypes.Contains(entry.ContextType))
        {
            return StepAuthorizationResult.Deny(
                AetherErrorCodes.AuthContextTypeDenied,
                "The current session is not allowed to publish this context type.");
        }

        return grant.AllowedClassifications.Contains(entry.Classification)
            ? StepAuthorizationResult.Allow()
            : StepAuthorizationResult.Deny(
                AetherErrorCodes.AuthClassificationDenied,
                "The current session is not cleared for the classification of this context.");
    }

    private static StepAuthorizationResult AuthorizeSummarize(ActionPlanStep step, AuthorizationContext context)
    {
        var grant = context.Grant;
        var capability = RequireCapability(grant, CapabilityNames.ContextRead, "summarize context");
        if (!capability.Authorized)
        {
            return capability;
        }

        if (step.ContextRef is null || !context.Catalog.Contexts.TryGetValue(step.ContextRef, out var entry))
        {
            return StepAuthorizationResult.Deny(
                AetherErrorCodes.PlanContextUnknown,
                "The referenced context is not present in the trusted catalog.");
        }

        return grant.AllowedClassifications.Contains(entry.Classification)
            ? StepAuthorizationResult.Allow()
            : StepAuthorizationResult.Deny(
                AetherErrorCodes.AuthClassificationDenied,
                "The current session is not cleared for the classification of this context.");
    }

    private static StepAuthorizationResult RequireCapability(CapabilityGrant grant, string capability, string operation) =>
        grant.HasCapability(capability)
            ? StepAuthorizationResult.Allow()
            : StepAuthorizationResult.Deny(
                AetherErrorCodes.AuthCapabilityMissing,
                $"The current session is not allowed to {operation}.");
}
