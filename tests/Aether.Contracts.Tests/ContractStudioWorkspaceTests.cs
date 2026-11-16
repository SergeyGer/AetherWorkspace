using Aether.Contracts.Errors;
using Aether.Contracts.Studio;
using Aether.ContractStudio;
using Aether.Testing;
using Xunit;

namespace Aether.Contracts.Tests;

public class ContractStudioWorkspaceTests
{
    private static readonly DateTimeOffset Created = new(2026, 11, 16, 15, 30, 0, TimeSpan.Zero);

    private static ContractDraft RegistryDraft() =>
        ContractDraftGenerator.GenerateRegistryEntry("sample.portfolio", "Synthetic Portfolio", createdAtUtc: Created);

    [Fact]
    public void SaveAndLoadKeepsDraftUntrusted()
    {
        using var directory = new TempDirectory("studio");
        var workspace = new DraftWorkspace(directory.FullPath);
        var path = workspace.SaveDraft(RegistryDraft());

        var draft = workspace.LoadDraft(path);

        Assert.True(draft.Untrusted);
        Assert.Equal(ContractDraftStatus.Untrusted, draft.Status);
        Assert.Empty(workspace.Review(draft));
        Assert.Empty(Directory.GetFiles(directory.FullPath, "*.tmp"));
    }

    [Fact]
    public void LoadRejectsDraftsOutsideTheStagingWorkspace()
    {
        using var directory = new TempDirectory("studio");
        var workspace = new DraftWorkspace(directory.FullPath);
        var outside = directory.PathTo("..", "escape.draft.json");
        File.WriteAllText(outside, "{}");

        var exception = Assert.Throws<AetherContractException>(() => workspace.LoadDraft(outside));

        Assert.Equal(AetherErrorCodes.PlanInvalidJson, exception.Code);
    }

    [Fact]
    public void ApplyRequiresExplicitConfirmation()
    {
        using var directory = new TempDirectory("studio");
        var workspace = new DraftWorkspace(directory.FullPath);
        var draft = RegistryDraft();

        var exception = Assert.Throws<AetherContractException>(() =>
            workspace.Apply(draft, directory.PathTo("app-registry.json"), confirmed: false));

        Assert.Equal(AetherErrorCodes.PlanInvalidJson, exception.Code);
        Assert.False(File.Exists(directory.PathTo("app-registry.json")));
    }

    [Fact]
    public void ApplyRejectsValidationIssuesBeforeWriting()
    {
        using var directory = new TempDirectory("studio");
        var workspace = new DraftWorkspace(directory.FullPath);
        var active = Aether.Contracts.Json.AetherJson.Serialize(new Aether.Contracts.Models.AppManifestDocument
        {
            ManifestVersion = "1.0",
            Apps = [new Aether.Contracts.Models.AppManifestEntry { AppId = "sample.portfolio" }],
        });

        Assert.Throws<AetherContractException>(() =>
            workspace.Apply(RegistryDraft(), directory.PathTo("app-registry.json"), confirmed: true, active));
        Assert.False(File.Exists(directory.PathTo("app-registry.json")));
    }

    [Fact]
    public void ConfirmedProtoDraftWritesUnwrappedProtoContent()
    {
        using var directory = new TempDirectory("studio");
        var workspace = new DraftWorkspace(directory.FullPath);
        var draft = ContractDraftGenerator.GenerateProtoMessage("NewMessage", "field_name", createdAtUtc: Created);
        var target = directory.PathTo("new_message.proto");

        var written = workspace.Apply(draft, target, confirmed: true);

        Assert.Equal(Path.GetFullPath(target), written);
        Assert.Contains("syntax = \"proto3\";", File.ReadAllText(written), StringComparison.Ordinal);
        Assert.Contains("message NewMessage", File.ReadAllText(written), StringComparison.Ordinal);
        Assert.Empty(Directory.GetFiles(directory.FullPath, "*.tmp"));
    }
}
