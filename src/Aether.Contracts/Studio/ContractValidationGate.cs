using Aether.Contracts.Capabilities;
using Aether.Contracts.Errors;
using Aether.Contracts.Json;
using Aether.Contracts.Models;

namespace Aether.Contracts.Studio;

/// <summary>One classified contract change between an untrusted draft and the active registry.</summary>
public sealed record ContractChange(string Kind, string Detail, bool Breaking);

/// <summary>Validation gate report: issues reject the draft; breaking changes require redesign.</summary>
public sealed record ContractValidationReport(
    IReadOnlyList<string> Issues,
    IReadOnlyList<ContractChange> Changes)
{
    public bool IsValid => Issues.Count == 0 && Changes.All(change => !change.Breaking);

    public string? FirstIssue => Issues.Count == 0 ? null : Issues[0];
}

/// <summary>
/// AW-035 validation gate: isolated, deterministic checks for generated drafts (no compilation,
/// no execution). Detects active-registry collisions, breaking contract changes and forbidden
/// output paths, producing actionable diagnostics.
/// </summary>
public static class ContractValidationGate
{
    private static readonly string[] ForbiddenSegments = [".git", "bin", "obj", ".ssh", "secrets", "node_modules"];

    public static ContractValidationReport ValidateDraft(ContractDraft draft, string? activeRegistryJson = null)
    {
        ArgumentNullException.ThrowIfNull(draft);
        var issues = ContractDraftValidator.Validate(draft, activeRegistryJson).ToList();
        var changes = new List<ContractChange>();
        if (draft.Kind == ContractDraftKind.RegistryEntry && activeRegistryJson is not null)
        {
            try
            {
                changes.AddRange(CompareRegistryEntry(draft, activeRegistryJson));
            }
            catch (AetherContractException exception)
            {
                issues.Add(exception.Message);
            }
        }

        return new ContractValidationReport(issues, changes);
    }

    /// <summary>Compares the draft's registry entry with the active registry entry (if any).</summary>
    public static IReadOnlyList<ContractChange> CompareRegistryEntry(ContractDraft draft, string activeRegistryJson)
    {
        var active = AetherJson.DeserializeStrict<AppManifestDocument>(activeRegistryJson);
        var generated = AetherJson.DeserializeStrict<AppManifestDocument>(draft.PayloadJson);
        var changes = new List<ContractChange>();
        foreach (var app in generated.Apps)
        {
            var existing = active.Apps.FirstOrDefault(candidate => string.Equals(candidate.AppId, app.AppId, StringComparison.Ordinal));
            if (existing is null)
            {
                changes.Add(new("add-app", $"App '{app.AppId}' is new.", Breaking: false));
                continue;
            }

            foreach (var capability in existing.Capabilities.Except(app.Capabilities, StringComparer.Ordinal))
            {
                changes.Add(new("remove-capability", $"App '{app.AppId}' drops capability '{capability}'.", Breaking: true));
            }

            foreach (var capability in app.Capabilities.Except(existing.Capabilities, StringComparer.Ordinal))
            {
                changes.Add(new("add-capability", $"App '{app.AppId}' gains capability '{capability}'.", Breaking: false));
            }

            foreach (var contextType in existing.AllowedContextTypes.Except(app.AllowedContextTypes, StringComparer.Ordinal))
            {
                changes.Add(new("remove-context-type", $"App '{app.AppId}' drops context type '{contextType}'.", Breaking: true));
            }

            foreach (var classification in existing.AllowedClassifications.Except(app.AllowedClassifications, StringComparer.Ordinal))
            {
                changes.Add(new("remove-classification", $"App '{app.AppId}' drops classification '{classification}'.", Breaking: true));
            }

            if (app.MaxInstances < existing.MaxInstances)
            {
                changes.Add(new("reduce-instances", $"App '{app.AppId}' reduces maxInstances {existing.MaxInstances} -> {app.MaxInstances}.", Breaking: true));
            }

            if (!string.Equals(app.Executable, existing.Executable, StringComparison.Ordinal) ||
                !app.Arguments.SequenceEqual(existing.Arguments, StringComparer.Ordinal))
            {
                changes.Add(new("change-launch-definition", $"App '{app.AppId}' changes its executable/arguments template.", Breaking: true));
            }
        }

        return changes;
    }

    /// <summary>Checks that a reviewed draft may be written to the requested target path.</summary>
    public static IReadOnlyList<string> CheckTargetPath(ContractDraft draft, string targetPath)
    {
        var issues = new List<string>();
        if (string.IsNullOrWhiteSpace(targetPath))
        {
            issues.Add("An apply target path is required.");
            return issues;
        }

        var fullPath = Path.GetFullPath(targetPath);
        var expectedExtension = draft.Kind == ContractDraftKind.ProtoMessage ? ".proto" : ".json";
        if (!string.Equals(Path.GetExtension(fullPath), expectedExtension, StringComparison.OrdinalIgnoreCase))
        {
            issues.Add($"A {draft.Kind} draft can only be applied to a {expectedExtension} target.");
        }

        var segments = fullPath.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        foreach (var segment in segments)
        {
            if (ForbiddenSegments.Contains(segment, StringComparer.OrdinalIgnoreCase))
            {
                issues.Add($"Target path contains the forbidden segment '{segment}'.");
            }
        }

        if (string.Equals(Path.GetFileName(fullPath), ".env", StringComparison.OrdinalIgnoreCase))
        {
            issues.Add("Target path must not point at an environment/secret file.");
        }

        return issues;
    }
}
