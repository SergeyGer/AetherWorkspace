using Aether.AI.Providers;
using Aether.AI.Routing;
using Aether.Contracts;
using Xunit;

namespace Aether.AI.Tests;

/// <summary>v1.3 AW-059: per-capability provider selection and fail-closed validation.</summary>
public sealed class ProviderConfigurationTests
{
    [Fact]
    public void PlannerAndSummaryProvidersAreSelectedIndependently()
    {
        var catalog = ModelProviderCatalog.FromEnvironment(new Dictionary<string, string>
        {
            ["AETHER_PLANNER_PROVIDER"] = "local",
            ["AETHER_LOCAL_MODEL_ENDPOINT"] = "http://127.0.0.1:11434/v1/chat/completions",
            ["AETHER_LOCAL_MODEL"] = "qwen2.5:7b",
            ["AETHER_SUMMARY_PROVIDER"] = "gemini",
            ["GEMINI_API_KEY"] = "synthetic-key",
            ["GEMINI_BASE_URL"] = "https://generativelanguage.googleapis.com/v1beta",
            ["GEMINI_MODEL"] = "gemini-3.1-flash-lite",
        });

        Assert.Equal("local", catalog.Planner.ProviderId);
        Assert.False(catalog.Planner.IsCloud);
        Assert.True(catalog.Planner.IsValid);
        Assert.Equal("gemini", catalog.Summarization.ProviderId);
        Assert.True(catalog.Summarization.IsCloud);
        Assert.True(catalog.Summarization.IsValid);
        Assert.Null(catalog.ConfigurationError);
    }

    [Fact]
    public void GenericProviderVariableIsAFallbackForBothCapabilities()
    {
        var catalog = ModelProviderCatalog.FromEnvironment(new Dictionary<string, string>
        {
            ["AETHER_MODEL_PROVIDER"] = "deterministic",
        });

        Assert.Equal("deterministic", catalog.Planner.ProviderId);
        Assert.Equal("deterministic", catalog.Summarization.ProviderId);
        Assert.True(catalog.Planner.IsValid);
        Assert.Equal(ModelRouteKind.DeterministicLocal,
            ModelRouter.Decide(DataClassification.Public, true, true, false, false, catalog.Planner).Kind);
    }

    [Fact]
    public void UnsupportedProviderNameIsAConfigurationErrorAndIsNotSilentlyReplaced()
    {
        var catalog = ModelProviderCatalog.FromEnvironment(new Dictionary<string, string>
        {
            ["AETHER_PLANNER_PROVIDER"] = "some-brand-xyz",
        });

        Assert.True(catalog.HasConfigurationError);
        Assert.Contains("Unsupported provider", catalog.ConfigurationError, StringComparison.Ordinal);
        var route = ModelRouter.Decide(DataClassification.Public, true, true, false, false, catalog.Planner);
        Assert.Equal(ModelRouteKind.DeterministicLocal, route.Kind);
        Assert.Equal(ModelRouter.ReasonProviderConfigInvalid, route.ReasonCode);
    }

    [Fact]
    public void NonLoopbackLocalEndpointIsRejected()
    {
        var settings = ModelProviderSettings.FromEnvironment(new Dictionary<string, string>
        {
            ["AETHER_PLANNER_PROVIDER"] = "local",
            ["AETHER_LOCAL_MODEL_ENDPOINT"] = "https://example.invalid/v1/chat/completions",
        });

        Assert.Contains("loopback-only", settings.ConfigurationError, StringComparison.Ordinal);
    }

    [Fact]
    public void CloudEndpointMustUseHttpsUnlessLoopback()
    {
        var settings = ModelProviderSettings.FromEnvironment(new Dictionary<string, string>
        {
            ["AETHER_PLANNER_PROVIDER"] = "openai",
            ["OPENAI_BASE_URL"] = "http://example.invalid/v1/chat/completions",
            ["OPENAI_API_KEY"] = "synthetic",
        });

        Assert.Contains("HTTPS", settings.ConfigurationError, StringComparison.Ordinal);

        var loopback = ModelProviderSettings.FromEnvironment(new Dictionary<string, string>
        {
            ["AETHER_PLANNER_PROVIDER"] = "openai",
            ["OPENAI_BASE_URL"] = "http://127.0.0.1:8080/v1/chat/completions",
            ["OPENAI_API_KEY"] = "synthetic",
        });
        Assert.Null(loopback.ConfigurationError);
        Assert.True(loopback.IsValid);
    }

    [Fact]
    public void MissingKeyOrEndpointIsUnconfiguredNotAnInvalidDirective()
    {
        var settings = ModelProviderSettings.FromEnvironment(new Dictionary<string, string>
        {
            ["AETHER_PLANNER_PROVIDER"] = "gemini",
        });

        Assert.False(settings.IsValid);
        Assert.Null(settings.ConfigurationError);
        var route = ModelRouter.Decide(DataClassification.Public, true, true, false, false, settings);
        Assert.Equal(ModelRouter.ReasonCloudNotConfigured, route.ReasonCode);
    }

    [Fact]
    public void ConfigurationErrorsNeverEchoSecretValues()
    {
        const string secret = "super-secret-key-value";
        var settings = ModelProviderSettings.FromEnvironment(new Dictionary<string, string>
        {
            ["AETHER_PLANNER_PROVIDER"] = "openai",
            ["OPENAI_BASE_URL"] = "http://example.invalid/v1/chat/completions",
            ["OPENAI_API_KEY"] = secret,
        });

        Assert.NotNull(settings.ConfigurationError);
        Assert.DoesNotContain(secret, settings.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain(secret, settings.ConfigurationError, StringComparison.Ordinal);
    }

    [Fact]
    public void LocalProfileDefaultsToApacheLicensedQwenModel()
    {
        var settings = ModelProviderSettings.FromEnvironment(new Dictionary<string, string>
        {
            ["AETHER_PLANNER_PROVIDER"] = "local",
            ["AETHER_LOCAL_MODEL_ENDPOINT"] = "http://127.0.0.1:11434/v1/chat/completions",
        });

        Assert.Equal("qwen2.5:7b", settings.Model);
    }
}
