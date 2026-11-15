using Aether.AI.Providers;
using Aether.AI.Routing;
using Aether.Contracts;
using Xunit;

namespace Aether.AI.Tests;

public class ModelRouterTests
{
    private static ModelProviderSettings OpenAiConfigured(IReadOnlyDictionary<string, string>? overrides = null) =>
        ModelProviderSettings.FromEnvironment(overrides ?? new Dictionary<string, string>
        {
            ["AETHER_MODEL_PROVIDER"] = "openai",
            ["OPENAI_API_KEY"] = "synthetic-key",
            ["OPENAI_BASE_URL"] = "https://example.invalid/v1/chat/completions",
            ["OPENAI_MODEL"] = "gpt-4o-mini",
        });

    [Theory]
    [InlineData(DataClassification.Restricted)]
    [InlineData(DataClassification.Unknown)]
    [InlineData(DataClassification.Internal)]
    public void NonPublicClassificationsNeverAllowCloud(DataClassification classification)
    {
        var route = ModelRouter.Decide(classification, cloudEnabled: true, cloudConsentGranted: true,
            cloudConfigured: true, localModelConfigured: false, OpenAiConfigured());

        Assert.False(route.CloudAttemptAllowed);
        Assert.NotEqual(ModelRouteKind.Cloud, route.Kind);
        Assert.Equal(ModelRouteKind.DeterministicLocal, route.Kind);
    }

    [Fact]
    public void NonPublicWithLocalModelUsesPrivateLocalRoute()
    {
        var route = ModelRouter.Decide(DataClassification.Restricted, true, true, true, localModelConfigured: true, OpenAiConfigured());

        Assert.False(route.CloudAttemptAllowed);
        Assert.Equal(ModelRouteKind.LocalModel, route.Kind);
    }

    [Fact]
    public void PublicRequiresEnabledConfiguredAndConsentBeforeCloud()
    {
        Assert.False(ModelRouter.Decide(DataClassification.Public, false, true, true, false, OpenAiConfigured()).CloudAttemptAllowed);
        Assert.False(ModelRouter.Decide(DataClassification.Public, true, false, true, false, OpenAiConfigured()).CloudAttemptAllowed);
        Assert.False(ModelRouter.Decide(DataClassification.Public, true, true, false, false, OpenAiConfigured()).CloudAttemptAllowed);

        var allowed = ModelRouter.Decide(DataClassification.Public, true, true, true, false, OpenAiConfigured());
        Assert.True(allowed.CloudAttemptAllowed);
        Assert.Equal(ModelRouteKind.Cloud, allowed.Kind);
        Assert.Equal("openai", allowed.ProviderId);
        Assert.Equal("gpt-4o-mini", allowed.Model);
        Assert.Equal(ModelRouter.ReasonPublicCloudAllowed, allowed.ReasonCode);
    }

    [Fact]
    public void ProviderSelectionHonoursConfiguredProvider()
    {
        var gemini = ModelProviderSettings.FromEnvironment(new Dictionary<string, string>
        {
            ["AETHER_MODEL_PROVIDER"] = "gemini",
            ["GEMINI_API_KEY"] = "synthetic",
            ["GEMINI_BASE_URL"] = "https://example.invalid/v1beta",
            ["GEMINI_MODEL"] = "gemini-2.5-flash-lite",
        });

        Assert.Equal("gemini", gemini.ProviderId);
        Assert.Equal("gemini-2.5-flash-lite", gemini.Model);
        Assert.True(gemini.IsConfigured);
        Assert.Equal("gemini-native", new Aether.AI.Providers.GeminiCloudModelTransport(
            new Aether.Security.Secrets.EnvironmentSecretProvider(new Dictionary<string, string>
            {
                ["GEMINI_API_KEY"] = "synthetic",
            }),
            baseUrl: gemini.ReadEndpoint(),
            model: gemini.Model).ProviderId);
    }

    [Fact]
    public void MissingConfigurationFallsBackToDeterministicRoute()
    {
        var provider = ModelProviderSettings.FromEnvironment(new Dictionary<string, string>());
        var route = ModelRouter.Decide(DataClassification.Public, true, true, false, false, provider);

        Assert.False(route.CloudAttemptAllowed);
        Assert.Equal(ModelRouteKind.DeterministicLocal, route.Kind);
        Assert.Equal(ModelRouter.ReasonCloudNotConfigured, route.ReasonCode);
    }
}
