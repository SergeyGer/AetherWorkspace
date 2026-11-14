using Aether.Contracts.Errors;
using Aether.Contracts.Json;
using Aether.Contracts.Models;
using Aether.Execution.Results;

namespace Aether.Execution.Validation;

/// <summary>Strict JSON-to-plan parser. Unknown fields are rejected, never ignored.</summary>
public static class ActionPlanParser
{
    public static (ActionPlan? Plan, IReadOnlyList<PlanValidationIssue> Issues) Parse(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return (null, [new PlanValidationIssue(AetherErrorCodes.PlanInvalidJson, "Plan document is empty.", null)]);
        }

        try
        {
            var plan = AetherJson.DeserializeStrict<ActionPlan>(json);
            return (plan, []);
        }
        catch (AetherContractException exception)
        {
            return (null, [new PlanValidationIssue(AetherErrorCodes.PlanInvalidJson, exception.Message, null)]);
        }
    }
}
