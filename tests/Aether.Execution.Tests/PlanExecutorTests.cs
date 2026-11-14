using Aether.Contracts;
using Aether.Contracts.Errors;
using Aether.Contracts.Models;
using Aether.Execution.Actions;
using Aether.Execution.Authorization;
using Aether.Execution.Executor;
using Aether.Execution.Results;
using Aether.Execution.Validation;
using Aether.Security.Audit;
using Aether.Testing;
using Xunit;

namespace Aether.Execution.Tests;

public class PlanExecutorTests
{
    private static readonly Aether.Execution.Catalog.PlanCatalog Catalog = TestData.CreatePlanCatalog();

    private static ActionPlan StatusPlan(params string[] texts) => new()
    {
        SchemaVersion = "1.0",
        PlanId = "plan-executor-test",
        Steps = texts.Select(text => new ActionPlanStep { Action = ActionKinds.ShowStatus, StatusText = text }).ToArray(),
    };

    private static (PlanExecutor Executor, Dictionary<ActionKind, RecordingActionHandler> Handlers, HashChainAuditLog Audit) CreateExecutor(
        ExecutionOptions? options = null,
        bool success = true,
        params ActionKind[] actions)
    {
        var handlers = new Dictionary<ActionKind, RecordingActionHandler>();
        foreach (var action in actions.Length == 0 ? [ActionKind.ShowStatus] : actions)
        {
            handlers[action] = new RecordingActionHandler(action, success);
        }

        var audit = new HashChainAuditLog();
        var executor = new PlanExecutor(
            new ActionPlanValidator(new ActionPlanValidationOptions { MaxActions = 6 }),
            new CapabilityPlanAuthorizationPolicy(),
            handlers.ToDictionary(pair => pair.Key, pair => (IActionHandler)pair.Value),
            audit,
            options ?? new ExecutionOptions { MaxActionsPerPlan = 6 },
            TimeProvider.System);
        return (executor, handlers, audit);
    }

    [Fact]
    public async Task SuccessfulPlanTransitionsThroughExplicitStates()
    {
        var (executor, handlers, audit) = CreateExecutor();
        var plan = StatusPlan("one", "two");
        var result = await executor.ExecuteAsync(plan, Catalog, TestData.OperatorGrant(), TraceContext.NewRoot());

        Assert.True(result.Succeeded);
        Assert.Null(result.DenialCode);
        Assert.Equal(2, result.Steps.Count);
        Assert.All(result.Steps, step => Assert.Equal(StepState.Succeeded, step.State));
        Assert.Equal([StepState.Pending, StepState.Authorized, StepState.Running, StepState.Succeeded], result.Steps[0].Transitions);
        Assert.Equal(2, handlers[ActionKind.ShowStatus].Executions.Count);
        Assert.All(handlers[ActionKind.ShowStatus].Executions, request => Assert.Equal("plan-executor-test", request.PlanId));
        Assert.True(audit.Verify().Valid);
        Assert.Contains(audit.Snapshot(), entry => entry.EventType == AuditEventTypes.ExecutionStep);
        Assert.Contains(audit.Snapshot(), entry => entry.EventType == AuditEventTypes.ExecutionPlan && entry.Outcome == AuditOutcomes.Succeeded);
    }

    [Fact]
    public async Task MissingPlanCapabilityDeniesBeforeAnyStepRuns()
    {
        var (executor, handlers, _) = CreateExecutor();
        var grant = TestData.OperatorGrant() with
        {
            Capabilities = new HashSet<string>(StringComparer.Ordinal) { "context.read" },
        };

        var result = await executor.ExecuteAsync(StatusPlan("hi"), Catalog, grant, TraceContext.NewRoot());

        Assert.False(result.Succeeded);
        Assert.Equal(AetherErrorCodes.AuthCapabilityMissing, result.DenialCode);
        Assert.Empty(result.Steps);
        Assert.Empty(handlers[ActionKind.ShowStatus].Executions);
    }

