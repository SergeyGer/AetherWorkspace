using System.Text.Json;
using Aether.Contracts.Models;
using Aether.Execution.Actions;
using Aether.Execution.Executor;
using Aether.Execution.Results;
using Aether.Execution.Validation;
using Aether.Security.Audit;
using Aether.Testing;
using Xunit;

namespace Aether.Execution.Tests;

/// <summary>
/// NFR-03 evidence: the documented 100-plan unsafe corpus is denied before any
/// handler side effect. The corpus is generated reproducibly and committed.
/// </summary>
public class UnsafePlanCorpusTests
{
    public static TheoryData<string> CorpusEntryIds => LoadIdeas();

    private static TheoryData<string> LoadIdeas()
    {
        var data = new TheoryData<string>();
        foreach (var entry in LoadCorpus())
        {
            data.Add(entry.Id);
        }

        return data;
    }

    [Fact]
    public void CorpusContainsExactlyOneHundredDocumentedPlans()
    {
        var corpus = LoadCorpus();
        Assert.Equal(100, corpus.Count);
        Assert.Equal(100, corpus.Select(entry => entry.Id).Distinct().Count());
        Assert.All(corpus, entry => Assert.False(string.IsNullOrWhiteSpace(entry.Category)));
        Assert.All(corpus, entry => Assert.False(string.IsNullOrWhiteSpace(entry.ExpectedCode)));
    }

    [Theory]
    [MemberData(nameof(CorpusEntryIds))]
    public async Task EveryUnsafePlanIsDeniedBeforeAnySideEffect(string id)
    {
        var corpus = LoadCorpus();
        var entry = corpus.Single(candidate => candidate.Id == id);
        var catalog = TestData.CreatePlanCatalog();
        var handlers = new Dictionary<ActionKind, RecordingActionHandler>
        {
            [ActionKind.LaunchApp] = new(ActionKind.LaunchApp),
            [ActionKind.PublishContext] = new(ActionKind.PublishContext),
            [ActionKind.ArrangeWindows] = new(ActionKind.ArrangeWindows),
            [ActionKind.SummarizeContext] = new(ActionKind.SummarizeContext),
            [ActionKind.ShowStatus] = new(ActionKind.ShowStatus),
        };
        var audit = new HashChainAuditLog();
        var validator = new ActionPlanValidator(new ActionPlanValidationOptions { MaxActions = 6 });
        var executor = new PlanExecutor(
            validator,
            new Aether.Execution.Authorization.CapabilityPlanAuthorizationPolicy(),
            handlers.ToDictionary(pair => pair.Key, pair => (IActionHandler)pair.Value),
            audit,
            new ExecutionOptions { MaxActionsPerPlan = 6 });

        ActionPlan? plan;
        string? denialCode;
        if (entry.ExpectParseFailure)
        {
            var (parsed, issues) = ActionPlanParser.Parse(entry.PlanJson);
            Assert.Null(parsed);
            denialCode = issues[0].Code;
            plan = null;
        }
        else
        {
            var (parsed, issues) = ActionPlanParser.Parse(entry.PlanJson);
            Assert.NotNull(parsed);
            Assert.Empty(issues);
            plan = parsed;
            var validation = validator.Validate(plan, new ActionPlanValidationOptions { MaxActions = 6 }, catalog);
            Assert.False(validation.IsValid);
            denialCode = validation.FirstCode;
            Assert.Equal(entry.ExpectedCode, denialCode);
        }

        var trace = Aether.Contracts.TraceContext.NewRoot();
        var result = await executor.ExecuteAsync(plan, catalog, TestData.OperatorGrant(), trace);

        Assert.False(result.Succeeded);
        Assert.Equal(denialCode, result.DenialCode);
        Assert.All(handlers.Values, handler => Assert.Empty(handler.Executions));
        Assert.True(audit.Verify().Valid);
    }

    private static List<CorpusEntry> LoadCorpus()
    {
        var path = TestRepo.PathTo("tests", "fixtures", "unsafe-plans", "unsafe-plans.json");
        using var document = JsonDocument.Parse(File.ReadAllText(path));
        var entries = new List<CorpusEntry>();
        foreach (var element in document.RootElement.GetProperty("entries").EnumerateArray())
        {
            entries.Add(new CorpusEntry(
                element.GetProperty("id").GetString()!,
                element.GetProperty("category").GetString()!,
                element.GetProperty("expectedCode").GetString()!,
                element.GetProperty("expectParseFailure").GetBoolean(),
                element.GetProperty("plan").GetRawText()));
        }

        return entries;
    }

    private sealed record CorpusEntry(string Id, string Category, string ExpectedCode, bool ExpectParseFailure, string PlanJson);
}
