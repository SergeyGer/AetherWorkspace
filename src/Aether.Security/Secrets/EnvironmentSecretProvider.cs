namespace Aether.Security.Secrets;

/// <summary>
/// Development-friendly secret access. Real deployments must use Windows
/// Credential Manager or secure environment injection; secrets are never
/// written to the repository, sample files, audit entries or telemetry.
/// </summary>
public interface ISecretProvider
{
    string? GetSecret(string name);

    bool HasSecret(string name);
}

public sealed class EnvironmentSecretProvider : ISecretProvider
{
    private readonly IReadOnlyDictionary<string, string> _values;

    public EnvironmentSecretProvider(IReadOnlyDictionary<string, string>? values = null) =>
        _values = values ?? new Dictionary<string, string>(StringComparer.Ordinal);

    public string? GetSecret(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return null;
        }

        if (_values.TryGetValue(name, out var value))
        {
            return string.IsNullOrWhiteSpace(value) ? null : value;
        }

        var fromEnvironment = Environment.GetEnvironmentVariable(name);
        return string.IsNullOrWhiteSpace(fromEnvironment) ? null : fromEnvironment;
    }

    public bool HasSecret(string name) => GetSecret(name) is not null;
}
