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

public class CloudPlanProviderTests
{
    private const string ValidPlanJson = """
        {
          "schemaVersion": "1.0",
          "planId": "plan-cloud-1",
          "steps": [
            { "action": "ShowStatus", "statusText": "cloud plan" }
          ]
        }
        """;

    private static (CloudPlanProvider Provider, RecordingCloudTransport Transport, EgressGateway Gateway, RecordingAuditSink Audit) Create(
        bool enabled = true, bool consent = true, string response = ValidPlanJson)
    {
        var audit = new RecordingAuditSink();
        var gateway = new EgressGateway(new PolicyEngine(PolicyDocument.CreateDefault()), audit);
        var transport = new RecordingCloudTransport
        {
            Responder = request => Aether.AI.Providers.CloudModelResponse.Ok(response),
        };
        var provider = new CloudPlanProvider(transport, gateway, new PlanProviderOptions(enabled, consent));
        return (provider, transport, gateway, audit);
    }

    [Fact]
    public async Task PublicDataWithConsentReachesTransportOnce()
    {
        var (provider, transport, gateway, _) = Create();
        var result = await provider.GeneratePlanAsync(new PlanningRequest(
            "some public synthetic command", DataClassification.Public, TraceIdGenerator.NewTraceId()));

        Assert.True(result.Success);
        Assert.True(result.UsedModel);
        Assert.Equal(1, transport.Calls);
        Assert.Equal(1, gateway.Snapshot().ExternalAttempts);
        Assert.Single(result.Plan!.Steps);
    }

    [Theory]
    [InlineData(DataClassification.Internal)]
    [InlineData(DataClassification.Restricted)]
    [InlineData(DataClassification.Unknown)]
    public async Task NonPublicClassificationsNeverReachTheProvider(DataClassification classification)
    {
        var (provider, transport, gateway, _) = Create();
        var result = await provider.GeneratePlanAsync(new PlanningRequest(
            "classified command", classification, TraceIdGenerator.NewTraceId()));

        Assert.False(result.Success);
        Assert.Equal(0, transport.Calls);
        Assert.Equal(0, gateway.Snapshot().ExternalAttempts);
        Assert.NotNull(result.ErrorCode);
    }

    [Fact]
    public async Task DisabledCloudModeNeverReachesTheProvider()
    {
        var (provider, transport, _, _) = Create(enabled: false);
        var result = await provider.GeneratePlanAsync(new PlanningRequest("public", DataClassification.Public, TraceIdGenerator.NewTraceId()));

        Assert.False(result.Success);
        Assert.Equal(0, transport.Calls);
    }

    [Fact]
    public async Task MissingConsentNeverReachesTheProvider()
    {
        var (provider, transport, _, _) = Create(consent: false);
        var result = await provider.GeneratePlanAsync(new PlanningRequest("public", DataClassification.Public, TraceIdGenerator.NewTraceId()));

        Assert.False(result.Success);
        Assert.Equal(0, transport.Calls);
        Assert.Equal(AetherErrorCodes.PolicyCloudConsentRequired, result.ErrorCode);
    }

    [Fact]
    public async Task MarkdownFencedPlanIsExtractedButStillValidatedLater()
    {
        var fenced = "```json\n" + ValidPlanJson + "\n```";
        var (provider, _, _, _) = Create(response: fenced);
        var result = await provider.GeneratePlanAsync(new PlanningRequest("public", DataClassification.Public, TraceIdGenerator.NewTraceId()));

        Assert.True(result.Success);
        Assert.Equal("plan-cloud-1", result.Plan!.PlanId);
    }

    [Fact]
    public async Task MalformedModelOutputIsRejectedAsInvalidOutput()
    {
        var (provider, _, _, _) = Create(response: "not a json plan at all");
        var result = await provider.GeneratePlanAsync(new PlanningRequest("public", DataClassification.Public, TraceIdGenerator.NewTraceId()));

        Assert.False(result.Success);
        Assert.Equal(AetherErrorCodes.AiOutputInvalid, result.ErrorCode);
    }

    [Fact]
    public async Task UnknownPlanFieldsFromModelAreRejected()
    {
        var malicious = """
            {
              "schemaVersion": "1.0",
              "planId": "plan-cloud-evil",
              "executable": "cmd.exe /c calc",
              "steps": [ { "action": "ShowStatus", "statusText": "hi" } ]
            }
            """;
        var (provider, _, _, _) = Create(response: malicious);
        var result = await provider.GeneratePlanAsync(new PlanningRequest("public", DataClassification.Public, TraceIdGenerator.NewTraceId()));

        Assert.False(result.Success);
        Assert.Equal(AetherErrorCodes.AiOutputInvalid, result.ErrorCode);
    }
}