    [Fact]
    public async Task StepAuthorizationDenialBlocksThatStepAndKeepsNoSideEffects()
    {
        var (executor, handlers, audit) = CreateExecutor(actions: [ActionKind.LaunchApp]);
        var plan = new ActionPlan
        {
            SchemaVersion = "1.0",
            PlanId = "plan-launch-denied",
            Steps = [new ActionPlanStep { Action = ActionKinds.LaunchApp, AppId = "sample.watchlist" }],
        };
        var grant = TestData.OperatorGrant() with
        {
            Capabilities = new HashSet<string>(StringComparer.Ordinal) { "plan.execute" },
        };

        var result = await executor.ExecuteAsync(plan, Catalog, grant, TraceContext.NewRoot());

        Assert.False(result.Succeeded);
        Assert.Equal(AetherErrorCodes.AuthCapabilityMissing, result.DenialCode);
        Assert.Single(result.Steps);
        Assert.Equal(StepState.Denied, result.Steps[0].State);
        Assert.Empty(handlers[ActionKind.LaunchApp].Executions);
        Assert.True(audit.Verify().Valid);
    }

    [Fact]
    public async Task HandlerFailureTriggersCompensationOfEarlierSteps()
    {
        var failing = new RecordingActionHandler(ActionKind.PublishContext, succeed: false);
        var succeeding = new RecordingActionHandler(ActionKind.LaunchApp, succeed: true);
        var audit = new HashChainAuditLog();
        var executor = new PlanExecutor(
            new ActionPlanValidator(new ActionPlanValidationOptions { MaxActions = 6 }),
            new CapabilityPlanAuthorizationPolicy(),
            new Dictionary<ActionKind, IActionHandler>
            {
                [ActionKind.LaunchApp] = succeeding,
                [ActionKind.PublishContext] = failing,
            },
            audit,
            new ExecutionOptions { MaxActionsPerPlan = 6 });

        var plan = new ActionPlan
        {
            SchemaVersion = "1.0",
            PlanId = "plan-compensate",
            Steps =
            [
                new ActionPlanStep { Action = ActionKinds.LaunchApp, AppId = "sample.watchlist" },
                new ActionPlanStep { Action = ActionKinds.PublishContext, ContextRef = "synthetic.instrument.northstar" },
            ],
        };

        var result = await executor.ExecuteAsync(plan, Catalog, TestData.OperatorGrant(), TraceContext.NewRoot());

        Assert.False(result.Succeeded);
        Assert.Equal("test.handler.failed", result.DenialCode);
        Assert.Equal(StepState.Compensated, result.Steps[0].State);
        Assert.Equal(StepState.Failed, result.Steps[1].State);
        Assert.Single(succeeding.Compensations);
        Assert.True(audit.Verify().Valid);
    }

    [Fact]
    public async Task HandlerExceptionBecomesStructuredFailure()
    {
        var throwing = new ThrowingHandler();
        var executor = new PlanExecutor(
            new ActionPlanValidator(new ActionPlanValidationOptions { MaxActions = 6 }),
            new CapabilityPlanAuthorizationPolicy(),
            new Dictionary<ActionKind, IActionHandler> { [ActionKind.ShowStatus] = throwing },
            new HashChainAuditLog(),
            new ExecutionOptions { MaxActionsPerPlan = 6 });

        var result = await executor.ExecuteAsync(StatusPlan("hi"), Catalog, TestData.OperatorGrant(), TraceContext.NewRoot());

        Assert.False(result.Succeeded);
        Assert.Equal(AetherErrorCodes.ExecutionFailed, result.DenialCode);
        Assert.Equal(StepState.Failed, result.Steps[0].State);
    }

    [Fact]
    public async Task StepDeadlineIsEnforced()
    {
        var slow = new SlowHandler(TimeSpan.FromSeconds(5));
        var executor = new PlanExecutor(
            new ActionPlanValidator(new ActionPlanValidationOptions { MaxActions = 6 }),
            new CapabilityPlanAuthorizationPolicy(),
            new Dictionary<ActionKind, IActionHandler> { [ActionKind.ShowStatus] = slow },
            new HashChainAuditLog(),
            new ExecutionOptions { MaxActionsPerPlan = 6, StepTimeout = TimeSpan.FromMilliseconds(50), PlanTimeout = TimeSpan.FromSeconds(5) });

        var result = await executor.ExecuteAsync(StatusPlan("hi"), Catalog, TestData.OperatorGrant(), TraceContext.NewRoot());

        Assert.False(result.Succeeded);
        Assert.Equal(AetherErrorCodes.ExecutionDeadlineExceeded, result.DenialCode);
        Assert.True(result.DurationMs < 2000);
    }

