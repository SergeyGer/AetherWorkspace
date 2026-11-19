using Aether.Contracts.Json;

namespace Aether.Contracts.Instructions;

/// <summary>One local, user-scoped instruction template. Never contains credentials or raw PII.</summary>
public sealed record InstructionTemplate(
    string Id,
    string Title,
    string Instruction,
    string CreatedAtUtc,
    string UpdatedAtUtc);

/// <summary>Strict on-disk template document; separate from the ActionPlan wire contract.</summary>
public sealed record InstructionTemplateDocument(
    string SchemaVersion,
    IReadOnlyList<InstructionTemplate> Items)
{
    public const string CurrentSchemaVersion = "1.0";
}

/// <summary>Result of defensively loading the local template file.</summary>
public sealed record InstructionTemplateLoadResult(
    IReadOnlyList<InstructionTemplate> Templates,
    string? Error,
    string? QuarantinedPath);

/// <summary>
/// Local instruction library for the AI Command Center (v1.5.1). Storage is user-scoped, never
/// Git-tracked, strict JSON, atomically written and defensively loaded. Corrupt/oversized files are
/// quarantined and never trigger execution.
/// </summary>
public sealed class InstructionTemplateStore
{
    public const int MaxTitleLength = 120;
    public const int MaxInstructionLength = 4000;
    public const int MaxFileBytes = 512 * 1024;
    public const int MaxItems = 100;
    public const string DefaultDirectoryName = "instructions";
    public const string DefaultFileName = "instructions.json";

    private readonly string _path;
    private readonly TimeProvider _timeProvider;
    private readonly object _gate = new();

    public InstructionTemplateStore(string? storagePath = null, TimeProvider? timeProvider = null)
    {
        _path = storagePath ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "AetherWorkspace",
            DefaultDirectoryName,
            DefaultFileName);
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public string StoragePath => _path;

    public InstructionTemplateLoadResult Load()
    {
        lock (_gate)
        {
            if (!File.Exists(_path))
            {
                return new InstructionTemplateLoadResult(StarterTemplates(), null, null);
            }

            try
            {
                var info = new FileInfo(_path);
                if (info.Length > MaxFileBytes)
                {
                    return Quarantine("The instruction library is larger than the 512 KiB safety limit.");
                }

                var document = AetherJson.DeserializeStrict<InstructionTemplateDocument>(File.ReadAllText(_path));
                if (!string.Equals(document.SchemaVersion, InstructionTemplateDocument.CurrentSchemaVersion, StringComparison.Ordinal))
                {
                    return Quarantine($"Unsupported instruction library schema version '{document.SchemaVersion}'.");
                }

                if (document.Items is null || document.Items.Count > MaxItems)
                {
                    return Quarantine("The instruction library contains too many items.");
                }

                var validated = new List<InstructionTemplate>();
                foreach (var item in document.Items)
                {
                    if (!TryValidate(item.Title, item.Instruction, out _))
                    {
                        return Quarantine("The instruction library contains an invalid template.");
                    }

                    validated.Add(item);
                }

                return new InstructionTemplateLoadResult(validated, null, null);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or Aether.Contracts.Errors.AetherContractException or System.Text.Json.JsonException)
            {
                return Quarantine("The instruction library could not be parsed safely: " + exception.GetType().Name);
            }
        }
    }

