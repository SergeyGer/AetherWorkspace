using Aether.Contracts;
using Xunit;

namespace Aether.Contracts.Tests;

public class ClassificationTests
{
    [Theory]
    [InlineData(DataClassification.Public, "PUBLIC")]
    [InlineData(DataClassification.Internal, "INTERNAL")]
    [InlineData(DataClassification.Restricted, "RESTRICTED")]
    [InlineData(DataClassification.Unknown, "UNKNOWN")]
    public void WireNamesRoundTrip(DataClassification classification, string wire)
    {
        Assert.Equal(wire, classification.ToWireName());
        Assert.True(DataClassificationExtensions.TryParseWire(wire, out var parsed));
        Assert.Equal(classification, parsed);
        Assert.True(DataClassificationExtensions.TryParseWire(wire.ToLowerInvariant(), out var lowercase));
        Assert.Equal(classification, lowercase);
    }

    [Fact]
    public void UnknownOutranksEveryExplicitLabel()
    {
        foreach (var classification in new[] { DataClassification.Public, DataClassification.Internal, DataClassification.Restricted })
        {
            Assert.Equal(DataClassification.Unknown, classification.Max(DataClassification.Unknown));
            Assert.Equal(DataClassification.Unknown, DataClassification.Unknown.Max(classification));
        }
    }

    [Fact]
    public void MaxEscalatesButNeverDowngrades()
    {
        Assert.Equal(DataClassification.Internal, DataClassification.Public.Max(DataClassification.Internal));
        Assert.Equal(DataClassification.Restricted, DataClassification.Restricted.Max(DataClassification.Public));
        Assert.Equal(DataClassification.Restricted, DataClassification.Internal.Max(DataClassification.Restricted));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("SECRET")]
    [InlineData("public' OR 1=1")]
    public void MalformedWireLabelsFailClosedToUnknown(string? wire)
    {
        Assert.False(DataClassificationExtensions.TryParseWire(wire, out var parsed));
        Assert.Equal(DataClassification.Unknown, parsed);
        Assert.Equal(DataClassification.Unknown, DataClassificationExtensions.ParseOrUnknown(wire));
    }

    [Fact]
    public void UndefinedEnumValueIsTreatedAsUnknown()
    {
        var undefined = (DataClassification)42;
        Assert.Equal(DataClassification.Unknown, undefined.Max(DataClassification.Public));
        Assert.Equal(3, undefined.SensitivityRank());
        Assert.Equal("UNKNOWN", undefined.ToWireName());
    }
}
