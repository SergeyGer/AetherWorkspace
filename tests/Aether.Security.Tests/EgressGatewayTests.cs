using Aether.Contracts;
using Aether.Contracts.Errors;
using Aether.Security.Audit;
using Aether.Security.Egress;
using Aether.Security.Policy;
using Aether.Testing;
using Xunit;

namespace Aether.Security.Tests;

public class EgressGatewayTests
{
    private static (EgressGateway Gateway, RecordingAuditSink Audit) CreateGateway(bool allowSanitizedInternal = false)
    {
        var policy = new PolicyDocument
        {
            Cloud = new PolicyDocument.CloudPolicy { AllowSanitizedInternal = allowSanitizedInternal },
            DataClasses = PolicyDocument.CreateDefault().DataClasses,
        };
        var audit = new RecordingAuditSink();
        return (new EgressGateway(new PolicyEngine(policy), audit), audit);
    }

    private static EgressRequest CloudRequest(DataClassification classification, bool enabled = true, bool consent = true) =>
        new("trace-egress", "recording-cloud", AiProviderKind.Cloud, AiTaskKind.Summarization,
            classification, enabled, consent, SanitizationReviewed: false, "payload", 1024);

    [Fact]
    public async Task RestrictedCloudCallIsDeniedBeforeTransportInvocation()
    {
        var (gateway, audit) = CreateGateway();
        var calls = 0;

        var outcome = await gateway.SendAsync(CloudRequest(DataClassification.Restricted), _ =>
        {
            calls++;
            return Task.FromResult("should-not-happen");
        });

        Assert.False(outcome.Sent);
        Assert.Equal(0, calls);
        Assert.Equal(AetherErrorCodes.PolicyCloudDenied, outcome.Decision.Code);

        var stats = gateway.Snapshot();
        Assert.Equal(0, stats.Attempts);
        Assert.Equal(0, stats.ExternalAttempts);
        Assert.Equal(1, stats.Denied);
        Assert.Equal(1, stats.DenialsByCode[AetherErrorCodes.PolicyCloudDenied]);
        Assert.Contains(audit.Drafts, draft => draft.EventType == AuditEventTypes.Egress && draft.Outcome == AuditOutcomes.Denied);
    }

    [Fact]
    public async Task UnknownCloudCallNeverReachesTransport()
    {
        var (gateway, _) = CreateGateway();
        var calls = 0;

        var outcome = await gateway.SendAsync(CloudRequest(DataClassification.Unknown), _ =>
        {
            calls++;
            return Task.FromResult("no");
        });

        Assert.False(outcome.Sent);
        Assert.Equal(0, calls);
        Assert.Equal(0, gateway.Snapshot().ExternalAttempts);
    }

    [Fact]
    public async Task LocalCallIsAllowedAndNotCountedAsExternal()
    {
        var (gateway, _) = CreateGateway();
        var request = new EgressRequest("trace-local", "recording-local", AiProviderKind.Local, AiTaskKind.Summarization,
            DataClassification.Restricted, false, false, false, "payload", 10);

        var outcome = await gateway.SendAsync(request, _ => Task.FromResult("local-result"));

        Assert.True(outcome.Sent);
        Assert.Equal("local-result", outcome.Value);
        var stats = gateway.Snapshot();
        Assert.Equal(1, stats.Attempts);
        Assert.Equal(0, stats.ExternalAttempts);
        Assert.Equal(1, stats.Succeeded);
    }

    [Fact]
    public async Task AllowedCloudCallInvokesTransportOnceAndCountsRetriesSeparately()
    {
        var (gateway, _) = CreateGateway();
        var calls = 0;

        for (var attempt = 0; attempt < 2; attempt++)
        {
            await gateway.SendAsync(CloudRequest(DataClassification.Public), _ =>
            {
                calls++;
                return Task.FromResult("ok");
            });
        }

        Assert.Equal(2, calls);
        Assert.Equal(2, gateway.Snapshot().ExternalAttempts);
    }

    [Fact]
    public async Task ProviderFailureIsReturnedAsStructuredOutcome()
    {
        var (gateway, _) = CreateGateway();

        var outcome = await gateway.SendAsync<string>(CloudRequest(DataClassification.Public), _ =>
            throw new HttpRequestException("simulated provider outage"));

        Assert.False(outcome.Sent);
        Assert.Equal(AetherErrorCodes.EgressProviderError, outcome.ErrorCode);
        Assert.Equal(1, gateway.Snapshot().Failed);
    }

    [Fact]
    public async Task CancellationPropagatesAndIsAudited()
    {
        var (gateway, audit) = CreateGateway();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            gateway.SendAsync<string>(CloudRequest(DataClassification.Public), token =>
            {
                token.ThrowIfCancellationRequested();
                return Task.FromResult("unreachable");
            }, cancellation.Token));

        Assert.Contains(audit.Drafts, draft => draft.EventType == AuditEventTypes.Egress && draft.Outcome == AuditOutcomes.Cancelled);
    }
}
