using System.Text.Json;
using Aether.Contracts.Json;
using Aether.Testing;
using Xunit;

namespace Aether.Contracts.Tests;

/// <summary>
/// Guards the documentation-governance fixes from the v1.0.0 gap assessment (G-08):
/// release/status documents must not regress to stale claims, and the README must remain
/// the concise entry point with the documented "Start here" structure.
/// </summary>
public class DocumentationConsistencyTests
{
    [Fact]
    public void ReadmeStaysConciseWithStartHerePaths()
    {
        var readme = Read("README.md");

        Assert.Contains("Choose the path that matches the time you have", readme, StringComparison.Ordinal);
        Assert.Contains("| I want to… | Time | What you get | Start |", readme, StringComparison.Ordinal);
        Assert.Contains("Windows desktop", readme, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("## Status and evidence", readme, StringComparison.Ordinal);
        Assert.DoesNotContain("## Repository layout", readme, StringComparison.Ordinal);
        Assert.DoesNotContain("## Known limitations (summary)", readme, StringComparison.Ordinal);
    }

    [Fact]
    public void ImplementationStatusHasCurrentReleaseAndNoStaleClaims()
    {
        var status = Read("docs/IMPLEMENTATION_STATUS.md");

        Assert.Contains("## v1.0.1 — corrective release", status, StringComparison.Ordinal);
        Assert.Contains("## v1.0.0 — released", status, StringComparison.Ordinal);
        Assert.DoesNotContain("No release tag", status, StringComparison.Ordinal);
        Assert.DoesNotContain("remain planned\nfor v0.6/v1.0", status, StringComparison.Ordinal);
    }

    [Fact]
    public void LimitationsRecordWindowsDesktopEvidence()
    {
        var limitations = Read("docs/limitations.md");

        Assert.Contains("recorded for the tested scope", limitations, StringComparison.Ordinal);
        Assert.DoesNotContain("physical-desktop evidence is not yet recorded", limitations, StringComparison.Ordinal);
        Assert.Contains("ADR-009", limitations, StringComparison.Ordinal);
    }

    [Fact]
    public void CorrectiveReleaseNotesDocumentWindowsBundle()
    {
        var notes = Read("docs/releases/v1.0.1.md");

        Assert.Contains("windows-x64-desktop.zip", notes, StringComparison.Ordinal);
        Assert.Contains("Start-AetherWorkspace.ps1", notes, StringComparison.Ordinal);
        Assert.Contains("G-01", notes, StringComparison.Ordinal);
    }

    [Fact]
    public void AcceptanceManifestIsMachineReadableAndConsistent()
    {
        var path = TestRepo.PathTo("docs", "testing", "evidence", "2026-11-16-windows11-v1.0.0-acceptance.manifest.json");
        using var document = JsonDocument.Parse(File.ReadAllText(path));
        var root = document.RootElement;

        Assert.Equal("1.0", root.GetProperty("schemaVersion").GetString());
        Assert.Equal("PASS", root.GetProperty("releaseGate").GetProperty("status").GetString());
        Assert.Equal(511, root.GetProperty("totals").GetProperty("passed").GetInt32());
        Assert.Equal(0, root.GetProperty("totals").GetProperty("failed").GetInt32());
        Assert.Equal(2, root.GetProperty("environment").GetProperty("monitors").GetInt32());
        Assert.False(root.GetProperty("sanitization").GetProperty("rawArtifactsPublished").GetBoolean());
        Assert.NotEmpty(root.GetProperty("evidenceFiles").EnumerateArray());
    }

    [Fact]
    public void GapAssessmentResponseCoversAllFindings()
    {
        var response = Read("docs/audit/2026-11-16-gap-assessment-response.md");

        for (var finding = 1; finding <= 13; finding++)
        {
            Assert.Contains($"G-{finding:00}", response, StringComparison.Ordinal);
        }
    }

    private static string Read(params string[] parts) => File.ReadAllText(TestRepo.PathTo(parts));
}
