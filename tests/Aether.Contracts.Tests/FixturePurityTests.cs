using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Aether.Testing;
using Xunit;

namespace Aether.Contracts.Tests;

/// <summary>
/// Synthetic-content policy checks: all committed fixtures must be explicitly
/// marked synthetic and must not contain real-looking personal data patterns.
/// </summary>
public class FixturePurityTests
{
    private static readonly Regex EmailPattern = new(@"[A-Za-z0-9._%+-]+@[A-Za-z0-9.-]+\.[A-Za-z]{2,}", RegexOptions.Compiled);
    private static readonly Regex LongDigitRun = new(@"\b\d{13,19}\b", RegexOptions.Compiled);

    [Theory]
    [InlineData("watchlist.json")]
    [InlineData("risk.json")]
    [InlineData("chart.json")]
    public void FixturesAreMarkedSyntheticAndFreeOfRealisticPii(string fileName)
    {
        var content = File.ReadAllText(TestRepo.PathTo("samples", "fixtures", fileName));
        var node = JsonNode.Parse(content)!;

        Assert.True(node["synthetic"]?.GetValue<bool>(), $"{fileName} must be marked synthetic.");
        Assert.Empty(EmailPattern.Matches(content));
        Assert.Empty(LongDigitRun.Matches(content));
        Assert.Contains("fictitious", content, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ContextCatalogDeclaresExplicitClassifications()
    {
        var node = JsonNode.Parse(File.ReadAllText(TestRepo.PathTo("samples", "contexts", "contexts.json")))!;
        var contexts = node["contexts"]!.AsArray();
        Assert.NotEmpty(contexts);

        foreach (var context in contexts)
        {
            var classification = context!["classification"]!.GetValue<string>();
            Assert.Contains(classification, new[] { "PUBLIC", "INTERNAL", "RESTRICTED" });
            Assert.NotNull(context["payload"]);
            Assert.NotNull(context["contextRef"]);
        }
    }

    [Fact]
    public void AppRegistryOnlyDeclaresKnownCapabilities()
    {
        var registry = TestData.CreateRegistry();
        foreach (var app in registry.Ordered)
        {
            foreach (var capability in app.Capabilities)
            {
                Assert.Contains(capability, Aether.Contracts.Capabilities.CapabilityNames.All);
            }

            Assert.NotEmpty(app.AllowedClassifications);
        }
    }
}
