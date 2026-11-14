using Aether.Broker;
using Aether.Broker.Launching;
using Aether.Broker.Sessions;
using Aether.Broker.Windows;
using Aether.Contracts;
using Aether.Contracts.Broker;
using Aether.Contracts.Models;
using Aether.Contracts.Errors;
using Aether.Contracts.V1;
using Aether.Security.Audit;
using Aether.Testing;
using Xunit;

namespace Aether.Broker.Tests;

public class HealthAuditSnapshotTests
{
    [Fact]
    public async Task SnapshotExposesAllowlistAndContextCatalogWithoutPayloads()
    {
        await using var context = BrokerTestContext.Create(testName: "snapshot");
        await context.StartAsync();
        await using var client = await context.RegisterOperatorAsync();

        var snapshot = await client.GetSnapshotAsync(TraceIdGenerator.NewTraceId());

        Assert.Equal(AetherVersions.BrokerVersion, snapshot.BrokerVersion);
        Assert.Equal(6, snapshot.MaxActionsPerPlan);
        Assert.Contains(Layouts.SideBySide, snapshot.SupportedLayouts);
        Assert.Equal(4, snapshot.Apps.Count);
        Assert.Equal(4, snapshot.Contexts.Count);
        Assert.All(snapshot.Apps, app => Assert.Contains(app.Capabilities, capability => capability.Length > 0));
        var instrument = snapshot.Contexts.Single(entry => entry.ContextRef == "synthetic.instrument.northstar");
        Assert.Equal("fdcp.instrument".Replace("dcp", "dc3"), instrument.ContextType);
        Assert.Equal(Classification.Restricted, instrument.Classification);
    }

    [Fact]
    public async Task HealthProbeIsAnonymousMinimalAndAuthenticatedDetailed()
    {
        await using var context = BrokerTestContext.Create(testName: "health");
        await context.StartAsync();

        await using var anonymous = context.CreateClient();
        var anonymousReply = await anonymous.HealthProbeAsync();
        Assert.Equal(AetherVersions.BrokerVersion, anonymousReply.BrokerVersion);
        Assert.Empty(anonymousReply.AppHealth);

        await using var operatorClient = await context.RegisterOperatorAsync();
        var authenticatedReply = await operatorClient.HealthProbeAsync();
        Assert.NotEmpty(authenticatedReply.Transport);
        Assert.True(authenticatedReply.ActiveSessions >= 1);
    }

    [Fact]
    public async Task MissedHeartbeatsBecomeUnresponsiveState()
    {
        var identity = new FakeProcessIdentityResolver();
        var time = new ManualTimeProvider(new DateTimeOffset(2026, 11, 14, 12, 0, 0, TimeSpan.Zero));
        identity.Register(Environment.ProcessId, "/tmp/aether-health-app", time.GetUtcNow());
        await using var context = BrokerTestContext.Create(identityResolver: identity, timeProvider: time, testName: "health-unresponsive");
        await context.StartAsync();

        var app = TestData.CreateRegistry().GetRequired("sample.watchlist");
        const string token = "health-token";
        context.Host.Sessions.AddPendingRegistration(
            app, "inst-health", token,
            new ProcessIdentity(Environment.ProcessId, "/tmp/aether-health-app", time.GetUtcNow()),
            time.GetUtcNow());
        await using var client = context.CreateClient();
        await client.RegisterAsync("sample.watchlist", token, "inst-health");

        var initial = context.Host.Health.Evaluate().Single(report => report.InstanceId == "inst-health");
        Assert.Equal(Broker.Health.AppHealthState.Healthy, initial.State);

        time.Advance(TimeSpan.FromSeconds(60));

        var later = context.Host.Health.Evaluate().Single(report => report.InstanceId == "inst-health");
        Assert.Equal(Broker.Health.AppHealthState.Unresponsive, later.State);
        Assert.True(later.MissedHeartbeats >= 3);
    }

