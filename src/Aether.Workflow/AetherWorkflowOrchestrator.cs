using System.Diagnostics;
using System.Globalization;
using System.Text;
using Aether.AI.Planner;
using Aether.AI.Summarization;
using Aether.Broker.Client;
using Aether.Contracts;
using Aether.Contracts.Errors;
using Aether.Contracts.Models;
using Aether.Execution.Actions;
using Aether.Execution.Authorization;
using Aether.Execution.Catalog;
using Aether.Execution.Executor;
using Aether.Execution.Results;
using Aether.Execution.Validation;
using Aether.Security.Audit;
using Aether.Security.Classification;
using Aether.Workflow.Handlers;

namespace Aether.Workflow;

/// <summary>
/// End-to-end workflow orchestration: classify trusted source data, obtain an
/// untrusted plan proposal (deterministic grammar first, optional cloud adapter),
/// validate schema/allowlist/limits, authorize capabilities, execute bounded
/// typed steps, summarize privately and audit metadata. No model output is ever
/// executed directly.
/// </summary>
public sealed class AetherWorkflowOrchestrator
{
    private readonly IDataClassifier _classifier;
    private readonly IPlanGenerator _deterministicPlanner;
    private readonly IPlanGenerator? _cloudPlanner;
    private readonly IWorkflowBroker _broker;
    private readonly ISummaryService _summaryService;
    private readonly ActionPlanValidator _validator;
    private readonly IPlanAuthorizationPolicy _authorizationPolicy;
    private readonly WorkflowOrchestratorOptions _options;
    private readonly IAuditSink _auditSink;
    private readonly TimeProvider _timeProvider;