    [Fact]
    public async Task PreCancelledWorkflowIsDeniedWithoutHandlerExecution()
    {
        var (executor, handlers, _) = CreateExecutor();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        var result = await executor.ExecuteAsync(StatusPlan("hi"), Catalog, TestData.OperatorGrant(), TraceContext.NewRoot(), cancellation.Token);

        Assert.False(result.Succeeded);
        Assert.Equal(AetherErrorCodes.ExecutionCancelled, result.DenialCode);
        Assert.Empty(handlers[ActionKind.ShowStatus].Executions);
    }

    [Fact]
    public async Task InvalidPlanIsRejectedInsideExecutorWithoutHandlerExecution()
    {
        var (executor, handlers, _) = CreateExecutor();
        var invalid = new ActionPlan
        {
            SchemaVersion = "2.0",
            PlanId = "plan-invalid",
            Steps = [new ActionPlanStep { Action = ActionKinds.ShowStatus, StatusText = "hi" }],
        };

        var result = await executor.ExecuteAsync(invalid, Catalog, TestData.OperatorGrant(), TraceContext.NewRoot());

        Assert.False(result.Succeeded);
        Assert.Equal(AetherErrorCodes.PlanSchemaVersionUnsupported, result.DenialCode);
        Assert.Empty(result.Steps);
        Assert.Empty(handlers[ActionKind.ShowStatus].Executions);
    }

    [Fact]
    public async Task ConfiguredActionBudgetIsEnforcedByExecutor()
    {
        var (executor, handlers, _) = CreateExecutor(new ExecutionOptions { MaxActionsPerPlan = 2 });
        var plan = StatusPlan("a", "b", "c");

        var result = await executor.ExecuteAsync(plan, Catalog, TestData.OperatorGrant(), TraceContext.NewRoot());

        Assert.False(result.Succeeded);
        Assert.Equal(AetherErrorCodes.PlanStepsTooMany, result.DenialCode);
        Assert.Empty(handlers[ActionKind.ShowStatus].Executions);
    }

    [Fact]
    public async Task EachStepCarriesTheWorkflowTraceId()
    {
        var (executor, handlers, _) = CreateExecutor();
        var trace = TraceContext.NewRoot();

        await executor.ExecuteAsync(StatusPlan("a", "b"), Catalog, TestData.OperatorGrant(), trace);

        Assert.All(handlers[ActionKind.ShowStatus].Executions, request => Assert.Equal(trace.TraceId, request.TraceId));
        Assert.NotEqual(handlers[ActionKind.ShowStatus].Executions[0].SpanId, handlers[ActionKind.ShowStatus].Executions[1].SpanId);
    }

    private sealed class ThrowingHandler : IActionHandler
    {
        public ActionKind Kind => ActionKind.ShowStatus;

        public Task<HandlerResult> ExecuteAsync(ActionExecutionRequest request, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("simulated handler fault");

        public Task<CompensationResult> CompensateAsync(ActionExecutionRequest request, CancellationToken cancellationToken) =>
            Task.FromResult(CompensationResult.Succeeded());
    }

    private sealed class SlowHandler : IActionHandler
    {
        private readonly TimeSpan _delay;

        public SlowHandler(TimeSpan delay) => _delay = delay;

        public ActionKind Kind => ActionKind.ShowStatus;

        public async Task<HandlerResult> ExecuteAsync(ActionExecutionRequest request, CancellationToken cancellationToken)
        {
            await Task.Delay(_delay, cancellationToken);
            return HandlerResult.Ok();
        }

        public Task<CompensationResult> CompensateAsync(ActionExecutionRequest request, CancellationToken cancellationToken) =>
            Task.FromResult(CompensationResult.Succeeded());
    }
}
