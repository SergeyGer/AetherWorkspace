using System.Text.Json;
using Aether.Broker;
using Aether.Broker.Client;
using Aether.Broker.Registry;
using Aether.Contracts;
using Aether.Contracts.Broker;
using Aether.Contracts.Errors;
using Aether.Contracts.Fdc3;
using Aether.Contracts.Models;
using Aether.Testing;
using Xunit;

namespace Aether.Fdc3.Tests;

public class IntentResolutionTests
{
    private static async Task<(BrokerTestContext Context, BrokerClient Client)> StartAsync(string name)
    {
        var context = BrokerTestContext.Create(testName: name);
        await context.StartAsync();
        var client = await context.RegisterOperatorAsync();
        return (context, client);
    }

    [Theory]
    [InlineData(Fdc3Intents.ViewInstrument, "sample.watchlist")]
    [InlineData(Fdc3Intents.ViewAnalysis, "sample.risk")]
    [InlineData(Fdc3Intents.ViewChart, "sample.chart")]
    public async Task SupportedIntentsResolveToDeclaredTargets(string intent, string expectedAppId)
    {
        var (context, client) = await StartAsync("intent-ok");
        await using var _ = context;
        await using var __ = client;
        var traceId = TraceIdGenerator.NewTraceId();

        var reply = await client.ResolveIntentAsync(intent, "synthetic.instrument.northstar", traceId: traceId);

        Assert.True(reply.Resolved, reply.DenialCode + ": " + reply.DenialMessage);
        Assert.Equal(expectedAppId, reply.AppId);
        Assert.Equal(Fdc3ContextTypes.Instrument, reply.ContextType);
        Assert.Equal(traceId, reply.Provenance.TraceId);
        Assert.Equal("aether.broker", reply.Provenance.SourceAppId);
    }

    [Fact]
    public async Task ApprovedPreferredTargetIsHonored()
    {
        var (context, client) = await StartAsync("intent-preferred");
        await using var _ = context;
        var reply = await client.ResolveIntentAsync(Fdc3Intents.ViewChart, "synthetic.instrument.northstar", preferredAppId: "sample.chart");
        Assert.True(reply.Resolved, reply.DenialCode);
        Assert.Equal("sample.chart", reply.AppId);
    }

    [Fact]
    public async Task UnapprovedPreferredTargetIsRejected()
    {
        var (context, client) = await StartAsync("intent-unapproved");
        await using var _ = context;
        var reply = await client.ResolveIntentAsync(Fdc3Intents.ViewInstrument, "synthetic.instrument.northstar", preferredAppId: "sample.chart");
        Assert.False(reply.Resolved);
        Assert.Equal(AetherErrorCodes.Fdc3IntentTargetNotApproved, reply.DenialCode);
    }

    [Fact]
    public async Task UnknownIntentIsRejected()
    {
        var (context, client) = await StartAsync("intent-unknown");
        await using var _ = context;
        var reply = await client.ResolveIntentAsync("TakeOverWorkspace", "synthetic.instrument.northstar");
        Assert.False(reply.Resolved);
        Assert.Equal(AetherErrorCodes.Fdc3IntentUnsupported, reply.DenialCode);
    }

    [Fact]
    public async Task ContextTypeMismatchAndUnknownContextAreRejected()
    {
        var (context, client) = await StartAsync("intent-context");
        await using var _ = context;
        var mismatch = await client.ResolveIntentAsync(Fdc3Intents.ViewInstrument, "synthetic.organization.northstar");
        Assert.False(mismatch.Resolved);
        Assert.Equal(AetherErrorCodes.Fdc3IntentContextMismatch, mismatch.DenialCode);

        var unknown = await client.ResolveIntentAsync(Fdc3Intents.ViewInstrument, "synthetic.unknown.context");
        Assert.False(unknown.Resolved);
        Assert.Equal(AetherErrorCodes.BrokerContextNotFound, unknown.DenialCode);
    }

    [Fact]
    public async Task SnapshotExposesIntentDirectorySubset()
    {
        var (context, client) = await StartAsync("intent-directory");
        await using var _ = context;
        var snapshot = await client.GetSnapshotAsync(TraceIdGenerator.NewTraceId());
        var watchlist = snapshot.Apps.Single(app => app.AppId == "sample.watchlist");
        Assert.Contains(Fdc3Intents.ViewInstrument, watchlist.Intents);
        var chart = snapshot.Apps.Single(app => app.AppId == "sample.chart");
        Assert.Contains(Fdc3Intents.ViewChart, chart.Intents);
        Assert.Contains(Fdc3ContextTypes.Organization, watchlist.AllowedContextTypes);
    }

    [Fact]
    public async Task CapabilityIsRequiredForIntentResolution()
    {
        var manifest = TestData.LoadManifestDocument();
        var apps = manifest.Apps.Select(app => app.AppId == "aether.shell"
            ? new AppManifestEntry
            {
                AppId = app.AppId,
                DisplayName = app.DisplayName,
                Launchable = app.Launchable,
                Capabilities = app.Capabilities.Where(capability => capability != "intent.resolve").ToArray(),
                AllowedChannels = app.AllowedChannels,
                AllowedContextTypes = app.AllowedContextTypes,
                AllowedClassifications = app.AllowedClassifications,
                Intents = app.Intents,
            }
            : app).ToArray();
        var options = new BrokerOptions
        {
            Registry = AppRegistry.Load(new AppManifestDocument { ManifestVersion = manifest.ManifestVersion, Apps = apps }),
            Contexts = TestData.CreateContextCatalog(),
            Transport = BrokerTransports.UnixDomainSocket,
            OperatorAppId = "aether.shell",
        };

        await using var context = BrokerTestContext.CreateWith(options);
        await context.StartAsync();
        await using var client = await context.RegisterOperatorAsync();

        var exception = await Assert.ThrowsAsync<BrokerRpcException>(() =>
            client.ResolveIntentAsync(Fdc3Intents.ViewInstrument, "synthetic.instrument.northstar"));
        Assert.Equal(AetherErrorCodes.AuthCapabilityMissing, exception.Code);
    }

    [Fact]
    public async Task FixtureScenarioMatrixMatchesRuntimeBehaviour()
    {
        var (context, client) = await StartAsync("intent-matrix");
        await using var _ = context;
        using var document = JsonDocument.Parse(File.ReadAllText(TestRepo.PathTo("tests", "fixtures", "fdc3", "intent-scenarios.json")));

        foreach (var scenario in document.RootElement.GetProperty("scenarios").EnumerateArray())
        {
            var name = scenario.GetProperty("name").GetString()!;
            var intent = scenario.GetProperty("intent").GetString()!;
            var contextRef = scenario.GetProperty("contextRef").GetString()!;
            var preferred = scenario.GetProperty("preferredAppId").GetString();
            var expectedAppId = scenario.GetProperty("expectedAppId").GetString();
            var expectedCode = scenario.GetProperty("expectedCode").GetString()!;

            var reply = await client.ResolveIntentAsync(intent, contextRef, preferred);
            if (expectedCode.Length == 0)
            {
                Assert.True(reply.Resolved, $"{name}: {reply.DenialCode} {reply.DenialMessage}");
                Assert.Equal(expectedAppId, reply.AppId);
            }
            else
            {
                Assert.False(reply.Resolved, name);
                Assert.Equal(expectedCode, reply.DenialCode);
            }
        }
    }
}
