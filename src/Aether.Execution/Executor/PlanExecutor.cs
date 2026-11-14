using System.Diagnostics;
using Aether.Contracts;
using Aether.Contracts.Errors;
using Aether.Contracts.Models;
using Aether.Execution.Actions;
using Aether.Execution.Authorization;
using Aether.Execution.Catalog;
using Aether.Execution.Results;
using Aether.Execution.Validation;
using Aether.Security.Audit;

namespace Aether.Execution.Executor;

/// <summary>
/// Deterministic plan executor: validate, authorize, execute bounded typed
/// steps, verify outcomes and audit metadata. The executor never interprets
/// natural language and never constructs commands from model output.
/// </summary>
public sealed class PlanExecutor
{
    private readonly ActionPlanValidator _validator;
    private readonly IPlanAuthorizationPolicy _authorizationPolicy;
    private readonly IReadOnlyDictionary<ActionKind, IActionHandler> _handlers;
    private readonly IAuditSink _auditSink;
    private readonly ExecutionOptions _options;
    private readonly TimeProvider _timeProvider;

    public PlanExecutor(
        ActionPlanValidator validator,
        IPlanAuthorizationPolicy authorizationPolicy,
        IReadOnlyDictionary<ActionKind, IActionHandler> handlers,
        IAuditSink auditSink,
        ExecutionOptions? options = null,
        TimeProvider? timeProvider = null)
    {
        _validator = validator ?? throw new ArgumentNullException(nameof(validator));
        _authorizationPolicy = authorizationPolicy ?? throw new ArgumentNullException(nameof(authorizationPolicy));
        _handlers = handlers ?? throw new ArgumentNullException(nameof(handlers));
        _auditSink = auditSink ?? throw new ArgumentNullException(nameof(auditSink));
        _options = options ?? new ExecutionOptions();
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public async Task<PlanExecutionResult> ExecuteAsync(
        ActionPlan? plan,
        PlanCatalog catalog,
        CapabilityGrant grant,
        TraceContext trace,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(grant);
        ArgumentNullException.ThrowIfNull(trace);

        var startedAtUtc = _timeProvider.GetUtcNow();
        var stopwatch = Stopwatch.StartNew();
        var singleStepOptions = new ActionPlanValidationOptions { MaxActions = _options.MaxActionsPerPlan };
        var validation = _validator.Validate(plan, singleStepOptions, catalog);

        if (plan is null || !validation.IsValid)
        {
            var firstIssue = validation.Issues.Count > 0
                ? validation.Issues[0]
                : new PlanValidationIssue(AetherErrorCodes.PlanInvalidJson, "Plan document is missing.", null);
            _auditSink.Append(new AuditEventDraft
            {
                TraceId = trace.TraceId,
                EventType = AuditEventTypes.PlanValidation,
                Outcome = AuditOutcomes.Denied,
                Code = firstIssue.Code,
                DurationMs = stopwatch.ElapsedMilliseconds,
            });

            return FailureResult(
                plan?.PlanId ?? "",
                trace.TraceId,
                steps: [],
                validation.Issues,
                firstIssue.Code,
                firstIssue.Message,
                startedAtUtc,
                stopwatch);
        }

        var planAuthorization = _authorizationPolicy.AuthorizePlan(grant);
        if (!planAuthorization.Authorized)
        {
            _auditSink.Append(new AuditEventDraft
            {
                TraceId = trace.TraceId,
                EventType = AuditEventTypes.ExecutionPlan,
                Outcome = AuditOutcomes.Denied,
                Code = planAuthorization.Code,
                DurationMs = stopwatch.ElapsedMilliseconds,
            });

            return FailureResult(
                plan!.PlanId,
                trace.TraceId,
                steps: [],
                validation.Issues,
                planAuthorization.Code,
                planAuthorization.Message,
                startedAtUtc,
                stopwatch);
        }

        var deadline = _timeProvider.GetUtcNow() + _options.PlanTimeout;
        var completedSteps = new List<MutableStep>();

        for (var index = 0; index < plan!.Steps.Count; index++)
        {
            var step = plan.Steps[index];
            var stepId = $"step-{index + 1}";
            var stepTrace = trace.NewSpan();
            var actionKind = ActionKinds.TryParse(step.Action, out var parsed) ? parsed : default;
            var mutable = new MutableStep(stepId, index, actionKind, step.Action);

            if (cancellationToken.IsCancellationRequested)
            {
                mutable.MarkFailed(AetherErrorCodes.ExecutionCancelled, "The workflow was cancelled before this step.");
                completedSteps.Add(mutable);
                return await FailureWithCompensationAsync(
                    plan.PlanId,
                    trace.TraceId,
                    completedSteps,
                    validation.Issues,
                    AetherErrorCodes.ExecutionCancelled,
                    "The workflow was cancelled.",
                    startedAtUtc,
                    stopwatch).ConfigureAwait(false);
            }

            if (_timeProvider.GetUtcNow() >= deadline)
            {
                mutable.MarkFailed(AetherErrorCodes.ExecutionDeadlineExceeded, "The plan exceeded its overall deadline.");
                completedSteps.Add(mutable);
                return await FailureWithCompensationAsync(
                    plan.PlanId,
                    trace.TraceId,
                    completedSteps,
                    validation.Issues,
                    AetherErrorCodes.ExecutionDeadlineExceeded,
                    "The plan exceeded its overall deadline.",
                    startedAtUtc,
                    stopwatch).ConfigureAwait(false);
            }

            var authorization = _authorizationPolicy.AuthorizeStep(
                index,
                step,
                new AuthorizationContext(grant, catalog));

            if (!authorization.Authorized)
            {
                mutable.MarkDenied(authorization.Code!, authorization.Message!);
                completedSteps.Add(mutable);
                return await FailureWithCompensationAsync(
                    plan.PlanId,
                    trace.TraceId,
                    completedSteps,
                    validation.Issues,
                    authorization.Code,
                    authorization.Message,
                    startedAtUtc,
                    stopwatch).ConfigureAwait(false);
            }

            if (!_handlers.TryGetValue(actionKind, out var handler))
            {
                mutable.MarkFailed(AetherErrorCodes.ExecutionInternalError, "No handler is registered for this action.");
                completedSteps.Add(mutable);
                return await FailureWithCompensationAsync(
                    plan.PlanId,
                    trace.TraceId,
                    completedSteps,
                    validation.Issues,
                    AetherErrorCodes.ExecutionInternalError,
                    "The action is not executable in this build.",
                    startedAtUtc,
                    stopwatch).ConfigureAwait(false);
            }

            var request = new ActionExecutionRequest(plan.PlanId, trace.TraceId, stepTrace.SpanId, stepId, index, step);
            mutable.AttachHandler(handler, request);
            mutable.MarkAuthorized();
            mutable.MarkRunning();

            var stepStopwatch = Stopwatch.StartNew();
            HandlerResult handlerResult;
            try
            {
                using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                var remaining = deadline - _timeProvider.GetUtcNow();
                var stepTimeout = remaining < _options.StepTimeout ? remaining : _options.StepTimeout;
                if (stepTimeout <= TimeSpan.Zero)
                {
                    throw new TimeoutException();
                }

                timeoutSource.CancelAfter(stepTimeout);
                handlerResult = await handler.ExecuteAsync(request, timeoutSource.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                stepStopwatch.Stop();
                mutable.MarkFailed(AetherErrorCodes.ExecutionCancelled, "The step was cancelled.");
                completedSteps.Add(mutable);
                AppendStepAudit(trace.TraceId, mutable, stepStopwatch.ElapsedMilliseconds);
                return await FailureWithCompensationAsync(
                    plan.PlanId,
                    trace.TraceId,
                    completedSteps,
                    validation.Issues,
                    AetherErrorCodes.ExecutionCancelled,
                    "The workflow was cancelled.",
                    startedAtUtc,
                    stopwatch).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                stepStopwatch.Stop();
                mutable.MarkFailed(AetherErrorCodes.ExecutionDeadlineExceeded, "The step exceeded its deadline.");
                completedSteps.Add(mutable);
                AppendStepAudit(trace.TraceId, mutable, stepStopwatch.ElapsedMilliseconds);
                return await FailureWithCompensationAsync(
                    plan.PlanId,
                    trace.TraceId,
                    completedSteps,
                    validation.Issues,
                    AetherErrorCodes.ExecutionDeadlineExceeded,
                    "The step exceeded its deadline.",
                    startedAtUtc,
                    stopwatch).ConfigureAwait(false);
            }
            catch (TimeoutException)
            {
                stepStopwatch.Stop();
                mutable.MarkFailed(AetherErrorCodes.ExecutionDeadlineExceeded, "The step exceeded its deadline.");
                completedSteps.Add(mutable);
                AppendStepAudit(trace.TraceId, mutable, stepStopwatch.ElapsedMilliseconds);
                return await FailureWithCompensationAsync(
                    plan.PlanId,
                    trace.TraceId,
                    completedSteps,
                    validation.Issues,
                    AetherErrorCodes.ExecutionDeadlineExceeded,
                    "The step exceeded its deadline.",
                    startedAtUtc,
                    stopwatch).ConfigureAwait(false);
            }
            catch (Exception)
            {
                stepStopwatch.Stop();
                mutable.MarkFailed(AetherErrorCodes.ExecutionFailed, "The step failed with an internal error.");
                completedSteps.Add(mutable);
                AppendStepAudit(trace.TraceId, mutable, stepStopwatch.ElapsedMilliseconds);
                return await FailureWithCompensationAsync(
                    plan.PlanId,
                    trace.TraceId,
                    completedSteps,
                    validation.Issues,
                    AetherErrorCodes.ExecutionFailed,
                    "The step failed with an internal error.",
                    startedAtUtc,
                    stopwatch).ConfigureAwait(false);
            }

            stepStopwatch.Stop();
            if (!handlerResult.Success)
            {
                var failureCode = string.IsNullOrWhiteSpace(handlerResult.ErrorCode)
                    ? AetherErrorCodes.ExecutionFailed
                    : handlerResult.ErrorCode;
                var failureMessage = string.IsNullOrWhiteSpace(handlerResult.Message)
                    ? "The step failed."
                    : handlerResult.Message;
                mutable.MarkFailed(failureCode, failureMessage);
                completedSteps.Add(mutable);
                AppendStepAudit(trace.TraceId, mutable, stepStopwatch.ElapsedMilliseconds);
                return await FailureWithCompensationAsync(
                    plan.PlanId,
                    trace.TraceId,
                    completedSteps,
                    validation.Issues,
                    failureCode,
                    failureMessage,
                    startedAtUtc,
                    stopwatch).ConfigureAwait(false);
            }

            mutable.MarkSucceeded(handlerResult.Artifacts);
            completedSteps.Add(mutable);
            AppendStepAudit(trace.TraceId, mutable, stepStopwatch.ElapsedMilliseconds);
        }

        _auditSink.Append(new AuditEventDraft
        {
            TraceId = trace.TraceId,
            EventType = AuditEventTypes.ExecutionPlan,
            Outcome = AuditOutcomes.Succeeded,
            DurationMs = stopwatch.ElapsedMilliseconds,
        });

        stopwatch.Stop();
        var completedAt = _timeProvider.GetUtcNow();
        return new PlanExecutionResult(
            plan.PlanId,
            trace.TraceId,
            Succeeded: true,
            Steps: completedSteps.Select(step => step.ToResult()).ToArray(),
            ValidationIssues: validation.Issues,
            DenialCode: null,
            DenialMessage: null,
            DenialCategory: DenialCategory.Unknown,
            StartedAtUtc: startedAtUtc,
            CompletedAtUtc: completedAt,
            DurationMs: stopwatch.ElapsedMilliseconds);
    }

    private async Task<PlanExecutionResult> FailureWithCompensationAsync(
        string planId,
        string traceId,
        List<MutableStep> completedSteps,
        IReadOnlyList<PlanValidationIssue> validationIssues,
        string? code,
        string? message,
        DateTimeOffset startedAtUtc,
        Stopwatch stopwatch)
    {
        if (_options.ExecuteCompensationsOnFailure)
        {
            for (var index = completedSteps.Count - 1; index >= 0; index--)
            {
                var step = completedSteps[index];
                if (!step.WasSucceeded || step.Handler is null || step.Request is null)
                {
                    continue;
                }

                try
                {
                    using var timeoutSource = new CancellationTokenSource(_options.CompensationTimeout);
                    var compensation = await step.Handler.CompensateAsync(step.Request, timeoutSource.Token).ConfigureAwait(false);
                    if (compensation.Success)
                    {
                        step.MarkCompensated(compensation.Message);
                        AppendStepAudit(traceId, step, 0);
                    }
                }
                catch (Exception)
                {
                    // Compensation is best-effort; the original failure stays authoritative.
                }
            }
        }

        return FailureResult(
            planId,
            traceId,
            completedSteps.Select(step => step.ToResult()).ToArray(),
            validationIssues,
            code,
            message,
            startedAtUtc,
            stopwatch);
    }

    private PlanExecutionResult FailureResult(
        string planId,
        string traceId,
        IReadOnlyList<StepExecutionResult> steps,
        IReadOnlyList<PlanValidationIssue> validationIssues,
        string? code,
        string? message,
        DateTimeOffset startedAtUtc,
        Stopwatch stopwatch)
    {
        stopwatch.Stop();
        var completedAt = _timeProvider.GetUtcNow();
        return new PlanExecutionResult(
            planId,
            traceId,
            Succeeded: false,
            Steps: steps,
            ValidationIssues: validationIssues,
            DenialCode: code,
            DenialMessage: message,
            DenialCategory: AetherErrorCodes.GetDenialCategory(code),
            StartedAtUtc: startedAtUtc,
            CompletedAtUtc: completedAt,
            DurationMs: stopwatch.ElapsedMilliseconds);
    }

    private void AppendStepAudit(string traceId, MutableStep step, long durationMs) =>
        _auditSink.Append(new AuditEventDraft
        {
            TraceId = traceId,
            EventType = AuditEventTypes.ExecutionStep,
            Outcome = step.State switch
            {
                StepState.Succeeded => AuditOutcomes.Succeeded,
                StepState.Denied => AuditOutcomes.Denied,
                StepState.Compensated => AuditOutcomes.Info,
                _ => AuditOutcomes.Failed,
            },
            Code = step.Code,
            DurationMs = durationMs,
            BrokeredAppId = step.Action,
        });

    private sealed class MutableStep
    {
        private readonly List<StepState> _states = [StepState.Pending];

        public MutableStep(string stepId, int index, ActionKind kind, string action)
        {
            StepId = stepId;
            Index = index;
            Kind = kind;
            Action = action;
        }

        public string StepId { get; }

        public int Index { get; }

        public ActionKind Kind { get; }

        public string Action { get; }

        public StepState State { get; private set; } = StepState.Pending;

        public string? Code { get; private set; }

        public string Message { get; private set; } = "";

        public IReadOnlyDictionary<string, string>? Artifacts { get; private set; }

        public ActionExecutionRequest? Request { get; private set; }

        public IActionHandler? Handler { get; private set; }

        public bool WasSucceeded => _states.Contains(StepState.Succeeded);

        public void AttachHandler(IActionHandler handler, ActionExecutionRequest request)
        {
            Handler = handler;
            Request = request;
        }

        public void MarkAuthorized()
        {
            _states.Add(StepState.Authorized);
            State = StepState.Authorized;
        }

        public void MarkRunning()
        {
            _states.Add(StepState.Running);
            State = StepState.Running;
        }

        public void MarkSucceeded(IReadOnlyDictionary<string, string>? artifacts)
        {
            _states.Add(StepState.Succeeded);
            State = StepState.Succeeded;
            Artifacts = artifacts;
        }

        public void MarkDenied(string code, string message)
        {
            _states.Add(StepState.Denied);
            State = StepState.Denied;
            Code = code;
            Message = message;
        }

        public void MarkFailed(string? code, string message)
        {
            _states.Add(StepState.Failed);
            State = StepState.Failed;
            Code = code;
            Message = message;
        }

        public void MarkCompensated(string message)
        {
            _states.Add(StepState.Compensated);
            State = StepState.Compensated;
            if (!string.IsNullOrEmpty(message))
            {
                Message = message;
            }
        }

        public StepExecutionResult ToResult() => new(
            StepId,
            Index,
            Kind,
            Action,
            State,
            Code,
            Message,
            Artifacts,
            _states.ToArray());
    }
}
