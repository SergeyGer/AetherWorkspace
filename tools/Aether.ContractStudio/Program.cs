using Aether.Contracts.Errors;
using Aether.Contracts.Studio;
using Aether.ContractStudio;

return Run(args);

static int Run(string[] args)
{
    if (args.Length == 0 || args[0] is "--help" or "-h")
    {
        PrintHelp();
        return args.Length == 0 ? 2 : 0;
    }

    var command = args[0];
    var options = ParseOptions(args.Skip(1).ToArray());
    try
    {
        return command switch
        {
            "generate" => Generate(options),
            "review" => Review(options),
            "apply" => Apply(options),
            _ => throw new AetherContractException(AetherErrorCodes.PlanInvalidJson, $"Unknown command '{command}'."),
        };
    }
    catch (Exception exception) when (exception is AetherContractException or InvalidOperationException or IOException)
    {
        Console.Error.WriteLine("contract-studio: " + exception.Message);
        return 2;
    }
}

static int Generate(Dictionary<string, string?> options)
{
    var kind = Required(options, "kind");
    var name = Required(options, "name");
    var output = options.GetValueOrDefault("out") ?? Path.Combine(Environment.CurrentDirectory, "drafts");
    if (options.ContainsKey("registry"))
    {
        throw new AetherContractException(AetherErrorCodes.PlanInvalidJson, "generate never reads or writes the active registry; use review/apply instead.");
    }

    var draft = kind switch
    {
        "registry" => ContractDraftGenerator.GenerateRegistryEntry(name, options.GetValueOrDefault("display-name") ?? name),
        "proto" => ContractDraftGenerator.GenerateProtoMessage(name, options.GetValueOrDefault("field") ?? "field_name"),
        "schema" => ContractDraftGenerator.GenerateSchema(name),
        _ => throw new AetherContractException(AetherErrorCodes.PlanInvalidJson, "--kind must be registry, proto or schema."),
    };
    var workspace = new DraftWorkspace(output);
    var path = workspace.SaveDraft(draft);
    Console.WriteLine($"draft: {path}");
    Console.WriteLine("status: UNTRUSTED (review and explicit --confirm are required before apply; nothing was executed or applied)");
    return 0;
}

static int Review(Dictionary<string, string?> options)
{
    var workspace = new DraftWorkspace(options.GetValueOrDefault("staging") ?? Environment.CurrentDirectory);
    var draft = workspace.LoadDraft(Required(options, "draft"));
    var active = options.GetValueOrDefault("registry") is { } registryPath ? File.ReadAllText(registryPath) : null;
    var issues = workspace.Review(draft, active);
    Console.WriteLine($"draft: {draft.DraftId} ({draft.Kind}, untrusted={draft.Untrusted})");
    Console.WriteLine("preview:");
    Console.WriteLine(DraftWorkspace.UnwrapPayload(draft));
    if (issues.Count == 0)
    {
        Console.WriteLine("review: OK (no active registry changes were made)");
        return 0;
    }

    foreach (var issue in issues)
    {
        Console.Error.WriteLine("issue: " + issue);
    }

    return 2;
}

static int Apply(Dictionary<string, string?> options)
{
    var workspace = new DraftWorkspace(options.GetValueOrDefault("staging") ?? Environment.CurrentDirectory);
    var draft = workspace.LoadDraft(Required(options, "draft"));
    var active = options.GetValueOrDefault("registry") is { } registryPath ? File.ReadAllText(registryPath) : null;
    var target = workspace.Apply(draft, Required(options, "target"), options.ContainsKey("confirm"), active);
    Console.WriteLine($"applied: {draft.DraftId} -> {target}");
    return 0;
}

static Dictionary<string, string?> ParseOptions(string[] args)
{
    var options = new Dictionary<string, string?>(StringComparer.Ordinal);
    for (var index = 0; index < args.Length; index++)
    {
        var argument = args[index];
        if (!argument.StartsWith("--", StringComparison.Ordinal))
        {
            throw new AetherContractException(AetherErrorCodes.PlanInvalidJson, $"Unexpected argument '{argument}'.");
        }

        var name = argument[2..];
        if (name == "confirm")
        {
            options[name] = "true";
            continue;
        }

        if (index + 1 >= args.Length)
        {
            throw new AetherContractException(AetherErrorCodes.PlanInvalidJson, $"Missing value for option '{argument}'.");
        }

        options[name] = args[++index];
    }

    return options;
}

static string Required(Dictionary<string, string?> options, string name) =>
    options.GetValueOrDefault(name) is { Length: > 0 } value
        ? value
        : throw new AetherContractException(AetherErrorCodes.PlanInvalidJson, $"Missing required option '--{name}'.");

static void PrintHelp() => Console.WriteLine("""
    AetherWorkspace contract studio (review-only; no compilation or execution)

    Usage:
      Aether.ContractStudio generate --kind registry|proto|schema --name <name> [--display-name <text>] [--field <name>] [--out <dir>]
      Aether.ContractStudio review   --draft <path> [--staging <dir>] [--registry <active-registry.json>]
      Aether.ContractStudio apply    --draft <path> --target <path> --confirm [--staging <dir>] [--registry <active-registry.json>]

    Drafts are untrusted envelopes written under the staging directory. review never writes.
    apply requires --confirm and a clean validation result; it never compiles or executes code.
    """);
