using Aether.AI.Planner;
using Aether.AI.Providers;
using Aether.AI.Summarization;
using Aether.Contracts;
using Aether.Contracts.Errors;
using Aether.Contracts.Json;
using Aether.Execution.Authorization;
using Aether.Execution.Executor;
using Aether.Execution.Validation;
using Aether.Security.Audit;
using Aether.Security.Classification;
using Aether.Security.Egress;
using Aether.Security.Policy;
using Aether.Testing;
using Xunit;

namespace Aether.Workflow.Tests;

public class OrchestratorTests
{
    private const string ReferenceCommand =
        "Open Northstar bond analytics, show the associated risk view, synchronize this instrument across the workspace and summarize the exposure privately.";

    private static (AetherWorkflowOrchestrator Orchestrator, FakeWorkflowBroker Broker, HashChainAuditLog Audit, RecordingCloudTransport Cloud) Create(
        bool withCloudPlanner = false,
        bool cloudEnabled = true,
        bool cloudConsent = true,
        PlanGenerationMode planningMode = PlanGenerationMode.DeterministicPreferred)
    {
        var audit = new HashChainAuditLog();
        var egress = new EgressGateway(new PolicyEngine(PolicyDocument.CreateDefault()), audit);
        var cloudTransport = new RecordingCloudTransport();
        IPlanGenerator? cloudPlanner = withCloudPlanner
            ? new CloudPlanProvider(cloudTransport, egress, new PlanProviderOptions(cloudEnabled, cloudConsent))
            : null;
        var broker = new FakeWorkflowBroker(TestData.CreateOperatorRegistrationReply());
        var summaryService = new FakeSummaryService();
        var orchestrator = new AetherWorkflowOrchestrator(
            new SourceFirstClassifier([new RuleBasedSensitiveEntityDetector()]),
            new DeterministicCommandPlanner(),
            cloudPlanner,
            broker,
            summaryService,
            new ActionPlanValidator(new ActionPlanValidationOptions { MaxActions = 6 }),
            new CapabilityPlanAuthorizationPolicy(),
            new WorkflowOrchestratorOptions { MaxActionsPerPlan = 6, PlanningMode = planningMode },
            audit);
        return (orchestrator, broker, audit, cloudTransport);
    }

    [Fact]
    public async Task ReferenceWorkflowExecutesAllBoundedSteps()
    {
        var (orchestrator, broker, audit, cloud) = Create();
        var result = await orchestrator.ExecuteAsync(new WorkflowRequest(ReferenceCommand, DataClassification.Restricted));

        Assert.True(result.Success, result.DenialCode + ": " + result.DenialMessage);
        Assert.Equal(6, result.Execution!.Steps.Count);
        Assert.All(result.Execution.Steps, step => Assert.Equal(Aether.Execution.Results.StepState.Succeeded, step.State));
        Assert.Equal(3, broker.LaunchedApps.Count);
        Assert.Single(broker.PublishedContexts);
        Assert.NotNull(result.Summary);
        Assert.Equal("fake-local-summary", result.Summary!.Provider);
        Assert.Equal(0, cloud.Calls);
        Assert.True(audit.Verify().Valid);
        Assert.True(TraceContext.IsValidTraceId(result.TraceId));
    }

    [Fact]
    public async Task CloudPreferredUsesModelPlanAndReportsTruthfulProvenance()
    {
        var (orchestrator, _, _, cloud) = Create(
            withCloudPlanner: true,
            planningMode: PlanGenerationMode.CloudPreferred);
        var reference = await new DeterministicCommandPlanner().GeneratePlanAsync(
            new PlanningRequest(ReferenceCommand, DataClassification.Public, TraceIdGenerator.NewTraceId()));
        Assert.NotNull(reference.Plan);
        cloud.Responder = _ => CloudModelResponse.Ok(AetherJson.Serialize(reference.Plan!));

        var result = await orchestrator.ExecuteAsync(new WorkflowRequest(ReferenceCommand, DataClassification.Public));

        Assert.True(result.Success, result.DenialCode + ": " + result.DenialMessage);
        Assert.True(result.UsedModel);
        Assert.Equal("cloud-plan", result.PlannerProvider);
        Assert.Equal(1, cloud.Calls);
        Assert.Equal(6, result.Execution!.Steps.Count);
    }

    [Fact]
    public async Task CloudPreferredFallbackIsClearlyLabelledAsDeterministic()
    {
        var (orchestrator, _, _, cloud) = Create(
            withCloudPlanner: true,
            planningMode: PlanGenerationMode.CloudPreferred);
        cloud.Responder = _ => CloudModelResponse.Ok("this is not a valid plan");

        var result = await orchestrator.ExecuteAsync(new WorkflowRequest(ReferenceCommand, DataClassification.Public));

        Assert.True(result.Success, result.DenialCode + ": " + result.DenialMessage);
        Assert.False(result.UsedModel);
        Assert.Equal(DeterministicCommandPlanner.ProviderName, result.PlannerProvider);
        Assert.Equal(1, cloud.Calls);
        Assert.Contains(result.PlanningNotes, note => note.Contains("fell back", StringComparison.Ordinal));
    }

