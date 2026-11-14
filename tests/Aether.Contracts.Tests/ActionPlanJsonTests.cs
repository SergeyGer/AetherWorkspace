using Aether.Contracts;
using Aether.Contracts.Errors;
using Aether.Contracts.Json;
using Aether.Contracts.Models;
using Aether.Execution.Validation;
using Aether.Testing;
using Xunit;

namespace Aether.Contracts.Tests;

public class ActionPlanJsonTests
{
    [Fact]
    public void ReferencePlanSerializesAndParsesStrictly()
    {
        var plan = TestData.ReferencePlan();
        var json = AetherJson.Serialize(plan);
        var parsed = AetherJson.DeserializeStrict<ActionPlan>(json);

        Assert.Equal(plan.PlanId, parsed.PlanId);
        Assert.Equal(plan.Steps.Count, parsed.Steps.Count);
        Assert.Equal(ActionKinds.LaunchApp, parsed.Steps[0].Action);
        Assert.Equal("synthetic.exposure.northstar", parsed.Steps[^1].ContextRef);
    }

    [Fact]
    public void UnknownPlanPropertyIsRejected()
    {
        var json = """
            {
              "schemaVersion": "1.0",
              "planId": "plan-unknown-field",
              "executable": "cmd.exe /c calc",
              "steps": [ { "action": "ShowStatus", "statusText": "hi" } ]
            }
            """;

        var exception = Assert.Throws<AetherContractException>(() => AetherJson.DeserializeStrict<ActionPlan>(json));
        Assert.Equal(AetherErrorCodes.PlanInvalidJson, exception.Code);
    }

    [Fact]
    public void ParserReturnsStructuredIssuesInsteadOfThrowing()
    {
        var (plan, issues) = ActionPlanParser.Parse("{ not json");

        Assert.Null(plan);
        Assert.Single(issues);
        Assert.Equal(AetherErrorCodes.PlanInvalidJson, issues[0].Code);
    }

    [Fact]
    public void EmptyDocumentIsRejected()
    {
        var (plan, issues) = ActionPlanParser.Parse("");
        Assert.Null(plan);
        Assert.Equal(AetherErrorCodes.PlanInvalidJson, issues[0].Code);
    }
}
