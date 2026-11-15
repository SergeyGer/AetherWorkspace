using System.Text.Json;
using Aether.Broker.Catalog;
using Aether.Contracts;
using Aether.Contracts.Fdc3;
using Aether.Contracts.Json;
using Aether.Contracts.Models;
using Aether.Contracts.Models.Contexts;
using Aether.Contracts.V1;
using Aether.Testing;
using Google.Protobuf;
using Xunit;

namespace Aether.Fdc3.Tests;

public class ContextContractTests
{
    private static JsonElement Fixture(string name)
    {
        using var document = JsonDocument.Parse(File.ReadAllText(TestRepo.PathTo("tests", "fixtures", "fdc3", "contexts.json")));
        return document.RootElement.GetProperty(name).Clone();
    }

    [Fact]
    public void SupportedFdc3ContextFixturesParseStrictly()
    {
        var instrument = AetherJson.DeserializeStrict<InstrumentContext>(Fixture("instrument").GetRawText());
        Assert.Equal(Fdc3ContextTypes.Instrument, instrument.Type);
        Assert.Equal("NRST", instrument.Id.Ticker);

        var organization = AetherJson.DeserializeStrict<OrganizationContext>(Fixture("organization").GetRawText());
        Assert.Equal(Fdc3ContextTypes.Organization, organization.Type);
        Assert.Equal("SYN0000NORTHSTAR01", organization.Id.Lei);
    }

    [Fact]
    public void UnknownContextFieldsAreRejected()
    {
        var json = """{"type":"fdc3.organization","name":"X","id":{"lei":"SYN"},"secretField":"leak"}""";
        Assert.Throws<Aether.Contracts.Errors.AetherContractException>(() =>
            AetherJson.DeserializeStrict<OrganizationContext>(json));
    }

    [Fact]
    public void OrganizationContextRoundTripsThroughProtobufEnvelope()
    {
        var payload = Fixture("organization").GetRawText();
        var envelope = new ContextEnvelope
        {
            MessageId = "msg-org",
            TraceId = TraceIdGenerator.NewTraceId(),
            SpanId = TraceIdGenerator.NewSpanId(),
            SchemaVersion = AetherVersions.ContractSchemaVersion,
            ContextType = Fdc3ContextTypes.Organization,
            Classification = Classification.Internal,
            PayloadJson = payload,
            Channel = "default",
            Provenance = new Provenance { SourceAppId = "aether.shell", SourceInstanceId = "op-1", TraceId = TraceIdGenerator.NewTraceId() },
        };

        var parsed = ContextEnvelope.Parser.ParseFrom(envelope.ToByteArray());
        Assert.Equal(Fdc3ContextTypes.Organization, parsed.ContextType);
        Assert.Equal(payload, parsed.PayloadJson);
        Assert.Equal(Classification.Internal, parsed.Classification);
        Assert.Equal("aether.shell", parsed.Provenance.SourceAppId);
    }

    [Fact]
    public void ContextCatalogAcceptsSupportedFdc3ContextsAndRejectsUnknownType()
    {
        var catalog = TestData.CreateContextCatalog();
        Assert.True(catalog.TryGet("synthetic.instrument.northstar", out _));
        Assert.True(catalog.TryGet("synthetic.organization.northstar", out var organization));
        Assert.Equal(DataClassification.Internal, organization.Classification);

        var invalid = new ContextCatalogDocument
        {
            SchemaVersion = "1.0",
            Contexts =
            [
                new ContextCatalogEntry
                {
                    ContextRef = "synthetic.contact.unknown",
                    ContextType = "fdc3.contact",
                    Classification = "PUBLIC",
                    Payload = Fixture("organization"),
                },
            ],
        };
        Assert.Throws<Aether.Broker.BrokerConfigurationException>(() => ContextCatalog.Load(invalid, 16 * 1024));
    }

    [Fact]
    public void AllSampleAppsDeclareExpectedIntentsAndOrganizationContextType()
    {
        var registry = TestData.CreateRegistry();
        Assert.Contains(Fdc3Intents.ViewInstrument, registry.GetRequired("sample.watchlist").Intents);
        Assert.Contains(Fdc3Intents.ViewAnalysis, registry.GetRequired("sample.risk").Intents);
        Assert.Contains(Fdc3Intents.ViewChart, registry.GetRequired("sample.chart").Intents);
        Assert.Contains(Fdc3ContextTypes.Organization, registry.GetRequired("sample.watchlist").AllowedContextTypes);
    }
}
