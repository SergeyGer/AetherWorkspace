using Aether.Contracts.Errors;

namespace Aether.Security.Secrets;

/// <summary>
/// Minimal, deterministic `.env` parser used only with an explicit <c>--env-file</c>
/// argument. Values are loaded into an in-memory secret provider; nothing is written to
/// audit/logs/telemetry, and no shell expansion or process-environment mutation happens.
/// </summary>
public static class DotEnvFile
{
    public static IReadOnlyDictionary<string, string> Load(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new AetherContractException(AetherErrorCodes.ConfigInvalidEnvFile, "An environment file path is required.");
        }

        if (!File.Exists(path))
        {
            throw new AetherContractException(AetherErrorCodes.ConfigInvalidEnvFile, $"Environment file not found: {path}");
        }

        return Parse(File.ReadAllLines(path), path);
    }

    public static IReadOnlyDictionary<string, string> Parse(IEnumerable<string> lines, string source = "<env>")
    {
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        var lineNumber = 0;
        foreach (var rawLine in lines)
        {
            lineNumber++;
            var line = rawLine.Trim();
            if (line.Length == 0 || line.StartsWith('#'))
            {
                continue;
            }

            if (line.StartsWith("export ", StringComparison.Ordinal))
            {
                line = line["export ".Length..].TrimStart();
            }

            var separator = line.IndexOf('=');
            if (separator <= 0)
            {
                throw new AetherContractException(
                    AetherErrorCodes.ConfigInvalidEnvFile,
                    $"Invalid environment file line {lineNumber} in {source}: expected NAME=VALUE.");
            }

            var key = line[..separator].Trim();
            if (!IsValidKey(key))
            {
                throw new AetherContractException(
                    AetherErrorCodes.ConfigInvalidEnvFile,
                    $"Invalid environment variable name on line {lineNumber} in {source}.");
            }

            var value = line[(separator + 1)..].Trim();
            var quoted = value.Length >= 2 &&
                         ((value[0] == '"' && value[^1] == '"') || (value[0] == '\'' && value[^1] == '\''));
            if (quoted)
            {
                value = value[1..^1];
            }
            else
            {
                // Support the common "VALUE  # comment" form without treating URL fragments as comments.
                var commentStart = value.IndexOf(" #", StringComparison.Ordinal);
                if (commentStart > 0)
                {
                    value = value[..commentStart].TrimEnd();
                }
            }

            values[key] = value;
        }

        return values;
    }

    private static bool IsValidKey(string key) =>
        key.Length > 0 &&
        (char.IsAsciiLetter(key[0]) || key[0] == '_') &&
        key.All(character => char.IsAsciiLetterOrDigit(character) || character == '_');
}
