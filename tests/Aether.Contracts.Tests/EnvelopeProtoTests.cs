using Aether.Contracts;
using Aether.Contracts.V1;
using Google.Protobuf;
using Xunit;

namespace Aether.Contracts.Tests;

public class EnvelopeProtoTests
{
    [Fact]
    public void ContextEnvelopeRoundTripsThroughProtobuf()
    {
        var envelope = new ContextEnvelope
        {
            MessageId = "msg-1",
            TraceId = TraceIdGenerator.NewTraceId(),
            SpanId = TraceIdGenerator.NewSpanId(),
            SchemaVersion = AetherVersions.ContractSchemaVersion,
            ContextType = "fdc3.instrument",
            Classification = Classification.Restricted,
            CreatedAtUnixMs = 1_700_000_000_000,
            PayloadJson = "{\"type\":\"fdc3.instrument\"}",
            Channel = "default",
            Provenance = new Provenance
            {
                SourceAppId = "sample.watchlist",
                SourceInstanceId = "inst-1",
                TraceId = TraceIdGenerator.NewTraceId(),
                SpanId = TraceIdGenerator.NewSpanId(),
                CreatedAtUnixMs = 1_700_000_000_000,
            },
        };

        var bytes = envelope.ToByteArray();
        var parsed = ContextEnvelope.Parser.ParseFrom(bytes);

        Assert.Equal(envelope.MessageId, parsed.MessageId);
        Assert.Equal(Classification.Restricted, parsed.Classification);
        Assert.Equal("sample.watchlist", parsed.Provenance.SourceAppId);
        Assert.Equal("default", parsed.Channel);
    }

    [Theory]
    [InlineData(Classification.Public)]
    [InlineData(Classification.Internal)]
    [InlineData(Classification.Restricted)]
    [InlineData(Classification.Unknown)]
    public void ClassificationProtoMappingIsBidirectional(Classification proto)
    {
        var model = proto.FromProto();
        Assert.Equal(proto, model.ToProto());
    }

    [Fact]
    public void UnspecifiedClassificationFailsClosedToUnknown()
    {
        Assert.Equal(DataClassification.Unknown, Classification.Unspecified.FromProto());
    }
}
