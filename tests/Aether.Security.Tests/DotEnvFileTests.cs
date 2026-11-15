using Aether.Contracts.Errors;
using Aether.Security.Secrets;
using Aether.Testing;
using Xunit;

namespace Aether.Security.Tests;

public class DotEnvFileTests
{
    [Fact]
    public void ParsesCommentsBlanksExportQuotesAndEqualsValues()
    {
        var values = DotEnvFile.Parse(new[]
        {
            "# comment",
            "",
            "OPENAI_API_KEY=sk-synthetic-not-real",
            "export GEMINI_MODEL = \"gemini-test\"",
            "AETHER_LOCAL_MODEL='local-test'",
            "AETHER_CLOUD_ENDPOINT=https://example.invalid/v1?x=1&y=2",
            "GEMINI_COMMENTED_MODEL=gemini-2.5-flash-lite  # cheapest tier",
            "AETHER_LOCAL_MODEL_ENDPOINT=http://127.0.0.1:11434/v1/chat/completions#fragment",
        });

        Assert.Equal("sk-synthetic-not-real", values["OPENAI_API_KEY"]);
        Assert.Equal("gemini-test", values["GEMINI_MODEL"]);
        Assert.Equal("local-test", values["AETHER_LOCAL_MODEL"]);
        Assert.Equal("https://example.invalid/v1?x=1&y=2", values["AETHER_CLOUD_ENDPOINT"]);
        Assert.Equal("gemini-test", values["GEMINI_MODEL"]);
        Assert.Equal("gemini-2.5-flash-lite", values["GEMINI_COMMENTED_MODEL"]);
        Assert.Equal("http://127.0.0.1:11434/v1/chat/completions#fragment", values["AETHER_LOCAL_MODEL_ENDPOINT"]);
    }

    [Fact]
    public void InvalidLinesAndKeysAreRejected()
    {
        Assert.Throws<AetherContractException>(() => DotEnvFile.Parse(new[] { "NOVALUE" }));
        Assert.Throws<AetherContractException>(() => DotEnvFile.Parse(new[] { "1INVALID=value" }));
        Assert.Throws<AetherContractException>(() => DotEnvFile.Parse(new[] { "=value" }));
    }

    [Fact]
    public void MissingFileIsRejected()
    {
        using var directory = new TempDirectory("env");
        Assert.Throws<AetherContractException>(() => DotEnvFile.Load(directory.PathTo("missing.env")));
    }

    [Fact]
    public void EnvironmentFileLoadsIntoTheSecretProvider()
    {
        using var directory = new TempDirectory("env-load");
        var path = directory.PathTo(".env");
        File.WriteAllLines(path, new[] { "OPENAI_API_KEY=synthetic-key", "OPENAI_MODEL=test-model" });
        var provider = new EnvironmentSecretProvider(DotEnvFile.Load(path));

        Assert.Equal("synthetic-key", provider.GetSecret("OPENAI_API_KEY"));
        Assert.Equal("test-model", provider.GetSecret("OPENAI_MODEL"));
        Assert.True(provider.HasSecret("OPENAI_API_KEY"));
    }

    [Fact]
    public void RepositoryProvidesACommittedEnvTemplateForModelProviders()
    {
        var template = TestRepo.PathTo(".env.example");
        Assert.True(File.Exists(template));
        var content = File.ReadAllText(template);
        foreach (var key in new[] { "OPENAI_API_KEY", "OPENAI_MODEL", "GEMINI_API_KEY", "GEMINI_MODEL", "AETHER_LOCAL_MODEL_ENDPOINT" })
        {
            Assert.Contains(key, content, StringComparison.Ordinal);
        }

        Assert.Contains("AETHER_MODEL_PROVIDER", content, StringComparison.Ordinal);
        // The template must never contain a real-looking secret value.
        Assert.DoesNotContain("sk-", content, StringComparison.OrdinalIgnoreCase);
    }
}
