using System.Text.Json;
using System.Text.Json.Nodes;
using Aether.Testing;
using Json.Schema;
using Xunit;

namespace Aether.Contracts.Tests;

public class SchemaDocumentTests
{

    [Fact]
    public void ActionPlanExampleMatchesPublishedSchema()
    {
        var schema = LoadSchema("action-plan.schema.json");
        var example = JsonSerializer.SerializeToElement(JsonNode.Parse(File.ReadAllText(TestRepo.PathTo("docs", "contracts", "action-plan.example.json")))!);

        var result = Evaluate(schema, example);
        Assert.True(result.IsValid, Describe(result));
    }

    [Fact]
    public void AppRegistryMatchesPublishedSchema()
    {
        var schema = LoadSchema("app-manifest.schema.json");
        var registry = JsonSerializer.SerializeToElement(JsonNode.Parse(File.ReadAllText(TestRepo.PathTo("samples", "registry", "apps.json")))!);

        var result = Evaluate(schema, registry);
        Assert.True(result.IsValid, Describe(result));
    }

    [Fact]
    public void EmbeddedExecutableFieldIsRejectedByPlanSchema()
    {
        var schema = LoadSchema("action-plan.schema.json");
        using var document = JsonDocument.Parse("""
            {
              "schemaVersion": "1.0",
              "planId": "plan-bad",
              "executable": "cmd.exe",
              "steps": [ { "action": "ShowStatus", "statusText": "hi" } ]
            }
            """);

        Assert.False(Evaluate(schema, document.RootElement).IsValid);
    }

    [Fact]
    public void SafePolicyExampleLoadsWithFailClosedInvariants()
    {
        var policy = Aether.Security.Policy.PolicyLoader.LoadFromFile(
            TestRepo.PathTo("docs", "contracts", "safe-policy.example.json"));

        Assert.Equal("FAIL_CLOSED", policy.FailureMode);
        Assert.False(policy.Cloud.EnabledByDefault);
        Assert.Equal("DENY", policy.DataClasses["RESTRICTED"].Cloud);
    }

    private static readonly object SchemaGate = new();
    private static readonly Dictionary<string, JsonSchema> SchemaCache = new(StringComparer.Ordinal);

    private static EvaluationResults Evaluate(JsonSchema schema, JsonElement instance) =>
        schema.Evaluate(instance, new EvaluationOptions { OutputFormat = OutputFormat.List });

    /// <summary>
    /// JsonSchema.Net registers schemas by $id in a process-global registry, so a
    /// second FromText call for the same schema throws. Cache one instance per file.
    /// </summary>
    private static JsonSchema LoadSchema(string name)
    {
        lock (SchemaGate)
        {
            if (SchemaCache.TryGetValue(name, out var cached))
            {
                return cached;
            }

            var schema = JsonSchema.FromText(File.ReadAllText(TestRepo.PathTo("docs", "contracts", name)));
            SchemaCache[name] = schema;
            return schema;
        }
    }

    private static string Describe(EvaluationResults result) =>
        string.Join("; ", (result.Details ?? []).Where(detail => !detail.IsValid).Select(detail => $"{detail.InstanceLocation}: {detail.Errors?.Count} error(s)"));
}
