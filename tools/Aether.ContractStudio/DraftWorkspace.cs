using Aether.Contracts.Errors;
using Aether.Contracts.Json;
using Aether.Contracts.Studio;

namespace Aether.ContractStudio;

/// <summary>
/// Review-only staging workspace for generated contract drafts. Drafts are written as untrusted
/// envelopes; applying to a target requires explicit confirmation and passing validation first.
/// No compilation or execution happens here.
/// </summary>
public sealed class DraftWorkspace
{
    private readonly string _root;

    public DraftWorkspace(string root)
    {
        if (string.IsNullOrWhiteSpace(root))
        {
            throw new ArgumentException("A staging root is required.", nameof(root));
        }

        _root = Path.GetFullPath(root);
        Directory.CreateDirectory(_root);
    }

    public string RootPath => _root;

    public string SaveDraft(ContractDraft draft)
    {
        ArgumentNullException.ThrowIfNull(draft);
        var path = Path.Combine(_root, SafeFileName(draft.DraftId) + ".draft.json");
        AtomicWrite(path, AetherJson.SerializeIndented(draft));
        return path;
    }

    public ContractDraft LoadDraft(string path)
    {
        var fullPath = Path.GetFullPath(path);
        if (!IsInsideRoot(fullPath))
        {
            throw new AetherContractException(AetherErrorCodes.PlanInvalidJson, "Drafts must be loaded from the configured staging workspace.");
        }

        if (!File.Exists(fullPath))
        {
            throw new AetherContractException(AetherErrorCodes.PlanInvalidJson, "The draft file does not exist.");
        }

        var draft = AetherJson.DeserializeStrict<ContractDraft>(File.ReadAllText(fullPath));
        if (!draft.Untrusted || draft.Status != ContractDraftStatus.Untrusted)
        {
            throw new AetherContractException(AetherErrorCodes.PlanInvalidJson, "Only untrusted, unreviewed drafts can be staged or applied.");
        }

        return draft;
    }

    public IReadOnlyList<string> Review(ContractDraft draft, string? activeRegistryJson = null) =>
        ContractDraftValidator.Validate(draft, activeRegistryJson);

    /// <summary>Writes an approved draft atomically to its target; refuses without --confirm or on validation issues.</summary>
    public string Apply(ContractDraft draft, string targetPath, bool confirmed, string? activeRegistryJson = null)
    {
        ArgumentNullException.ThrowIfNull(draft);
        if (!confirmed)
        {
            throw new AetherContractException(AetherErrorCodes.PlanInvalidJson, "Applying a draft requires explicit --confirm after review.");
        }

        var report = ContractValidationGate.ValidateDraft(draft, activeRegistryJson);
        var issues = report.Issues
            .Concat(report.Changes.Where(change => change.Breaking).Select(change => "breaking: " + change.Detail))
            .ToList();
        issues.AddRange(ContractValidationGate.CheckTargetPath(draft, targetPath));
        if (issues.Count > 0)
        {
            throw new AetherContractException(AetherErrorCodes.PlanInvalidJson, "Draft rejected: " + string.Join("; ", issues));
        }

        if (string.IsNullOrWhiteSpace(targetPath))
        {
            throw new AetherContractException(AetherErrorCodes.PlanInvalidJson, "An apply target path is required.");
        }

        var fullTarget = Path.GetFullPath(targetPath);
        var expectedExtension = draft.Kind == ContractDraftKind.ProtoMessage ? ".proto" : ".json";
        if (!string.Equals(Path.GetExtension(fullTarget), expectedExtension, StringComparison.OrdinalIgnoreCase))
        {
            throw new AetherContractException(AetherErrorCodes.PlanInvalidJson, $"A {draft.Kind} draft can only be applied to a {expectedExtension} target.");
        }

        var directory = Path.GetDirectoryName(fullTarget);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        AtomicWrite(fullTarget, UnwrapPayload(draft));
        return fullTarget;
    }

    public static string UnwrapPayload(ContractDraft draft)
    {
        ArgumentNullException.ThrowIfNull(draft);
        if (draft.Kind != ContractDraftKind.ProtoMessage)
        {
            return draft.PayloadJson;
        }

        using var document = System.Text.Json.JsonDocument.Parse(draft.PayloadJson);
        return document.RootElement.GetProperty("proto").GetString() ?? "";
    }

    private static string SafeFileName(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.IndexOfAny(['/', '\\']) >= 0 || value.Contains("..", StringComparison.Ordinal) || value.Any(char.IsControl))
        {
            throw new AetherContractException(AetherErrorCodes.PlanInvalidJson, "The draft id cannot be used as a staging file name.");
        }

        return value;
    }

    private bool IsInsideRoot(string fullPath) =>
        fullPath.Equals(_root, StringComparison.Ordinal) ||
        fullPath.StartsWith(_root + Path.DirectorySeparatorChar, StringComparison.Ordinal);

    private static void AtomicWrite(string path, string content)
    {
        var temporary = path + "." + Guid.NewGuid().ToString("n") + ".tmp";
        try
        {
            File.WriteAllText(temporary, content);
            File.Move(temporary, path, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporary))
            {
                File.Delete(temporary);
            }
        }
    }
}
