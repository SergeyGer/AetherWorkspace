using Aether.Contracts.Errors;
using Aether.Contracts.Models;
using Aether.Execution.Results;
using Aether.Execution.Validation;
using Aether.Testing;
using Xunit;

namespace Aether.Execution.Tests;

public class ValidatorTests
{
    private static readonly ActionPlanValidator Validator = new(new ActionPlanValidationOptions { MaxActions = 6 });
    private static readonly Aether.Execution.Catalog.PlanCatalog Catalog = TestData.CreatePlanCatalog();

    private static ActionPlan Plan(params ActionPlanStep[] steps) => new()
    {
        SchemaVersion = Aether.Contracts.AetherVersions.ContractSchemaVersion,
        PlanId = "plan-validator-test",
        Steps = steps,
    };

    [Fact]
    public void ReferencePlanIsValid()
    {
        var result = Validator.Validate(TestData.ReferencePlan(), Catalog);
        Assert.True(result.IsValid, string.Join("; ", result.Issues.Select(issue => issue.Code)));
    }

    [Fact]
    public void SupportedSingleStepActionsAreValid()
    {
        var plans = new[]
        {
            Plan(new ActionPlanStep { Action = ActionKinds.LaunchApp, AppId = "sample.watchlist" }),
            Plan(new ActionPlanStep { Action = ActionKinds.PublishContext, ContextRef = "synthetic.instrument.northstar" }),
            Plan(new ActionPlanStep { Action = ActionKinds.ArrangeWindows, Layout = Layouts.SideBySide }),
            Plan(new ActionPlanStep { Action = ActionKinds.SummarizeContext, ContextRef = "synthetic.exposure.northstar", Mode = SummarizeModes.Private }),
            Plan(new ActionPlanStep { Action = ActionKinds.ShowStatus, StatusText = "ready" }),
        };

        foreach (var plan in plans)
        {
            var result = Validator.Validate(plan, Catalog);
            Assert.True(result.IsValid, plan.Steps[0].Action + ": " + string.Join("; ", result.Issues.Select(issue => issue.Code)));
        }
    }

    [Fact]
    public void UnknownActionIsRejected()
    {
        var result = Validator.Validate(Plan(new ActionPlanStep { Action = "ExecShell" }), Catalog);
        AssertRejected(result, AetherErrorCodes.PlanActionUnknown);
    }

    [Theory]
    [InlineData("2.0")]
    [InlineData("0.9")]
    [InlineData("")]
    public void UnsupportedSchemaVersionsAreRejected(string version)
    {
        var plan = Plan(new ActionPlanStep { Action = ActionKinds.ShowStatus, StatusText = "hi" });
        plan = new ActionPlan { SchemaVersion = version, PlanId = plan.PlanId, Steps = plan.Steps };
        var result = Validator.Validate(plan, Catalog);
        AssertRejected(result, AetherErrorCodes.PlanSchemaVersionUnsupported);
    }

    [Fact]
    public void TooManyActionsAreRejected()
    {
        var steps = Enumerable.Range(0, 7)
            .Select(index => new ActionPlanStep { Action = ActionKinds.ShowStatus, StatusText = $"step {index}" })
            .ToArray();
        var result = Validator.Validate(Plan(steps), Catalog);
        AssertRejected(result, AetherErrorCodes.PlanStepsTooMany);
    }

    [Fact]
    public void UnknownAppIsRejected()
    {
        var result = Validator.Validate(Plan(new ActionPlanStep { Action = ActionKinds.LaunchApp, AppId = "sample.unknown" }), Catalog);
        AssertRejected(result, AetherErrorCodes.PlanAppUnknown);
    }

    [Fact]
    public void NonLaunchableAppIsRejected()
    {
        var result = Validator.Validate(Plan(new ActionPlanStep { Action = ActionKinds.LaunchApp, AppId = "aether.shell" }), Catalog);
        AssertRejected(result, AetherErrorCodes.PlanAppNotLaunchable);
    }

    [Fact]
    public void DuplicateLaunchIsRejected()
    {
        var result = Validator.Validate(Plan(
            new ActionPlanStep { Action = ActionKinds.LaunchApp, AppId = "sample.watchlist" },
            new ActionPlanStep { Action = ActionKinds.LaunchApp, AppId = "sample.watchlist" }), Catalog);
        AssertRejected(result, AetherErrorCodes.PlanAppDuplicateLaunch);
    }