    public AetherWorkflowOrchestrator(
        IDataClassifier classifier,
        IPlanGenerator deterministicPlanner,
        IPlanGenerator? cloudPlanner,
        IWorkflowBroker broker,
        ISummaryService summaryService,
        ActionPlanValidator validator,
        IPlanAuthorizationPolicy authorizationPolicy,
        WorkflowOrchestratorOptions options,
        IAuditSink auditSink,
        TimeProvider? timeProvider = null)
    {
        _classifier = classifier ?? throw new ArgumentNullException(nameof(classifier));
        _deterministicPlanner = deterministicPlanner ?? throw new ArgumentNullException(nameof(deterministicPlanner));
        _cloudPlanner = cloudPlanner;
        _broker = broker ?? throw new ArgumentNullException(nameof(broker));
        _summaryService = summaryService ?? throw new ArgumentNullException(nameof(summaryService));
        _validator = validator ?? throw new ArgumentNullException(nameof(validator));
        _authorizationPolicy = authorizationPolicy ?? throw new ArgumentNullException(nameof(authorizationPolicy));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _auditSink = auditSink ?? throw new ArgumentNullException(nameof(auditSink));
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public async Task<WorkflowResult> ExecuteAsync(WorkflowRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var stopwatch = Stopwatch.StartNew();
        var traceId = TraceContext.IsValidTraceId(request.TraceId) ? request.TraceId! : TraceIdGenerator.NewTraceId();
        var trace = new TraceContext(traceId, TraceIdGenerator.NewSpanId());

        if (cancellationToken.IsCancellationRequested)
        {
            stopwatch.Stop();
            _auditSink.Append(new AuditEventDraft
            {
                TraceId = traceId,
                EventType = AuditEventTypes.WorkflowResult,
                Outcome = AuditOutcomes.Cancelled,
                Code = AetherErrorCodes.WorkflowCancelled,
                Classification = request.SourceClassification.ToWireName(),
            });
            return WorkflowResult.Denied(
                traceId,
                request.SourceClassification,
                [],
                AetherErrorCodes.WorkflowCancelled,
                "The workflow was cancelled before execution.",
                "none",
                usedModel: false,
                stopwatch.ElapsedMilliseconds);
        }

        var classification = _classifier.Classify(new ClassificationInput(
            request.Command,
            request.SourceClassification,
            request.SourceAppId));
        AppendClassificationAudit(traceId, classification, request.Command);

        if (classification.Classification == DataClassification.Unknown)
        {
            // Fail-closed: an unknown classification may still use the deterministic
            // local path, but publishing classified context is denied by the broker.
            _auditSink.Append(new AuditEventDraft
            {
                TraceId = traceId,
                EventType = AuditEventTypes.Classification,
                Outcome = AuditOutcomes.Info,
                Code = AetherErrorCodes.PolicyClassificationUnknown,
                Classification = DataClassification.Unknown.ToWireName(),
            });
        }

        ActionPlan? plan = request.SuppliedPlan;
        var plannerProvider = "supplied-plan";
        var usedModel = false;
        var planningNotes = new List<string>();

        if (plan is null && _options.PlanningMode == PlanGenerationMode.CloudPreferred && _cloudPlanner is not null)
        {
            var cloudPreferred = await _cloudPlanner
                .GeneratePlanAsync(new PlanningRequest(request.Command, classification.Classification, traceId), cancellationToken)
                .ConfigureAwait(false);
            if (cloudPreferred.Success && cloudPreferred.Plan is not null)
            {
                plan = cloudPreferred.Plan;
                plannerProvider = cloudPreferred.Provider;
                usedModel = cloudPreferred.UsedModel;
            }
            else
            {
                planningNotes.Add(
                    "cloud planning failed ("
                    + (cloudPreferred.ErrorCode ?? "unknown")
                    + "); fell back to "
                    + _deterministicPlanner.ProviderId
                    + " with usedModel=false");
                // Explicit LLM mode must not silently relabel a deterministic plan as model output.
                var fallback = await _deterministicPlanner
                    .GeneratePlanAsync(new PlanningRequest(request.Command, classification.Classification, traceId), cancellationToken)
                    .ConfigureAwait(false);
                if (fallback.Success && fallback.Plan is not null)
                {
                    plan = fallback.Plan;
                    plannerProvider = fallback.Provider;
                    usedModel = fallback.UsedModel;
                }
                else
                {
                    stopwatch.Stop();
                    var denialCode = cloudPreferred.ErrorCode ?? fallback.ErrorCode ?? AetherErrorCodes.AiCommandUnsupported;
                    AppendWorkflowAudit(traceId, classification, AuditOutcomes.Denied, denialCode, stopwatch.ElapsedMilliseconds);
                    return WorkflowResult.Denied(
                        traceId,
                        classification.Classification,
                        classification.ReasonCodes,
                        denialCode,
                        cloudPreferred.Message ?? fallback.Message ?? "The command could not be converted into an allowed plan.",
                        cloudPreferred.Provider,
                        usedModel: false,
                        stopwatch.ElapsedMilliseconds);
                }
            }
        }

        if (plan is null)
        {
            var deterministic = await _deterministicPlanner
                .GeneratePlanAsync(new PlanningRequest(request.Command, classification.Classification, traceId), cancellationToken)
                .ConfigureAwait(false);

            if (deterministic.Success && deterministic.Plan is not null)
            {
                plan = deterministic.Plan;
                plannerProvider = deterministic.Provider;
                usedModel = deterministic.UsedModel;
            }
            else if (_cloudPlanner is not null)
            {
                var cloud = await _cloudPlanner
                    .GeneratePlanAsync(new PlanningRequest(request.Command, classification.Classification, traceId), cancellationToken)
                    .ConfigureAwait(false);
                if (!cloud.Success || cloud.Plan is null)
                {
                    stopwatch.Stop();
                    var denialCode = cloud.ErrorCode ?? AetherErrorCodes.AiCommandUnsupported;
                    AppendWorkflowAudit(traceId, classification, AuditOutcomes.Denied, denialCode, stopwatch.ElapsedMilliseconds);
                    return WorkflowResult.Denied(
                        traceId,
                        classification.Classification,
                        classification.ReasonCodes,
                        denialCode,
                        cloud.Message ?? "The command could not be converted into an allowed plan.",
                        cloud.Provider,
                        usedModel: false,
                        stopwatch.ElapsedMilliseconds);
                }

                plan = cloud.Plan;
                plannerProvider = cloud.Provider;
                usedModel = cloud.UsedModel;
            }
            else
            {
                stopwatch.Stop();
                var denialCode = deterministic.ErrorCode ?? AetherErrorCodes.AiCommandUnsupported;
                AppendWorkflowAudit(traceId, classification, AuditOutcomes.Denied, denialCode, stopwatch.ElapsedMilliseconds);
                return WorkflowResult.Denied(
                    traceId,
                    classification.Classification,
                    classification.ReasonCodes,
                    denialCode,
                    deterministic.Message ?? "The command could not be converted into an allowed plan.",
                    deterministic.Provider,
                    usedModel: false,
                    stopwatch.ElapsedMilliseconds);
            }
        }

        try
        {
            var snapshot = await _broker.GetSnapshotAsync(traceId, cancellationToken).ConfigureAwait(false);
            var catalog = PlanCatalogMapper.FromSnapshot(snapshot);
            var grant = PlanCatalogMapper.FromRegistration(_broker.Registration);

            var validationOptions = new ActionPlanValidationOptions { MaxActions = _options.MaxActionsPerPlan };
            var validation = _validator.Validate(plan, validationOptions, catalog);
            if (!validation.IsValid)
            {
                var firstIssue = validation.Issues[0];
                stopwatch.Stop();
                _auditSink.Append(new AuditEventDraft
                {
                    TraceId = traceId,
                    EventType = AuditEventTypes.PlanValidation,
                    Outcome = AuditOutcomes.Denied,
                    Code = firstIssue.Code,
                    DurationMs = stopwatch.ElapsedMilliseconds,
                });
                AppendWorkflowAudit(traceId, classification, AuditOutcomes.Denied, firstIssue.Code, stopwatch.ElapsedMilliseconds);
                return WorkflowResult.Denied(
                    traceId,
                    classification.Classification,
                    classification.ReasonCodes,
                    firstIssue.Code,
                    firstIssue.Message,
                    plannerProvider,
                    usedModel,
                    stopwatch.ElapsedMilliseconds,
                    plan: plan);
            }

            var handlers = BuildHandlers(classification.Classification, catalog);
            var executionOptions = _options.Execution with { MaxActionsPerPlan = _options.MaxActionsPerPlan };
            var executor = new PlanExecutor(_validator, _authorizationPolicy, handlers, _auditSink, executionOptions, _timeProvider);
            var execution = await executor.ExecuteAsync(plan, catalog, grant, trace, cancellationToken).ConfigureAwait(false);

            stopwatch.Stop();
            var summary = ExtractSummary(execution);
            var succeeded = execution.Succeeded;
            AppendWorkflowAudit(
                traceId,
                classification,
                succeeded ? AuditOutcomes.Succeeded : AuditOutcomes.Denied,
                execution.DenialCode,
                stopwatch.ElapsedMilliseconds);

            return new WorkflowResult(
                succeeded,
                traceId,
                plan.PlanId,
                plan,
                classification.Classification,
                classification.ReasonCodes,
                execution,
                summary,
                execution.DenialCode,
                execution.DenialMessage,
                execution.DenialCategory,
                plannerProvider,
                usedModel,
                stopwatch.ElapsedMilliseconds)
            {
                PlanningNotes = planningNotes,
            };
        }
        catch (BrokerRpcException exception)
        {
            stopwatch.Stop();
            AppendWorkflowAudit(traceId, classification, AuditOutcomes.Failed, exception.Code, stopwatch.ElapsedMilliseconds);
            return WorkflowResult.Denied(
                traceId,
                classification.Classification,
                classification.ReasonCodes,
                exception.Code,
                exception.Message,
                plannerProvider,
                usedModel,
                stopwatch.ElapsedMilliseconds,
                plan: plan);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            stopwatch.Stop();
            AppendWorkflowAudit(traceId, classification, AuditOutcomes.Cancelled, AetherErrorCodes.WorkflowCancelled, stopwatch.ElapsedMilliseconds);
            return WorkflowResult.Denied(
                traceId,
                classification.Classification,
                classification.ReasonCodes,
                AetherErrorCodes.WorkflowCancelled,
                "The workflow was cancelled.",
                plannerProvider,
                usedModel,
                stopwatch.ElapsedMilliseconds,
                plan: plan);
        }
        catch (Exception)
        {
            stopwatch.Stop();
            AppendWorkflowAudit(traceId, classification, AuditOutcomes.Failed, AetherErrorCodes.WorkflowFailed, stopwatch.ElapsedMilliseconds);
            return WorkflowResult.Denied(
                traceId,
                classification.Classification,
                classification.ReasonCodes,
                AetherErrorCodes.WorkflowFailed,
                "The workflow failed with an internal error.",
                plannerProvider,
                usedModel,
                stopwatch.ElapsedMilliseconds,
                plan: plan);
        }
    }

    private IReadOnlyDictionary<ActionKind, IActionHandler> BuildHandlers(DataClassification classification, PlanCatalog catalog) =>
        new Dictionary<ActionKind, IActionHandler>
        {
            [ActionKind.LaunchApp] = new BrokerLaunchHandler(_broker),
            [ActionKind.PublishContext] = new BrokerPublishContextHandler(_broker, classification),
            [ActionKind.ArrangeWindows] = new BrokerArrangeWindowsHandler(_broker),
            [ActionKind.SummarizeContext] = new SummarizeContextActionHandler(_broker, _summaryService),
            [ActionKind.ShowStatus] = new ShowStatusHandler(),
        };

    private static SummaryOutcome? ExtractSummary(PlanExecutionResult execution)
    {
        var step = execution.Steps.FirstOrDefault(candidate =>
            candidate.Kind == ActionKind.SummarizeContext &&
            candidate.State == StepState.Succeeded &&
            candidate.Artifacts is not null);
        if (step?.Artifacts is null ||
            !step.Artifacts.TryGetValue("summary", out var summary) ||
            !step.Artifacts.TryGetValue("provider", out var provider))
        {
            return null;
        }

        var classification = step.Artifacts.TryGetValue("classification", out var wire)
            ? DataClassificationExtensions.ParseOrUnknown(wire)
            : DataClassification.Unknown;
        var redactionCount = step.Artifacts.TryGetValue("redactionCount", out var redactionText) &&
                             int.TryParse(redactionText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : 0;

        return new SummaryOutcome(summary, provider, classification, redactionCount);
    }

    private void AppendClassificationAudit(string traceId, ClassificationResult classification, string content)
    {
        _auditSink.Append(new AuditEventDraft
        {
            TraceId = traceId,
            EventType = AuditEventTypes.Classification,
            Outcome = classification.Classification == DataClassification.Unknown ? AuditOutcomes.Denied : AuditOutcomes.Info,
            Code = classification.Classification == DataClassification.Unknown ? AetherErrorCodes.PolicyClassificationUnknown : null,
            Classification = classification.Classification.ToWireName(),
            PayloadBytes = Encoding.UTF8.GetByteCount(content),
        });
    }

    private void AppendWorkflowAudit(string traceId, ClassificationResult classification, string outcome, string? code, long durationMs) =>
        _auditSink.Append(new AuditEventDraft
        {
            TraceId = traceId,
            EventType = AuditEventTypes.WorkflowResult,
            Outcome = outcome,
            Code = code,
            Classification = classification.Classification.ToWireName(),
            DurationMs = durationMs,
        });
}
