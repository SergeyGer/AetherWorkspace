using Aether.Contracts.Broker;

namespace Aether.BrokerApp;

/// <summary>Command-line options for the broker process.</summary>
public sealed class BrokerAppOptions
{
    public string? Transport { get; private set; }

    public string? Endpoint { get; private set; }

    public string? EndpointFile { get; private set; }

    public required string RegistryPath { get; init; }

    public required string ContextsPath { get; init; }

    public Dictionary<string, string> Tokens { get; } = new(StringComparer.Ordinal);

    public List<string> AllowedExecutableRoots { get; } = [];

    public string? OperatorApp { get; private set; } = "aether.shell";

    public string? OperatorToken { get; private set; }

    public int MaxActions { get; private set; } = 6;

    public int MaxPayloadBytes { get; private set; } = 16 * 1024;

    public int WorkAreaWidth { get; private set; } = 1920;

    public int WorkAreaHeight { get; private set; } = 1080;

    public string? AuditFile { get; private set; }

    public long AuditMaxFileBytes { get; private set; }

    public int AuditMaxFiles { get; private set; } = 4;

    public string? ChildLogDirectory { get; private set; }

    public int? RunSeconds { get; private set; }

    public bool RequireAppReadiness { get; private set; } = true;

    public bool EnableWin32WindowArrangement { get; private set; } = OperatingSystem.IsWindows();

    public static BrokerAppOptions Parse(string[] args)
    {
        string? registry = null;
        string? contexts = null;
        var options = new BrokerAppOptions
        {
            RegistryPath = "",
            ContextsPath = "",
        };

        for (var index = 0; index < args.Length; index++)
        {
            var argument = args[index];
            string NextValue() => index + 1 < args.Length
                ? args[++index]
                : throw new ArgumentException($"Missing value for option '{argument}'.");

            switch (argument)
            {
                case "--transport":
                    options.Transport = NextValue();
                    if (!BrokerTransports.TryParse(options.Transport, out _))
                    {
                        throw new ArgumentException("Transport must be 'named-pipe' or 'unix-domain-socket'.");
                    }

                    break;
                case "--endpoint":
                    options.Endpoint = NextValue();
                    break;
                case "--registry":
                    registry = NextValue();
                    break;
                case "--contexts":
                    contexts = NextValue();
                    break;
                case "--token":
                    var token = NextValue();
                    var separator = token.IndexOf('=');
                    if (separator <= 0)
                    {
                        throw new ArgumentException("Token values must use NAME=VALUE syntax.");
                    }

                    options.Tokens[token[..separator]] = token[(separator + 1)..];
                    break;
                case "--allowed-root":
                    options.AllowedExecutableRoots.Add(NextValue());
                    break;
                case "--endpoint-file":
                    options.EndpointFile = NextValue();
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
                case "--child-log-dir":
                    options.ChildLogDirectory = NextValue();
                    break;
                case "--operator-app":
                    options.OperatorApp = NextValue();
                    break;
                case "--operator-token":
                    options.OperatorToken = NextValue();
                    break;
                case "--max-actions":
                    options.MaxActions = int.Parse(NextValue(), System.Globalization.CultureInfo.InvariantCulture);
                    break;
                case "--max-payload-bytes":
                    options.MaxPayloadBytes = int.Parse(NextValue(), System.Globalization.CultureInfo.InvariantCulture);
                    break;
                case "--work-area":
                    var area = NextValue().Split('x', StringSplitOptions.RemoveEmptyEntries);
                    if (area.Length != 2)
                    {
                        throw new ArgumentException("Work area must use WIDTHxHEIGHT syntax.");
                    }

                    options.WorkAreaWidth = int.Parse(area[0], System.Globalization.CultureInfo.InvariantCulture);
                    options.WorkAreaHeight = int.Parse(area[1], System.Globalization.CultureInfo.InvariantCulture);
                    break;
                case "--run-seconds":
                    options.RunSeconds = int.Parse(NextValue(), System.Globalization.CultureInfo.InvariantCulture);
                    break;
                case "--no-readiness-check":
                    options.RequireAppReadiness = false;
                    break;
                case "--no-win32-arrangement":
                    options.EnableWin32WindowArrangement = false;
                    break;
                case "--help":
                case "-h":
                    throw new ArgumentException(HelpText);
                default:
                    throw new ArgumentException($"Unknown option '{argument}'.");
            }
        }

        if (string.IsNullOrWhiteSpace(registry) || string.IsNullOrWhiteSpace(contexts))
        {
            throw new ArgumentException("--registry and --contexts are required." + Environment.NewLine + HelpText);
        }

        var parsed = new BrokerAppOptions
        {
            RegistryPath = registry,
            ContextsPath = contexts,
            Transport = options.Transport,
            Endpoint = options.Endpoint,
            EndpointFile = options.EndpointFile,
            OperatorApp = options.OperatorApp,
            OperatorToken = options.OperatorToken,
            MaxActions = options.MaxActions,
            MaxPayloadBytes = options.MaxPayloadBytes,
            WorkAreaWidth = options.WorkAreaWidth,
            WorkAreaHeight = options.WorkAreaHeight,
            AuditFile = options.AuditFile,
            AuditMaxFileBytes = options.AuditMaxFileBytes,
            AuditMaxFiles = options.AuditMaxFiles,
            ChildLogDirectory = options.ChildLogDirectory,
            RunSeconds = options.RunSeconds,
            RequireAppReadiness = options.RequireAppReadiness,
            EnableWin32WindowArrangement = options.EnableWin32WindowArrangement,
        };
        foreach (var pair in options.Tokens)
        {
            parsed.Tokens[pair.Key] = pair.Value;
        }

        parsed.AllowedExecutableRoots.AddRange(options.AllowedExecutableRoots);
        return parsed;
    }

    public static string HelpText => """
        AetherWorkspace broker (local-only IPC host).

        Usage: Aether.BrokerApp --registry <path> --contexts <path> [options]

        Required:
          --registry <path>        Allowlisted app manifest (JSON).
          --contexts <path>        Trusted context catalog (JSON).

        Options:
          --transport <kind>       named-pipe | unix-domain-socket (platform default).
          --endpoint <value>       Explicit pipe name or socket path.
          --endpoint-file <path>   Write the operator handshake file (token, endpoint).
          --token NAME=VALUE       Launch template token (repeatable).
          --allowed-root <path>    Allowed executable root (repeatable).
          --operator-app <appId>   Non-launchable shell app identity (default aether.shell).
          --operator-token <val>   Explicit operator token (generated when omitted).
          --max-actions <n>        Plan action budget (demo default 6; library default 5).
          --work-area WxH          Virtual work area for deterministic layout (default 1920x1080).
          --audit-file <path>      Append the tamper-evident audit chain as JSONL.
          --audit-max-bytes <n>    Rotate the audit file at n bytes (min 4096; 0 disables).
          --audit-files <n>        Retain at most n rotated audit files (default 4).
          --child-log-dir <path>   Capture child app stdout/stderr lines for diagnostics.
          --run-seconds <n>        Stop after n seconds (smoke tests).
          --no-readiness-check     Do not wait for app subscription/window readiness.
          --no-win32-arrangement   Use the virtual headless arranger even on Windows.
        """;
}
