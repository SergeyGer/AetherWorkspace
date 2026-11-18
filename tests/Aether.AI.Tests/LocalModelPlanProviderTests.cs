using Aether.AI.Planner;
using Aether.AI.Providers;
using Aether.Contracts;
using Aether.Contracts.Errors;
using Aether.Security.Audit;
using Aether.Security.Egress;
using Aether.Security.Policy;
using Aether.Testing;
using Xunit;

namespace Aether.AI.Tests;

/// <summary>v1.3 AW-059/AW-067: local planning route, truthful provenance and failure semantics.</summary>
public sealed class LocalModelPlanProviderTests
{
    private const string ValidPlanJson = """
        {
          "schemaVersion": "1.0",
          "planId": "plan-local-1",
          "steps": [
            { "action": "ShowStatus", "statusText": "local plan" }
          ]
        }
        """;

    private static (LocalModelPlanProvider Provider, RecordingLocalTransport Transport, RecordingAuditSink Audit) Create(
        string response = ValidPlanJson,
        CloudModelResponse? failure = null)
    {
        var audit = new RecordingAuditSink();
        var gateway = new EgressGateway(new PolicyEngine(PolicyDocument.CreateDefault()), audit);
        var transport = new RecordingLocalTransport
        {
            Responder = _ => failure ?? CloudModelResponse.Ok(response),
        };
        return (new LocalModelPlanProvider(transport, gateway), transport, audit);
    }

    [Fact]
    public async Task LocalPlanUsesLocalTransportAndTruthfulProvenance()
    {
        var (provider, transport, audit) = Create();

        var result = await provider.GeneratePlanAsync(new PlanningRequest(
            "private synthetic command", DataClassification.Restricted, TraceIdGenerator.NewTraceId()));

        Assert.True(result.Success);
        Assert.True(result.UsedModel);
        Assert.Equal("local-plan", result.Provider);
        Assert.Equal("plan-local-1", result.Plan!.PlanId);
        Assert.Equal(1, transport.Calls);
        Assert.Single(audit.Drafts);
        Assert.Equal(Aether.Security.Audit.AuditOutcomes.Succeeded, audit.Drafts[0].Outcome);
    }

    [Fact]
    public async Task ProviderFailureReturnsTypedErrorAndNoPlan()
    {
        var (provider, _, _) = Create(failure: CloudModelResponse.Fail(AetherErrorCodes.AiProviderUnavailable));

        var result = await provider.GeneratePlanAsync(new PlanningRequest(
            "private synthetic command", DataClassification.Internal, TraceIdGenerator.NewTraceId()));

        Assert.False(result.Success);
        Assert.False(result.UsedModel);
        Assert.Equal(AetherErrorCodes.AiProviderUnavailable, result.ErrorCode);
        Assert.Equal("local-plan", result.Provider);
    }

    [Fact]
    public async Task InvalidModelOutputIsRejectedByTheStrictParser()
    {
        var (provider, _, _) = Create("not a plan");

        var result = await provider.GeneratePlanAsync(new PlanningRequest(
            "private synthetic command", DataClassification.Public, TraceIdGenerator.NewTraceId()));

        Assert.False(result.Success);
        Assert.Equal(AetherErrorCodes.AiOutputInvalid, result.ErrorCode);
        Assert.False(result.UsedModel);
    }
}
