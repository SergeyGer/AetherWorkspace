using Aether.Contracts.Errors;
using Aether.Contracts.Json;
using Aether.Contracts.Models;
using Aether.Contracts.Studio;
using Xunit;

namespace Aether.Contracts.Tests;

public class ContractStudioTests
{
    private static readonly DateTimeOffset Created = new(2026, 11, 16, 15, 0, 0, TimeSpan.Zero);

    [Fact]
    public void RegistryDraftIsUntrustedAndStrictlyValid()
    {
        var draft = ContractDraftGenerator.GenerateRegistryEntry(
            "sample.portfolio",
            "Synthetic Portfolio",
            ["context.subscribe", "context.publish"],
            ["fdc3.instrument"],
            ["PUBLIC", "INTERNAL"],
            Created);

        Assert.True(draft.Untrusted);
        Assert.Equal(ContractDraftStatus.Untrusted, draft.Status);
        Assert.Empty(ContractDraftValidator.Validate(draft));
        Assert.Contains("\"appId\":\"sample.portfolio\"", draft.PayloadJson, StringComparison.Ordinal);
    }

    [Fact]
    public void ValidationDetectsActiveRegistryCollisions()
    {
        var active = AetherJson.Serialize(new AppManifestDocument
        {
            ManifestVersion = "1.0",
            Apps = [new AppManifestEntry { AppId = "sample.portfolio" }],
        });
        var draft = ContractDraftGenerator.GenerateRegistryEntry("sample.portfolio", "Synthetic Portfolio", createdAtUtc: Created);

        var issues = ContractDraftValidator.Validate(draft, active);

        Assert.Contains(issues, issue => issue.Contains("already exists", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("Sample")]
    [InlineData("1sample")]
    [InlineData("a")]
    [InlineData("")]
    [InlineData("sample/portfolio")]
    public void InvalidIdentifiersAreRejected(string appId)
    {
        Assert.Throws<AetherContractException>(() => ContractDraftGenerator.GenerateRegistryEntry(appId, "Synthetic App", createdAtUtc: Created));
    }

    [Fact]
    public void ProtoAndSchemaDraftsValidate()
    {
        var proto = ContractDraftGenerator.GenerateProtoMessage("NewMessage", "field_name", createdAtUtc: Created);
        var schema = ContractDraftGenerator.GenerateSchema("NewContract", createdAtUtc: Created);

        Assert.Empty(ContractDraftValidator.Validate(proto));
        Assert.Empty(ContractDraftValidator.Validate(schema));
        Assert.Contains("proto3", proto.PayloadJson, StringComparison.Ordinal);
    }

    [Fact]
    public void UnknownCapabilitiesAndContextsAreRejected()
    {
        Assert.Throws<AetherContractException>(() =>
            ContractDraftGenerator.GenerateRegistryEntry("sample.app", "Synthetic App", ["broker.admin"], createdAtUtc: Created));
        Assert.Throws<AetherContractException>(() =>
            ContractDraftGenerator.GenerateRegistryEntry("sample.app", "Synthetic App", contextTypes: ["fdc3.private"], createdAtUtc: Created));
    }
}
