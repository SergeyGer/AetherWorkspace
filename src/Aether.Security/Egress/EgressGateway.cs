using System.Collections.Concurrent;
using System.Diagnostics;
using Aether.Contracts;
using Aether.Contracts.Errors;
using Aether.Security.Audit;
using Aether.Security.Policy;

namespace Aether.Security.Egress;

public interface IEgressGateway
{
    Task<EgressOutcome<T>> SendAsync<T>(
        EgressRequest request,
        Func<CancellationToken, Task<T>> send,
        CancellationToken cancellationToken = default);

    EgressStatistics Snapshot();
}

/// <summary>
/// Single deny-by-default egress choke point. A denied request never invokes the
/// outbound transport, so tests can observe zero external calls for RESTRICTED,
/// UNKNOWN and failed-classification fixtures.
/// </summary>
public sealed class EgressGateway : IEgressGateway
{
    private readonly IPolicyEngine _policyEngine;
    private readonly IAuditSink? _auditSink;
    private readonly ConcurrentDictionary<string, long> _denialsByCode = new(StringComparer.Ordinal);
    private long _attempts;
    private long _externalAttempts;
    private long _denied;
    private long _succeeded;
    private long _failed;

    public EgressGateway(IPolicyEngine policyEngine, IAuditSink? auditSink = null)
    {
        _policyEngine = policyEngine ?? throw new ArgumentNullException(nameof(policyEngine));
        _auditSink = auditSink;
    }

    public async Task<EgressOutcome<T>> SendAsync<T>(
        EgressRequest request,
        Func<CancellationToken, Task<T>> send,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(send);

        var decision = _policyEngine.Evaluate(new PolicyRequest(
            request.Classification,
            request.ProviderKind,
            request.TaskKind,
            request.CloudEnabled,
            request.CloudConsentGranted,
            request.SanitizationReviewed));

        if (!decision.Allowed)
        {
            Interlocked.Increment(ref _denied);
            _denialsByCode.AddOrUpdate(decision.Code, 1, static (_, count) => count + 1);
            _auditSink?.Append(new AuditEventDraft
            {
                TraceId = request.TraceId,
                EventType = AuditEventTypes.Egress,
                Outcome = AuditOutcomes.Denied,
                Code = decision.Code,
                PolicyVersion = decision.PolicyVersion,
                Classification = request.Classification.ToWireName(),
                ProviderCategory = ProviderCategory(request.ProviderKind),
                PayloadBytes = request.PayloadBytes,
            });

            return new EgressOutcome<T>(Sent: false, Value: default, decision);
        }

        Interlocked.Increment(ref _attempts);
        if (request.ProviderKind == AiProviderKind.Cloud)
        {
            Interlocked.Increment(ref _externalAttempts);
        }

        var stopwatch = Stopwatch.StartNew();
        try
        {
            var value = await send(cancellationToken).ConfigureAwait(false);
            Interlocked.Increment(ref _succeeded);
            stopwatch.Stop();
            _auditSink?.Append(new AuditEventDraft
            {
                TraceId = request.TraceId,
                EventType = AuditEventTypes.Egress,
                Outcome = AuditOutcomes.Succeeded,
                PolicyVersion = decision.PolicyVersion,
                Classification = request.Classification.ToWireName(),
                ProviderCategory = ProviderCategory(request.ProviderKind),
                PayloadBytes = request.PayloadBytes,
                DurationMs = stopwatch.ElapsedMilliseconds,
            });

            return new EgressOutcome<T>(Sent: true, Value: value, decision);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            stopwatch.Stop();
            _auditSink?.Append(new AuditEventDraft
            {
                TraceId = request.TraceId,
                EventType = AuditEventTypes.Egress,
                Outcome = AuditOutcomes.Cancelled,
                Code = AetherErrorCodes.WorkflowCancelled,
                PolicyVersion = decision.PolicyVersion,
                Classification = request.Classification.ToWireName(),
                ProviderCategory = ProviderCategory(request.ProviderKind),
                PayloadBytes = request.PayloadBytes,
                DurationMs = stopwatch.ElapsedMilliseconds,
            });
            throw;
        }
        catch (Exception)
        {
            Interlocked.Increment(ref _failed);
            stopwatch.Stop();
            _auditSink?.Append(new AuditEventDraft
            {
                TraceId = request.TraceId,
                EventType = AuditEventTypes.Egress,
                Outcome = AuditOutcomes.Failed,
                Code = AetherErrorCodes.EgressProviderError,
                PolicyVersion = decision.PolicyVersion,
                Classification = request.Classification.ToWireName(),
                ProviderCategory = ProviderCategory(request.ProviderKind),
                PayloadBytes = request.PayloadBytes,
                DurationMs = stopwatch.ElapsedMilliseconds,
            });

            return new EgressOutcome<T>(
                Sent: false,
                Value: default,
                decision,
                ErrorCode: AetherErrorCodes.EgressProviderError);
        }
    }

    public EgressStatistics Snapshot() => new(
        Interlocked.Read(ref _attempts),
        Interlocked.Read(ref _externalAttempts),
        Interlocked.Read(ref _denied),
        Interlocked.Read(ref _succeeded),
        Interlocked.Read(ref _failed),
        new Dictionary<string, long>(_denialsByCode, StringComparer.Ordinal));

    private static string ProviderCategory(AiProviderKind providerKind) =>
        providerKind == AiProviderKind.Cloud ? "cloud" : "local";
}
