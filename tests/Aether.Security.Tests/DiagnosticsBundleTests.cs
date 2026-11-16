using Aether.Contracts.Json;
using Aether.Contracts.Models.Diagnostics;
using Aether.Security.Diagnostics;
using Aether.Testing;
using Xunit;

namespace Aether.Security.Tests;

public class DiagnosticsBundleTests
{
    private static DiagnosticsBundle Build(string? appVersion = "0.5.0", string status = "succeeded") => DiagnosticsBundleBuilder.Build(
        new DiagnosticsRun(status, "trace-123", "plan-1", "RESTRICTED", null, 42, "deterministic-local", false),
        [
            new DiagnosticsTimelineEntry(1, "LaunchApp", "succeeded", "ACC-00000001", null),
            new DiagnosticsTimelineEntry(2, "SummarizeContext", "succeeded", null, null),
        ],
        [new DiagnosticsApp("sample.watchlist", "HEALTHY", 1), new DiagnosticsApp("sample.risk", "UNKNOWN", 0)],
        new DiagnosticsAudit(9, true, 0, true, new Dictionary<string, int> { ["broker.launch"] = 3 }),
        new DiagnosticsProviders("local-private", "DeterministicLocal", "private.deterministic", "openai", "gpt-4o-mini", false, false, false, false),
        offline: true,
        generatedAtUtc: new DateTimeOffset(2026, 11, 16, 14, 0, 0, TimeSpan.Zero),
        appVersion: appVersion);

    [Fact]
    public void BundleRedactsSyntheticAccountIdsAndNeverContainsPayloadFields()
    {
        var bundle = Build(status: "succeeded ACC-00000001");
        var json = AetherJson.SerializeIndented(bundle);

        Assert.DoesNotContain("ACC-00000001", json, StringComparison.Ordinal);
        Assert.Contains("[REDACTED-ACCOUNT]", json, StringComparison.Ordinal);
        Assert.DoesNotContain("\"command\"", json, StringComparison.Ordinal);
        Assert.DoesNotContain("\"payload\"", json, StringComparison.Ordinal);
        Assert.DoesNotContain("\"summary\"", json, StringComparison.Ordinal);
        Assert.Equal("true", bundle.Flags["syntheticDataOnly"]);
        Assert.Equal("false", bundle.Flags["rawPayloadsIncluded"]);
        Assert.Equal("false", bundle.Flags["telemetryUploads"]);
    }

    [Fact]
    public void BundleRedactsSecretAndJwtLikeValues()
    {
        var bundle = Build(appVersion: "sk-abcdefgh12345678", status: "succeeded");
        var json = AetherJson.SerializeIndented(bundle);

        Assert.DoesNotContain("sk-abcdefgh12345678", json, StringComparison.Ordinal);
        Assert.Contains("[REDACTED-SECRET]", json, StringComparison.Ordinal);
    }

    [Fact]
    public void WriteFileWritesAtomicJsonAndRoundTrips()
    {
        using var directory = new TempDirectory("diagnostics");
        var path = directory.PathTo("bundle.json");

        var written = DiagnosticsBundleBuilder.WriteFile(Build(), path);

        Assert.Equal(Path.GetFullPath(path), written);
        Assert.True(File.Exists(written));
        Assert.Empty(Directory.GetFiles(directory.FullPath, "*.tmp"));
        var parsed = WorkspaceLayoutRoundTrip(written);
        Assert.Contains("\"rawPayloadsIncluded\": \"false\"", parsed, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("")]
    [InlineData("bundle.txt")]
    [InlineData("bundle")]
    public void InvalidExportPathsAreRejected(string path)
    {
        using var directory = new TempDirectory("diagnostics");
        var candidate = string.IsNullOrEmpty(path) ? path : directory.PathTo(path);

        var exception = Assert.Throws<Aether.Contracts.Errors.AetherContractException>(() =>
            DiagnosticsBundleBuilder.WriteFile(Build(), candidate));
        Assert.Equal(Aether.Contracts.Errors.AetherErrorCodes.DiagnosticsPathInvalid, exception.Code);
    }

    [Fact]
    public void BuilderNeverThrowsOnEmptyOptionalValues()
    {
        var bundle = Build(appVersion: null);

        Assert.Equal("", bundle.AppVersion);
        Assert.Equal("trace-123", bundle.Run.TraceId);
    }

    private static string WorkspaceLayoutRoundTrip(string path) => File.ReadAllText(path);
}
