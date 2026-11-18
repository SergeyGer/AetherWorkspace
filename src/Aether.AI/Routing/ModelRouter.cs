using Aether.AI.Providers;
using Aether.Contracts;

namespace Aether.AI.Routing;

public enum ModelRouteKind
{
    DeterministicLocal = 0,
    LocalModel = 1,
    Cloud = 2,
    Denied = 3,
}

/// <summary>
/// Explicit, testable route decision for one AI task. Cloud attempts are only
/// allowed for explicitly permitted classifications, enabled cloud mode and
/// recorded consent; INTERNAL/RESTRICTED/UNKNOWN never fall back to cloud.
/// </summary>
public sealed record ModelRoute(
    ModelRouteKind Kind,
    string ReasonCode,
    bool CloudAttemptAllowed,
    string? ProviderId,
    string? Model);

public static class ModelRouter
{
    public const string ReasonPublicCloudAllowed = "public.cloud_opt_in";
    public const string ReasonCloudDisabled = "policy.cloud_disabled";
    public const string ReasonConsentMissing = "policy.consent_missing";
    public const string ReasonCloudNotConfigured = "provider.cloud_not_configured";
    public const string ReasonLocalModel = "private.local_model";
    public const string ReasonDeterministic = "private.deterministic";
    public const string ReasonProviderConfigInvalid = "provider.config_invalid";

    public static ModelRoute Decide(
        DataClassification classification,
        bool cloudEnabled,
        bool cloudConsentGranted,
        bool cloudConfigured,
        bool localModelConfigured,
        ModelProviderSettings? provider = null)
    {
        var cloudCapable = classification == DataClassification.Public;

        if (provider?.ConfigurationError is not null)
        {
            return Fallback(localModelConfigured, ReasonProviderConfigInvalid);
        }

        if (!cloudCapable)
        {
            return localModelConfigured
                ? new ModelRoute(ModelRouteKind.LocalModel, ReasonLocalModel, false, "local-loopback", provider?.ProviderId == "local" ? provider.Model : null)
                : new ModelRoute(ModelRouteKind.DeterministicLocal, ReasonDeterministic, false, null, null);
        }

        if (!cloudEnabled)
        {
            return Fallback(localModelConfigured, ReasonCloudDisabled);
        }

        if (!cloudConsentGranted)
        {
            return Fallback(localModelConfigured, ReasonConsentMissing);
        }

        if (!cloudConfigured || provider is null || !provider.IsConfigured)
        {
            return Fallback(localModelConfigured, ReasonCloudNotConfigured);
        }

        return new ModelRoute(ModelRouteKind.Cloud, ReasonPublicCloudAllowed, true, provider.ProviderId, provider.Model);
    }

    private static ModelRoute Fallback(bool localModelConfigured, string reason) =>
        localModelConfigured
            ? new ModelRoute(ModelRouteKind.LocalModel, reason, false, "local-loopback", null)
            : new ModelRoute(ModelRouteKind.DeterministicLocal, reason, false, null, null);
}