    [Fact]
    public async Task UnsupportedCommandWithoutCloudIsDeniedWithoutBrokerCalls()
    {
        var (orchestrator, broker, _, cloud) = Create();
        var result = await orchestrator.ExecuteAsync(new WorkflowRequest("please do something unsupported", DataClassification.Public));

        Assert.False(result.Success);
        Assert.Equal(AetherErrorCodes.AiCommandUnsupported, result.DenialCode);
        Assert.Empty(broker.LaunchedApps);
        Assert.Equal(0, cloud.Calls);
    }

    [Fact]
    public async Task UnsupportedCommandWithCloudDeniedByPolicyReportsZeroExternalAttempts()
    {
        var (orchestrator, broker, _, cloud) = Create(withCloudPlanner: true);
        var result = await orchestrator.ExecuteAsync(new WorkflowRequest("please rewrite all customer records", DataClassification.Restricted));

        Assert.False(result.Success);
        Assert.Equal(AetherErrorCodes.PolicyCloudDenied, result.DenialCode);
        Assert.Equal(0, cloud.Calls);
        Assert.Empty(broker.LaunchedApps);
    }

    [Fact]
    public async Task SuppliedUnsafePlanIsDeniedBeforeAnySideEffect()
    {
        var (orchestrator, broker, audit, _) = Create();
        var unsafePlan = new Aether.Contracts.Models.ActionPlan
        {
            SchemaVersion = "1.0",
            PlanId = "plan-supplied-unsafe",
            Steps = [new Aether.Contracts.Models.ActionPlanStep { Action = "ExecShell", StatusText = "rm -rf /" }],
        };

        var result = await orchestrator.ExecuteAsync(new WorkflowRequest("", DataClassification.Restricted, SuppliedPlan: unsafePlan));

        Assert.False(result.Success);
        Assert.Equal(AetherErrorCodes.PlanActionUnknown, result.DenialCode);
        Assert.Empty(broker.LaunchedApps);
        Assert.True(audit.Verify().Valid);
    }

    [Fact]
    public async Task LaunchDenialFromBrokerIsPropagatedWithStepContext()
    {
        var (orchestrator, broker, _, _) = Create();
        broker.DenyLaunch = true;

        var result = await orchestrator.ExecuteAsync(new WorkflowRequest(ReferenceCommand, DataClassification.Restricted));

        Assert.False(result.Success);
        Assert.Equal(AetherErrorCodes.BrokerLaunchFailed, result.DenialCode);
        Assert.Single(broker.LaunchedApps);
        Assert.Equal(Aether.Execution.Results.StepState.Failed, result.Execution!.Steps[0].State);
    }

    [Fact]
    public async Task BrokerRpcFailureOnSnapshotBecomesStructuredWorkflowDenial()
    {
        var (orchestrator, broker, audit, _) = Create();
        broker.ThrowOnSnapshot = new Aether.Broker.Client.BrokerRpcException(
            AetherErrorCodes.AuthSessionInvalid, DenialCategory.PolicyDenied, Grpc.Core.StatusCode.Unauthenticated, "session expired");

        var result = await orchestrator.ExecuteAsync(new WorkflowRequest(ReferenceCommand, DataClassification.Restricted));

        Assert.False(result.Success);
        Assert.Equal(AetherErrorCodes.AuthSessionInvalid, result.DenialCode);
        Assert.True(audit.Verify().Valid);
    }

    [Fact]
    public async Task SummaryResolutionDenialIsPropagated()
    {
        var (orchestrator, broker, _, _) = Create();
        broker.DenySummaryResolution = true;

        var result = await orchestrator.ExecuteAsync(new WorkflowRequest(ReferenceCommand, DataClassification.Restricted));

        Assert.False(result.Success);
        Assert.Equal(AetherErrorCodes.AuthClassificationDenied, result.DenialCode);
        Assert.Null(result.Summary);
    }

    [Fact]
    public async Task UnknownClassificationCannotPublishRestrictedContext()
    {
        var (orchestrator, broker, _, _) = Create();
        var result = await orchestrator.ExecuteAsync(new WorkflowRequest(ReferenceCommand, DataClassification.Unknown));

        Assert.False(result.Success);
        Assert.Equal(DataClassification.Unknown, result.Classification);
        Assert.Equal(AetherErrorCodes.PolicyClassificationUnknown, result.DenialCode);
        Assert.Single(broker.PublishedContexts);
    }

    [Fact]
    public async Task CancellationIsReportedAsStructuredDenial()
    {
        var (orchestrator, _, _, _) = Create();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        var result = await orchestrator.ExecuteAsync(
            new WorkflowRequest(ReferenceCommand, DataClassification.Restricted), cancellation.Token);

        Assert.False(result.Success);
        Assert.Equal(AetherErrorCodes.WorkflowCancelled, result.DenialCode);
    }

    private sealed class FakeSummaryService : ISummaryService
    {
        public Task<SummaryResult> SummarizeAsync(SummaryRequest request, CancellationToken cancellationToken = default) =>
            SummarizePrivatelyAsync(request, cancellationToken);

        public Task<SummaryResult> SummarizePrivatelyAsync(SummaryRequest request, CancellationToken cancellationToken = default) =>
            Task.FromResult(SummaryResult.Ok("redacted summary", "fake-local-summary", request.Classification, 0));
    }
}
