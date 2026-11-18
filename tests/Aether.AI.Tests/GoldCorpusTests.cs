using System.Text.Json;
using Aether.Testing;
using Xunit;

namespace Aether.AI.Tests;

/// <summary>v1.3 AW-060: the committed labelled corpus meets the minimum v1.3 contract.</summary>
public sealed class GoldCorpusTests
{
    private static string CorpusPath => TestRepo.PathTo("docs", "evaluation", "command-corpus-v1.3.json");

    [Fact]
    public void CorpusHasAtLeastOneHundredUniqueLabelledItems()
    {
        using var document = JsonDocument.Parse(File.ReadAllText(CorpusPath));
        var root = document.RootElement;
        Assert.Equal("1.1", root.GetProperty("schemaVersion").GetString());
        var items = root.GetProperty("items").EnumerateArray().ToArray();

        Assert.True(items.Length >= 100, $"expected >=100 items, found {items.Length}");
        Assert.Equal(items.Length, items.Select(item => item.GetProperty("id").GetString()).Distinct(StringComparer.Ordinal).Count());
        Assert.Contains(items, item => item.GetProperty("expectation").GetString() == "plan");
        Assert.Contains(items, item => item.GetProperty("expectation").GetString() == "no_bounded_plan");

        foreach (var item in items)
        {
            Assert.False(string.IsNullOrWhiteSpace(item.GetProperty("id").GetString()));
            Assert.False(string.IsNullOrWhiteSpace(item.GetProperty("category").GetString()));
            Assert.False(string.IsNullOrWhiteSpace(item.GetProperty("command").GetString()));
        }
    }

    [Fact]
    public void CanonicalPlanIsABoundedSixStepReferenceShape()
    {
        using var document = JsonDocument.Parse(File.ReadAllText(CorpusPath));
        var canonical = document.RootElement.GetProperty("canonicalPlans").GetProperty("reference").GetProperty("steps");
        var actions = canonical.EnumerateArray().Select(step => step.GetProperty("action").GetString()!).ToArray();

        Assert.Equal(6, actions.Length);
        Assert.Equal(["LaunchApp", "LaunchApp", "LaunchApp", "PublishContext", "ArrangeWindows", "SummarizeContext"], actions);
        Assert.Equal("side-by-side", canonical[4].GetProperty("layout").GetString());
        Assert.Equal("private", canonical[5].GetProperty("mode").GetString());
    }

    [Fact]
    public void EveryPlanExpectationReferencesAnExistingCanonicalPlan()
    {
        using var document = JsonDocument.Parse(File.ReadAllText(CorpusPath));
        var canonicalPlans = document.RootElement.GetProperty("canonicalPlans");
        foreach (var item in document.RootElement.GetProperty("items").EnumerateArray())
        {
            if (item.GetProperty("expectation").GetString() != "plan")
            {
                continue;
            }

            var planId = item.GetProperty("canonicalPlan").GetString();
            Assert.False(string.IsNullOrWhiteSpace(planId));
            Assert.True(canonicalPlans.TryGetProperty(planId!, out _));
        }
    }
}