    public InstructionTemplateSaveResult Save(IReadOnlyList<InstructionTemplate> templates)
    {
        lock (_gate)
        {
            ArgumentNullException.ThrowIfNull(templates);
            if (templates.Count > MaxItems)
            {
                return InstructionTemplateSaveResult.Fail("At most 100 templates are supported.");
            }

            var seenTitles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var template in templates)
            {
                if (!TryValidate(template.Title, template.Instruction, out var error))
                {
                    return InstructionTemplateSaveResult.Fail(error!);
                }

                if (!seenTitles.Add(template.Title.Trim()))
                {
                    return InstructionTemplateSaveResult.Fail("Template titles must be unique.");
                }
            }

            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
                var document = new InstructionTemplateDocument(InstructionTemplateDocument.CurrentSchemaVersion, templates);
                var json = AetherJson.SerializeIndented(document);
                var temp = _path + ".tmp-" + Guid.NewGuid().ToString("n");
                File.WriteAllText(temp, json);
                File.Move(temp, _path, overwrite: true);
                return InstructionTemplateSaveResult.Ok(_path);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                return InstructionTemplateSaveResult.Fail("The instruction library could not be written: " + exception.Message);
            }
        }
    }

    public static bool TryValidate(string? title, string? instruction, out string? error)
    {
        if (string.IsNullOrWhiteSpace(title))
        {
            error = "A template title is required.";
            return false;
        }

        if (title.Trim().Length > MaxTitleLength)
        {
            error = $"The title must be at most {MaxTitleLength} characters.";
            return false;
        }

        if (string.IsNullOrWhiteSpace(instruction))
        {
            error = "The instruction must not be empty.";
            return false;
        }

        if (instruction.Length > MaxInstructionLength)
        {
            error = $"The instruction must be at most {MaxInstructionLength} characters.";
            return false;
        }

        error = null;
        return true;
    }

    public static InstructionTemplate NewTemplate(string title, string instruction, TimeProvider? timeProvider = null)
    {
        if (!TryValidate(title, instruction, out var error))
        {
            throw new ArgumentException(error, nameof(title));
        }

        var now = (timeProvider ?? TimeProvider.System).GetUtcNow().ToString("O", System.Globalization.CultureInfo.InvariantCulture);
        return new InstructionTemplate(Guid.NewGuid().ToString("n"), title.Trim(), instruction, now, now);
    }

    public static InstructionTemplate WithUpdatedText(InstructionTemplate template, string title, string instruction, TimeProvider? timeProvider = null)
    {
        if (!TryValidate(title, instruction, out var error))
        {
            throw new ArgumentException(error);
        }

        var now = (timeProvider ?? TimeProvider.System).GetUtcNow().ToString("O", System.Globalization.CultureInfo.InvariantCulture);
        return template with { Title = title.Trim(), Instruction = instruction, UpdatedAtUtc = now };
    }

    public static InstructionTemplate Duplicate(InstructionTemplate template, TimeProvider? timeProvider = null)
    {
        var now = (timeProvider ?? TimeProvider.System).GetUtcNow().ToString("O", System.Globalization.CultureInfo.InvariantCulture);
        return template with
        {
            Id = Guid.NewGuid().ToString("n"),
            Title = (template.Title.Length > MaxTitleLength - 5 ? template.Title[..(MaxTitleLength - 5)] : template.Title) + " copy",
            CreatedAtUtc = now,
            UpdatedAtUtc = now,
        };
    }

    public static IReadOnlyList<InstructionTemplate> StarterTemplates()
    {
        var time = DateTimeOffset.UnixEpoch.ToString("O", System.Globalization.CultureInfo.InvariantCulture);
        return
        [
            new("starter-vega", "Vega Analytics Workspace (synthetic, 3 apps)",
                "Open Vega bond analytics, show the associated risk view, synchronize this instrument across the workspace and summarize the exposure privately.",
                time, time),
            new("starter-orin", "ORIN Watchlist + Chart (synthetic, 2 apps)",
                "Open the ORIN watchlist, show the associated risk view, synchronize this instrument across the workspace and summarize the exposure privately.",
                time, time),
            new("starter-northstar", "Northstar Restricted Offline (synthetic, local-only)",
                "Open Northstar bond analytics, show the associated risk view, synchronize this instrument across the workspace and summarize the exposure privately without any cloud call.",
                time, time),
        ];
    }

    private InstructionTemplateLoadResult Quarantine(string error)
    {
        string? quarantined = null;
        try
        {
            var stamp = _timeProvider.GetUtcNow().ToString("yyyyMMddHHmmssfff", System.Globalization.CultureInfo.InvariantCulture);
            quarantined = _path + ".corrupt-" + stamp;
            File.Move(_path, quarantined, overwrite: true);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            quarantined = null;
        }

        return new InstructionTemplateLoadResult([], error, quarantined);
    }
}

public sealed record InstructionTemplateSaveResult(bool Success, string? Error, string? Path)
{
    public static InstructionTemplateSaveResult Ok(string path) => new(true, null, path);

    public static InstructionTemplateSaveResult Fail(string error) => new(false, error, null);
}
