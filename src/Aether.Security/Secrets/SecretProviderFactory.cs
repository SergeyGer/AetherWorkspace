namespace Aether.Security.Secrets;

/// <summary>Selects the credential source without ever logging secret values (v1.3 AW-067).</summary>
public static class SecretProviderFactory
{
    public const string CredentialStoreEnvironmentVariable = "AETHER_CREDENTIAL_STORE";

    public const string EnvironmentStore = "environment";
    public const string WindowsCredentialManagerStore = "windows-credential-manager";

    /// <summary>
    /// Builds the configured provider. The default remains explicit environment/.env injection so
    /// the portable CI and headless tests never depend on Windows Credential Manager.
    /// </summary>
    public static ISecretProvider Create(IReadOnlyDictionary<string, string>? values = null)
    {
        var configured = ModelStore(values).Trim().ToLowerInvariant();
        return configured switch
        {
            "" or EnvironmentStore => new EnvironmentSecretProvider(values),
            WindowsCredentialManagerStore or "windows" => CreateWindowsCredentialManager(),
            _ => throw new ArgumentException(
                $"Unsupported {CredentialStoreEnvironmentVariable} value. Use '{EnvironmentStore}' or '{WindowsCredentialManagerStore}'."),
        };
    }

    private static string ModelStore(IReadOnlyDictionary<string, string>? values)
    {
        if (values is not null && values.TryGetValue(CredentialStoreEnvironmentVariable, out var configured))
        {
            return configured;
        }

        return Environment.GetEnvironmentVariable(CredentialStoreEnvironmentVariable) ?? "";
    }

    private static ISecretProvider CreateWindowsCredentialManager()
    {
        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException("Windows Credential Manager is only available on Windows.");
        }

        return new WindowsCredentialManagerSecretProvider();
    }
}
