using System.Text.Json;
using System.Text.Json.Nodes;
using Aether.Contracts;
using Aether.Contracts.Fdc3;
using Aether.Contracts.Models;
using Aether.Contracts.V1;
using Aether.Testing;
using Json.Schema;
using Xunit;

namespace Aether.Fdc3.Tests;

/// <summary>
/// External interoperability increment for AW-024: our supported FDC3 payloads are
/// validated against the official FDC3 3.0 JSON Schemas vendored under
/// tests/fixtures/fdc3/official (pinned commit, Apache-2.0). This is standards-schema
/// interop, not a claim of FDC3 conformance.
/// </summary>
public class OfficialSchemaInteropTests
{
    private static readonly object SchemaGate = new();
    private static readonly Dictionary<string, JsonSchema> SchemaCache = new(StringComparer.Ordinal);

    [Theory]
    [InlineData("instrument")]
    [InlineData("organization")]
    public void SupportedFixturePayloadsValidateAgainstOfficialFdc3Schemas(string contextName)
    {
        var schema = LoadOfficialSchema(contextName);
        using var fixtures = JsonDocument.Parse(File.ReadAllText(TestRepo.PathTo("tests", "fixtures", "fdc3", "contexts.json")));
        var payload = JsonSerializer.SerializeToElement(fixtures.RootElement.GetProperty(contextName));

        var result = schema.Evaluate(payload, new EvaluationOptions { OutputFormat = OutputFormat.List });
        Assert.True(result.IsValid, Describe(result));
    }

    [Fact]
    public void TrustedCatalogPayloadsValidateAgainstOfficialFdc3Schemas()
    {
        using var catalog = JsonDocument.Parse(File.ReadAllText(TestRepo.PathTo("samples", "contexts", "contexts.json")));
        var validated = 0;
        foreach (var context in catalog.RootElement.GetProperty("contexts").EnumerateArray())
        {
            var type = context.GetProperty("contextType").GetString();
            if (type is not (Fdc3ContextTypes.Instrument or Fdc3ContextTypes.Organization))
            {
                continue;
            }

            var name = type == Fdc3ContextTypes.Instrument ? "instrument" : "organization";
            var payload = JsonSerializer.SerializeToElement(context.GetProperty("payload"));
            var result = LoadOfficialSchema(name).Evaluate(payload, new EvaluationOptions { OutputFormat = OutputFormat.List });
            Assert.True(result.IsValid, Describe(result));
            validated++;
        }

        Assert.True(validated >= 2, $"expected at least two supported catalog payloads, validated {validated}");
    }

    [Fact]
    public void ProvenanceProjectsOntoFdc3ContextMetadataShape()
    {
        var envelope = new ContextEnvelope
        {
            MessageId = "msg-1",
            TraceId = "0123456789abcdef0123456789abcdef",
            SchemaVersion = AetherVersions.ContractSchemaVersion,
            ContextType = Fdc3ContextTypes.Instrument,
            Classification = Classification.Public,
            PayloadJson = """{"type":"fdc3.instrument","name":"Synthetic","id":{"ticker":"SYN"}}""",
            CreatedAtUnixMs = 1_763_270_400_000,
            Provenance = new Provenance { SourceAppId = "aether.shell", SourceInstanceId = "op-1", TraceId = "0123456789abcdef0123456789abcdef" },
        };

        using var metadata = JsonDocument.Parse(Fdc3MetadataProjection.ToJson(envelope));
        Assert.Equal("aether.shell", metadata.RootElement.GetProperty("source").GetProperty("appId").GetString());
        Assert.Equal("op-1", metadata.RootElement.GetProperty("source").GetProperty("instanceId").GetString());
        Assert.Equal("0123456789abcdef0123456789abcdef", metadata.RootElement.GetProperty("traceId").GetString());
        var timestamp = metadata.RootElement.GetProperty("timestamp").GetString()!;
        Assert.EndsWith("Z", timestamp);
        Assert.True(DateTimeOffset.TryParse(timestamp, out _));
    }

    [Fact]
    public void OfficialSchemaProvenanceIsPinnedAndLicensed()
    {
        var commit = File.ReadAllText(TestRepo.PathTo("tests", "fixtures", "fdc3", "official", "SOURCE_COMMIT.txt")).Trim();
        Assert.Equal(40, commit.Length);
        Assert.True(File.Exists(TestRepo.PathTo("tests", "fixtures", "fdc3", "official", "LICENSE")));
        var notice = File.ReadAllText(TestRepo.PathTo("tests", "fixtures", "fdc3", "official", "NOTICE.md"));
        Assert.Contains(commit, notice, StringComparison.Ordinal);
        Assert.Contains("Apache License", notice, StringComparison.OrdinalIgnoreCase);
    }

    private static JsonSchema LoadOfficialSchema(string contextName)
    {
        lock (SchemaGate)
        {
            if (SchemaCache.TryGetValue(contextName, out var cached))
            {
                return cached;
            }

            var basePath = TestRepo.PathTo("tests", "fixtures", "fdc3", "official");
            var context = JsonNode.Parse(File.ReadAllText(Path.Combine(basePath, "context.schema.json")))!;
            var schemaNode = JsonNode.Parse(File.ReadAllText(Path.Combine(basePath, contextName + ".schema.json")))!;
            schemaNode["definitions"] = context["definitions"]!.DeepClone();
            var inlined = schemaNode.ToJsonString().Replace("context.schema.json#/definitions/", "#/definitions/", StringComparison.Ordinal);

            var schema = JsonSchema.FromText(inlined);
            SchemaCache[contextName] = schema;
            return schema;
        }
    }

    private static string Describe(EvaluationResults result) =>
        string.Join("; ", (result.Details ?? []).Where(detail => !detail.IsValid).Select(detail => $"{detail.InstanceLocation}: {detail.Errors?.Count} error(s)"));
}