    [Fact]
    public async Task BrokerAuditChainDetectsNoTamperingAfterOperations()
    {
        await using var context = BrokerTestContext.Create(testName: "audit");
        await context.StartAsync();
        await using var operatorClient = await context.RegisterOperatorAsync();

        var launched = await operatorClient.LaunchAppAsync("sample.watchlist", TraceIdGenerator.NewTraceId());
        Assert.True(launched.Accepted);
        await operatorClient.PublishContextAsync(
            "synthetic.instrument.northstar", Aether.Contracts.DataClassification.Restricted, TraceIdGenerator.NewTraceId());
        await operatorClient.ArrangeWindowsAsync("side-by-side", TraceIdGenerator.NewTraceId());

        var log = Assert.IsType<HashChainAuditLog>(context.Host.AuditSink);
        var verification = log.Verify();
        Assert.True(verification.Valid);
        Assert.Contains(log.Snapshot(), entry => entry.EventType == AuditEventTypes.BrokerLaunch);
        Assert.Contains(log.Snapshot(), entry => entry.EventType == AuditEventTypes.ContextPublish && entry.Outcome == AuditOutcomes.Succeeded);
        Assert.Contains(log.Snapshot(), entry => entry.EventType == AuditEventTypes.WindowArrange);
    }

    [Fact]
    public async Task PublishContextDenialsAreStructured()
    {
        await using var context = BrokerTestContext.Create(testName: "publish-denial");
        await context.StartAsync();
        await using var operatorClient = await context.RegisterOperatorAsync();

        var unknownContext = await operatorClient.PublishContextAsync(
            "synthetic.unknown.context", Aether.Contracts.DataClassification.Public, TraceIdGenerator.NewTraceId());
        Assert.False(unknownContext.Accepted);
        Assert.Equal(AetherErrorCodes.BrokerContextNotFound, unknownContext.DenialCode);

        var downgrade = await operatorClient.PublishContextAsync(
            "synthetic.instrument.northstar", Aether.Contracts.DataClassification.Public, TraceIdGenerator.NewTraceId());
        Assert.False(downgrade.Accepted);
        Assert.Equal(AetherErrorCodes.AuthClassificationDenied, downgrade.DenialCode);

        var unknownClassification = await operatorClient.PublishContextAsync(
            "synthetic.instrument.vega", Aether.Contracts.DataClassification.Unknown, TraceIdGenerator.NewTraceId());
        Assert.False(unknownClassification.Accepted);
        Assert.Equal(AetherErrorCodes.PolicyClassificationUnknown, unknownClassification.DenialCode);
    }

    [Fact]
    public async Task ResolveContextRequiresClearanceAndReturnsProvenance()
    {
        await using var context = BrokerTestContext.Create(testName: "resolve");
        await context.StartAsync();
        await using var operatorClient = await context.RegisterOperatorAsync();

        var resolved = await operatorClient.ResolveContextAsync("synthetic.exposure.northstar", TraceIdGenerator.NewTraceId());
        Assert.True(resolved.Allowed);
        Assert.Equal(Classification.Restricted, resolved.Classification);
        Assert.NotNull(resolved.Provenance);
        Assert.Contains("Northstar", resolved.PayloadJson);
    }

    [Fact]
    public async Task EnsureTheBrokerAuditFileIsTamperEvident()
    {
        await using var context = BrokerTestContext.Create(testName: "audit-file");
        await context.StartAsync();
        await using var client = await context.RegisterOperatorAsync();
        await client.HealthProbeAsync();

        var auditPath = Path.Combine(context.Directory, "audit.jsonl");
        Assert.True(File.Exists(auditPath));
        Assert.True(AuditChainVerifier.VerifyFile(auditPath).Valid);

        var lines = File.ReadAllLines(auditPath);
        lines[0] = lines[0].Replace("\"outcome\":\"allowed\"", "\"outcome\":\"denied\"");
        File.WriteAllLines(auditPath, lines);

        var verification = AuditChainVerifier.VerifyFile(auditPath);
        Assert.False(verification.Valid);
    }
}
