using Aether.Contracts;

namespace Aether.Shell.Headless;

/// <summary>Command-line options for the headless shell (CI/demo host).</summary>
public sealed class ShellOptions
{
    public string? Command { get; private set; }

    public string? PlanFile { get; private set; }

    public DataClassification Classification { get; private set; } = DataClassification.Unknown;

    public string? EndpointFile { get; private set; }

    public string Planner { get; private set; } = "deterministic";

    public bool CloudEnabled { get; private set; }

    public bool CloudConsent { get; private set; }

    public string? PolicyPath { get; private set; }

    public string? AuditFile { get; private set; }

    public string? OutputFile { get; private set; }

    public int MaxActions { get; private set; } = 6;

    public int TimeoutSeconds { get; private set; } = 120;

    public bool TextOutput { get; private set; }

    public bool UseLocalModel { get; private set; }

    /// <summary>Offline mode: cloud adapters are not constructed and cloud flags are forced off.</summary>
    public bool Offline { get; private set; }

    /// <summary>Optional `.env`-style file loaded into an in-memory secret provider.</summary>
    public string? EnvFile { get; private set; }

    /// <summary>Operator-initiated sanitized diagnostics export path (v0.5, AW-033).</summary>
    public string? ExportDiagnosticsPath { get; private set; }

    public long AuditMaxFileBytes { get; private set; }

    public int AuditMaxFiles { get; private set; } = 4;

    public static ShellOptions Parse(string[] args)
    {
        var options = new ShellOptions();
        for (var index = 0; index < args.Length; index++)
        {
            var argument = args[index];
            string NextValue() => index + 1 < args.Length
                ? args[++index]
                : throw new ArgumentException($"Missing value for option '{argument}'.");

            switch (argument)
            {
                case "--command":
                    options.Command = NextValue();
                    break;
                case "--plan-file":
                    options.PlanFile = NextValue();
                    break;
                case "--classification":
                    var wire = NextValue();
                    if (!DataClassificationExtensions.TryParseWire(wire, out var classification))
                    {
                        throw new ArgumentException("--classification must be PUBLIC, INTERNAL, RESTRICTED or UNKNOWN.");
                    }

                    options.Classification = classification;
                    break;
                case "--endpoint-file":
                    options.EndpointFile = NextValue();
                    break;
                case "--planner":
                    options.Planner = NextValue();
                    if (options.Planner is not ("deterministic" or "cloud" or "local"))
                    {
                        throw new ArgumentException("--planner must be 'deterministic', 'cloud' or 'local'.");
                    }

                    break;
                case "--cloud-enabled":
                    options.CloudEnabled = true;
                    break;
                case "--cloud-consent":
                    options.CloudConsent = true;
                    break;
                case "--policy":
                    options.PolicyPath = NextValue();
                    break;
                case "--audit-file":
                    options.AuditFile = NextValue();
                    break;
                case "--audit-max-bytes":
                    options.AuditMaxFileBytes = long.Parse(NextValue(), System.Globalization.CultureInfo.InvariantCulture);
                    break;
                case "--audit-files":
                    options.AuditMaxFiles = int.Parse(NextValue(), System.Globalization.CultureInfo.InvariantCulture);
                    break;
                case "--output-file":
                    options.OutputFile = NextValue();
                    break;
                case "--max-actions":
                    options.MaxActions = int.Parse(NextValue(), System.Globalization.CultureInfo.InvariantCulture);
                    break;
                case "--timeout-seconds":
                    options.TimeoutSeconds = int.Parse(NextValue(), System.Globalization.CultureInfo.InvariantCulture);
                    break;
                case "--text":
                    options.TextOutput = true;
                    break;
                case "--use-local-model":
                    options.UseLocalModel = true;
                    break;
                case "--offline":
                    options.Offline = true;
                    break;
                case "--env-file":
                    options.EnvFile = NextValue();
                    break;
                case "--export-diagnostics":
                    options.ExportDiagnosticsPath = NextValue();
                    break;
                default:
                    throw new ArgumentException($"Unknown option '{argument}'.");
            }
        }

        if ((options.Command is null) == (options.PlanFile is null))
        {
            throw new ArgumentException("Provide exactly one of --command or --plan-file.");
        }

        if (options.EndpointFile is null)
        {
            throw new ArgumentException("--endpoint-file is required.");
        }

        return options;
    }

    public static string HelpText => """
        AetherWorkspace headless shell (reference workflow host).

        Usage: Aether.Shell.Headless --endpoint-file <path> --classification <class> (--command <text> | --plan-file <path>)

        Options:
          --classification <class>  PUBLIC | INTERNAL | RESTRICTED | UNKNOWN (trusted source label).
          --planner <kind>          deterministic (default) | cloud (opt-in cloud adapter) | local (loopback model).
          --cloud-enabled           Enable cloud mode (still requires consent and an allowed classification).
          --cloud-consent           Grant explicit consent for cloud processing.
          --policy <path>           Load a trusted policy JSON document (default built-in policy).
          --audit-file <path>       Append the workflow audit chain as JSONL.
          --output-file <path>      Write the structured workflow result JSON.
          --max-actions <n>         Plan action budget (reference configuration uses 6).
          --timeout-seconds <n>     Workflow cancellation timeout.
          --text                    Human-readable output instead of JSON.
          --use-local-model         Use the optional loopback local model adapter for summaries.
          --offline                 Offline mode: no cloud adapter is constructed and cloud flags are forced off.
          --env-file <path>         Load provider configuration/secrets from a local .env file (git-ignored).
          --export-diagnostics <path.json>  Write a sanitized local diagnostics bundle (no telemetry upload).
          --audit-max-bytes <n>     Rotate the audit file at n bytes (min 4096; 0 disables rotation).
          --audit-files <n>         Retain at most n rotated audit files (default 4).
        """;
}