    [Theory]
    [InlineData("../../secrets")]
    [InlineData("https://attacker.example/context")]
    [InlineData("C:\\Windows\\System32\\cmd.exe")]
    public void UnsafeContextReferencesAreRejected(string contextRef)
    {
        var result = Validator.Validate(Plan(new ActionPlanStep { Action = ActionKinds.PublishContext, ContextRef = contextRef }), Catalog);
        Assert.False(result.IsValid);
        Assert.Contains(result.Issues, issue => issue.Code == AetherErrorCodes.PlanUnsafeContent);
    }

    [Fact]
    public void UnknownTrustedContextIsRejected()
    {
        var result = Validator.Validate(Plan(new ActionPlanStep { Action = ActionKinds.PublishContext, ContextRef = "synthetic.unknown" }), Catalog);
        AssertRejected(result, AetherErrorCodes.PlanContextUnknown);
    }

    [Fact]
    public void UnsupportedLayoutIsRejected()
    {
        var result = Validator.Validate(Plan(new ActionPlanStep { Action = ActionKinds.ArrangeWindows, Layout = "mosaic" }), Catalog);
        AssertRejected(result, AetherErrorCodes.PlanLayoutUnsupported);
    }

    [Fact]
    public void CloudSummaryModeIsRejected()
    {
        var result = Validator.Validate(Plan(
            new ActionPlanStep { Action = ActionKinds.SummarizeContext, ContextRef = "synthetic.exposure.northstar", Mode = "cloud" }), Catalog);
        AssertRejected(result, AetherErrorCodes.PlanModeUnsupported);
    }

    [Fact]
    public void InvalidOrOversizedStatusTextIsRejected()
    {
        var empty = Validator.Validate(Plan(new ActionPlanStep { Action = ActionKinds.ShowStatus, StatusText = "" }), Catalog);
        AssertRejected(empty, AetherErrorCodes.PlanStatusInvalid);

        var huge = Validator.Validate(Plan(new ActionPlanStep { Action = ActionKinds.ShowStatus, StatusText = new string('x', 300) }), Catalog);
        AssertRejected(huge, AetherErrorCodes.PlanStatusInvalid);
    }

    [Fact]
    public void FieldsFromOtherActionsAreRejected()
    {
        var result = Validator.Validate(Plan(new ActionPlanStep
        {
            Action = ActionKinds.LaunchApp,
            AppId = "sample.watchlist",
            ContextRef = "synthetic.instrument.northstar",
        }), Catalog);

        AssertRejected(result, AetherErrorCodes.PlanActionFieldsInvalid);
    }

    [Fact]
    public void PlanIdMustBeSafeAndBounded()
    {
        var plan = new ActionPlan
        {
            SchemaVersion = "1.0",
            PlanId = "../escape plan",
            Steps = [new ActionPlanStep { Action = ActionKinds.ShowStatus, StatusText = "hi" }],
        };

        var result = Validator.Validate(plan, Catalog);
        AssertRejected(result, AetherErrorCodes.PlanIdInvalid);
    }

    [Fact]
    public void EmptyPlanIsRejected()
    {
        var plan = new ActionPlan { SchemaVersion = "1.0", PlanId = "plan-empty", Steps = [] };
        var result = Validator.Validate(plan, Catalog);
        AssertRejected(result, AetherErrorCodes.PlanStepsEmpty);
    }

    [Fact]
    public void ConfiguredBudgetCanBeTightenedPerCall()
    {
        var sixSteps = new ActionPlan
        {
            SchemaVersion = "1.0",
            PlanId = "plan-budget",
            Steps = Enumerable.Range(0, 6)
                .Select(index => new ActionPlanStep { Action = ActionKinds.ShowStatus, StatusText = $"step {index}" })
                .ToArray(),
        };

        var result = Validator.Validate(sixSteps, new ActionPlanValidationOptions { MaxActions = 5 }, Catalog);
        AssertRejected(result, AetherErrorCodes.PlanStepsTooMany);
    }

    [Fact]
    public void InvalidValidatorOptionsAreRejectedEarly()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new ActionPlanValidator(new ActionPlanValidationOptions { MaxActions = 0 }));
        Assert.Throws<ArgumentOutOfRangeException>(() => new ActionPlanValidator(new ActionPlanValidationOptions { MaxActions = 13 }));
    }

    private static void AssertRejected(ActionPlanValidationResult result, string expectedCode)
    {
        Assert.False(result.IsValid);
        Assert.Contains(result.Issues, issue => issue.Code == expectedCode);
    }
}
