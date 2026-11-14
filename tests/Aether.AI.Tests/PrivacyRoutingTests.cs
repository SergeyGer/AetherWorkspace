using Aether.AI.Planner;
using Aether.AI.Providers;
using Aether.AI.Summarization;
using Aether.Contracts;
using Aether.Contracts.Errors;
using Aether.Security.Egress;
using Aether.Security.Policy;
using Aether.Testing;
using Xunit;

namespace Aether.AI.Tests;

public class PrivacyRoutingTests
{
    private static (EgressGateway Egress, RecordingAuditSink Audit) CreateEgress() 
    {
        var audit = new RecordingAuditSink();
        return (new EgressGateway(new PolicyEngine(PolicyDocument.CreateDefault()), audit), audit);
    }

    private static SummaryRequest Summary(DataClassification classification) => new(
        TraceIdGenerator.NewTraceId(),
        "plan-routing",
        "synthetic.exposure.northstar",
        "aether.exposure",
        classification,
        "{\"type\":\"aether.exposure\",\"name\":\"test\"}",
        "private");

    [Fact]
    public async Task RestrictedSummaryNeverUsesCloudEvenWhenPublicPrefersCloud()
    {
        var (egress, _) = CreateEgress();
        var cloudTransport = new RecordingCloudTransport();
        var cloudProvider = new CloudSummaryProvider(cloudTransport, egress, new PlanProviderOptions(true, true));
        var localProvider = new LocalDeterministicSummaryProvider();
        var service = new SummaryService(localProvider, cloudProvider, new SummaryServiceOptions(PreferCloudForPublic: true));

        var result = await service.SummarizeAsync(Summary(DataClassification.Restricted));

        Assert.True(result.Success);
        Assert.Equal(localProvider.ProviderId, result.Provider);
        Assert.Equal(0, cloudTransport.Calls);
        Assert.Equal(0, egress.Snapshot().ExternalAttempts);
    }

    [Fact]
    public async Task PublicSummaryCanUseConfiguredCloudOnlyWhenExplicitlyPreferred()
    {
        var (egress, _) = CreateEgress();
        var cloudTransport = new RecordingCloudTransport
        {
            Responder = _ => CloudModelResponse.Ok("{\"summary\":\"synthetic public summary\"}"),
        };
        var cloudProvider = new CloudSummaryProvider(cloudTransport, egress, new PlanProviderOptions(true, true));
        var service = new SummaryService(new LocalDeterministicSummaryProvider(), cloudProvider, new SummaryServiceOptions(PreferCloudForPublic: true));

        var result = await service.SummarizeAsync(Summary(DataClassification.Public));

        Assert.True(result.Success);
        Assert.Equal(1, cloudTransport.Calls);
        Assert.Equal(CloudSummaryProvider.SummaryPrompt, cloudTransport.Requests[0].SystemPrompt);
    }

    [Fact]
    public async Task PrivateSummaryAlwaysUsesLocalProvider()
    {
        var (egress, _) = CreateEgress();
        var cloudTransport = new RecordingCloudTransport();
        var cloudProvider = new CloudSummaryProvider(cloudTransport, egress, new PlanProviderOptions(true, true));
        var service = new SummaryService(new LocalDeterministicSummaryProvider(), cloudProvider, new SummaryServiceOptions(PreferCloudForPublic: true));

        var result = await service.SummarizePrivatelyAsync(Summary(DataClassification.Public));

        Assert.True(result.Success);
        Assert.Equal(0, cloudTransport.Calls);
    }

    [Fact]
    public async Task UnknownOrRestrictedCloudSummaryIsDeniedBeforeTransport()
    {
        var (egress, _) = CreateEgress();
        var cloudTransport = new RecordingCloudTransport();
        var provider = new CloudSummaryProvider(cloudTransport, egress, new PlanProviderOptions(true, true));

        foreach (var classification in new[] { DataClassification.Restricted, DataClassification.Unknown, DataClassification.Internal })
        {
            var result = await provider.SummarizeAsync(Summary(classification));
            Assert.False(result.Success);
        }

        Assert.Equal(0, cloudTransport.Calls);
        Assert.Equal(0, egress.Snapshot().ExternalAttempts);
        Assert.Equal(3, egress.Snapshot().Denied);
    }

    [Fact]
    public async Task DeterministicPlannerNeverGeneratesExternalTraffic()
    {
        var (egress, _) = CreateEgress();
        var planner = new DeterministicCommandPlanner();
        for (var index = 0; index < 20; index++)
        {
            await planner.GeneratePlanAsync(new PlanningRequest(
                "Open Northstar bond analytics, show risk view, synchronize instrument and summarize exposure privately",
                DataClassification.Restricted,
                TraceIdGenerator.NewTraceId()));
        }

        Assert.Equal(0, egress.Snapshot().Attempts);
        Assert.Equal(0, egress.Snapshot().ExternalAttempts);
    }
}
