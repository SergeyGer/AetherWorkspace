using Aether.AI.Planner;
using Aether.Contracts;
using Aether.Contracts.Errors;
using Aether.Contracts.Models;
using Xunit;

namespace Aether.AI.Tests;

public class DeterministicPlannerTests
{
    private const string ReferenceCommand =
        "Open Northstar bond analytics, show the associated risk view, synchronize this instrument across the workspace and summarize the exposure privately.";

    private static readonly DeterministicCommandPlanner Planner = new();

    private static async Task<PlanGenerationResult> PlanAsync(string command, DataClassification classification = DataClassification.Restricted) =>
        await Planner.GeneratePlanAsync(new PlanningRequest(command, classification, TraceIdGenerator.NewTraceId()));

    [Fact]
    public async Task ReferenceCommandProducesTheExactDeterministicPlan()
    {
        var result = await PlanAsync(ReferenceCommand);

        Assert.True(result.Success);
        Assert.False(result.UsedModel);
        Assert.Equal(DeterministicCommandPlanner.ProviderName, result.Provider);
        var plan = result.Plan!;
        Assert.Equal("1.0", plan.SchemaVersion);
        Assert.Equal(6, plan.Steps.Count);
        Assert.Equal(
            ["LaunchApp", "LaunchApp", "LaunchApp", "PublishContext", "ArrangeWindows", "SummarizeContext"],
            plan.Steps.Select(step => step.Action));
        Assert.Equal(
            ["sample.watchlist", "sample.risk", "sample.chart"],
            plan.Steps.Take(3).Select(step => step.AppId));
        Assert.Equal("synthetic.instrument.northstar", plan.Steps[3].ContextRef);
        Assert.Equal(Layouts.SideBySide, plan.Steps[4].Layout);
        Assert.Equal("synthetic.exposure.northstar", plan.Steps[5].ContextRef);
        Assert.Equal(SummarizeModes.Private, plan.Steps[5].Mode);
    }

    [Fact]
    public async Task PlanIdIsStableForTheSameCommand()
    {
        var first = await PlanAsync(ReferenceCommand);
        var second = await PlanAsync(ReferenceCommand);
        Assert.Equal(first.Plan!.PlanId, second.Plan!.PlanId);
        Assert.StartsWith("plan-", first.Plan.PlanId);
    }

    [Theory]
    [InlineData("hello world")]
    [InlineData("delete everything")]
    [InlineData("open the risk view")]
    public async Task UnsupportedCommandsAreRejectedWithoutAModelCall(string command)
    {
        var result = await PlanAsync(command);

        Assert.False(result.Success);
        Assert.False(result.UsedModel);
        Assert.Equal(AetherErrorCodes.AiCommandUnsupported, result.ErrorCode);
        Assert.Null(result.Plan);
    }

    [Theory]
    [InlineData("OPEN NORTHSTAR BOND ANALYTICS, SHOW THE ASSOCIATED RISK VIEW, SYNCHRONIZE THIS INSTRUMENT AND SUMMARIZE THE EXPOSURE PRIVATELY.")]
    [InlineData("Open Northstar bond analytics, show risk view, sync instrument across the workspace and summarize exposure privately")]
    public async Task CaseAndWordingVariantsAreRecognized(string command)
    {
        var result = await PlanAsync(command);
        Assert.True(result.Success);
    }

    [Fact]
    public async Task ChartVariantCanBeSelectedByTrustedPlannerConfiguration()
    {
        var planner = new DeterministicCommandPlanner(DeterministicCommandPlanner.WebView2ChartAppId);
        var result = await planner.GeneratePlanAsync(new PlanningRequest(
            "Open Northstar bond analytics, show the associated risk view, synchronize this instrument across the workspace and summarize the exposure privately.",
            DataClassification.Restricted,
            Aether.Contracts.TraceIdGenerator.NewTraceId()));

        Assert.True(result.Success);
        Assert.Contains(result.Plan!.Steps, step => step.Action == "LaunchApp" && step.AppId == "sample.chart.webview2");
    }

}
