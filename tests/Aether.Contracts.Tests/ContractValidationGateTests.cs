using Aether.Contracts.Json;
using Aether.Contracts.Models;
using Aether.Contracts.Studio;
using Aether.Testing;
using Xunit;

namespace Aether.Contracts.Tests;

public class ContractValidationGateTests
{
    private static readonly DateTimeOffset Created = new(2026, 11, 16, 16, 0, 0, TimeSpan.Zero);

    private static string ActiveRegistry(string capabilities = "context.subscribe", string classifications = "PUBLIC,INTERNAL,RESTRICTED", int maxInstances = 1) =>
        AetherJson.Serialize(new AppManifestDocument
        {
            ManifestVersion = "1.0",
            Apps =
            [
                new AppManifestEntry
                {
                    AppId = "sample.portfolio",
                    DisplayName = "Synthetic Portfolio",
                    Executable = "{DOTNET}",
                    Arguments = ["{SAMPLES_DLL}", "--app", "sample.portfolio"],
                    Capabilities = capabilities.Split(',', StringSplitOptions.RemoveEmptyEntries),
                    AllowedChannels = ["default"],
                    AllowedContextTypes = ["fdc3.instrument"],
                    AllowedClassifications = classifications.Split(',', StringSplitOptions.RemoveEmptyEntries),
                    MaxInstances = maxInstances,
                },
            ],
        });

    [Fact]
    public void DraftOfNewAppIsNonBreaking()
    {
        var draft = ContractDraftGenerator.GenerateRegistryEntry("sample.new", "Synthetic New", createdAtUtc: Created);

        var report = ContractValidationGate.ValidateDraft(draft, ActiveRegistry());

        Assert.True(report.IsValid);
        Assert.Contains(report.Changes, change => change.Kind == "add-app" && !change.Breaking);
    }

    [Fact]
    public void RemovingCapabilityOrClassificationIsBreaking()
    {
        var draft = ContractDraftGenerator.GenerateRegistryEntry(
            "sample.portfolio", "Synthetic Portfolio",
            capabilities: ["context.subscribe"],
            classifications: ["PUBLIC"],
            createdAtUtc: Created);

        var report = ContractValidationGate.ValidateDraft(draft, ActiveRegistry());

        Assert.False(report.IsValid);
        Assert.Contains(report.Changes, change => change.Kind == "remove-classification" && change.Breaking);
    }

    [Fact]
    public void ReducedInstancesOrChangedLaunchDefinitionIsBreaking()
    {
        var draft = ContractDraftGenerator.GenerateRegistryEntry("sample.portfolio", "Synthetic Portfolio", createdAtUtc: Created);

        var report = ContractValidationGate.ValidateDraft(draft, ActiveRegistry(maxInstances: 2));

        Assert.Contains(report.Changes, change => change.Kind == "reduce-instances" && change.Breaking);
    }

    [Fact]
    public void ForbiddenTargetPathsAreRejected()
    {
        var draft = ContractDraftGenerator.GenerateRegistryEntry("sample.new", "Synthetic New", createdAtUtc: Created);
        using var directory = new TempDirectory("gate");

        var forbidden = ContractValidationGate.CheckTargetPath(draft, Path.Combine(directory.FullPath, ".git", "hooks", "registry.json"));
        var secrets = ContractValidationGate.CheckTargetPath(draft, Path.Combine(directory.FullPath, "secrets", "registry.json"));
        var env = ContractValidationGate.CheckTargetPath(draft, Path.Combine(directory.FullPath, ".env"));

        Assert.Contains(forbidden, issue => issue.Contains("forbidden", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(secrets, issue => issue.Contains("forbidden", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(env, issue => issue.Contains("environment", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void WrongTargetExtensionIsRejected()
    {
        var draft = ContractDraftGenerator.GenerateRegistryEntry("sample.new", "Synthetic New", createdAtUtc: Created);
        using var directory = new TempDirectory("gate");

        var issues = ContractValidationGate.CheckTargetPath(draft, Path.Combine(directory.FullPath, "registry.proto"));

        Assert.Contains(issues, issue => issue.Contains(".json", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void SafeTargetPathIsAccepted()
    {
        using var directory = new TempDirectory("gate");
        var draft = ContractDraftGenerator.GenerateRegistryEntry("sample.new", "Synthetic New", createdAtUtc: Created);

        var issues = ContractValidationGate.CheckTargetPath(draft, directory.PathTo("registry.json"));

        Assert.Empty(issues);
    }
}
