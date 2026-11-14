using Aether.AI.Summarization;
using Aether.Contracts;
using Aether.Contracts.Errors;
using Aether.Contracts.Json;
using Aether.Contracts.Models.Contexts;
using Aether.Testing;
using Xunit;

namespace Aether.AI.Tests;

public class LocalSummaryTests
{
    private static readonly LocalDeterministicSummaryProvider Provider = new();

    private static string ExposurePayload()
    {
        var catalog = AetherJson.DeserializeStrict<Aether.Contracts.Models.ContextCatalogDocument>(
            File.ReadAllText(TestRepo.PathTo("samples", "contexts", "contexts.json")));
        var exposure = catalog.Contexts.Single(context => context.ContextRef == "synthetic.exposure.northstar");
        return exposure.Payload.GetRawText();
    }

    private static SummaryRequest Request(string contextType, string payload) => new(
        TraceIdGenerator.NewTraceId(),
        "plan-test",
        "synthetic.exposure.northstar",
        contextType,
        DataClassification.Restricted,
        payload,
        "private");

    [Fact]
    public async Task ExposureSummaryIsDeterministicAndExcludesAccountIdentifiers()
    {
        var payload = ExposurePayload();
        var first = await Provider.SummarizeAsync(Request(ExposureContext.ContextType, payload), CancellationToken.None);
        var second = await Provider.SummarizeAsync(Request(ExposureContext.ContextType, payload), CancellationToken.None);

        Assert.True(first.Success);
        Assert.Equal(first.Summary, second.Summary);
        Assert.Contains("Private summary", first.Summary);
        Assert.Contains("42,500,000", first.Summary);
        Assert.Contains("Northstar Capital", first.Summary);
        Assert.DoesNotContain("ACC-", first.Summary);
        Assert.Equal(DataClassification.Restricted, first.OutputClassification);
        Assert.True(first.RedactionCount >= 3);
        Assert.Equal(LocalDeterministicSummaryProvider.ProviderName, first.Provider);
    }

    [Fact]
    public async Task InstrumentSummaryIsProducedLocally()
    {
        var payload = AetherJson.Serialize(new InstrumentContext
        {
            Name = "Northstar 2032 Bond",
            Id = new InstrumentId { Ticker = "NRST", Isin = "SYN00000NRST2" },
        });
        var result = await Provider.SummarizeAsync(Request(InstrumentContext.ContextType, payload), CancellationToken.None);

        Assert.True(result.Success);
        Assert.Contains("Northstar 2032 Bond", result.Summary);
        Assert.Contains("NRST", result.Summary);
    }

    [Fact]
    public async Task UnsupportedContextTypeFailsWithStructuredCode()
    {
        var result = await Provider.SummarizeAsync(Request("unknown.type", "{}"), CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal(AetherErrorCodes.AiOutputInvalid, result.ErrorCode);
    }

    [Fact]
    public async Task InvalidPayloadFailsClosed()
    {
        var result = await Provider.SummarizeAsync(Request(ExposureContext.ContextType, "not-json"), CancellationToken.None);
        Assert.False(result.Success);
        Assert.Equal(AetherErrorCodes.AiOutputInvalid, result.ErrorCode);
    }

    [Fact]
    public async Task MissingLocalModelFailsClearlyWithoutCloudFallback()
    {
        var audit = new RecordingAuditSink();
        var gateway = new Aether.Security.Egress.EgressGateway(
            new Aether.Security.Policy.PolicyEngine(Aether.Security.Policy.PolicyDocument.CreateDefault()), audit);
        var transport = new RecordingLocalTransport
        {
            Responder = _ => Aether.AI.Providers.CloudModelResponse.Fail(AetherErrorCodes.AiProviderMissing),
        };
        var provider = new LocalModelSummaryProvider(transport, gateway);

        var result = await provider.SummarizeAsync(Request(ExposureContext.ContextType, ExposurePayload()), CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal(AetherErrorCodes.AiProviderMissing, result.ErrorCode);
        Assert.Equal(1, transport.Calls);
    }

    [Fact]
    public async Task ConfiguredLocalModelSummaryIsUsedWhenAvailable()
    {
        var gateway = new Aether.Security.Egress.EgressGateway(
            new Aether.Security.Policy.PolicyEngine(Aether.Security.Policy.PolicyDocument.CreateDefault()));
        var transport = new RecordingLocalTransport
        {
            Responder = _ => Aether.AI.Providers.CloudModelResponse.Ok("{\"summary\":\"local model summary\"}"),
        };
        var provider = new LocalModelSummaryProvider(transport, gateway);

        var result = await provider.SummarizeAsync(Request(ExposureContext.ContextType, ExposurePayload()), CancellationToken.None);

        Assert.True(result.Success);
        Assert.Equal("local model summary", result.Summary);
        Assert.Equal(1, transport.Calls);
    }
}
