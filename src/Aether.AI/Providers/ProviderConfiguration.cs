namespace Aether.AI.Providers;

/// <summary>
/// Resolved provider configuration from environment variables or an explicitly
/// loaded <c>.env</c> dictionary. API key values are never stored here; only the
/// variable name is carried, and the secret provider resolves it on demand.
/// </summary>
public sealed record ModelProviderSettings(
    string ProviderId,
    string Model,
    string EndpointEnvironmentVariable,
    string ApiKeyEnvironmentVariable,
    string ModelEnvironmentVariable,
    bool IsCloud,
    bool EndpointConfigured,
    bool ApiKeyConfigured)
{
    public const string DefaultProviderEnvironmentVariable = "AETHER_MODEL_PROVIDER";

    public static ModelProviderSettings FromEnvironment(IReadOnlyDictionary<string, string>? overrides = null)
    {
        var provider = Read(overrides, DefaultProviderEnvironmentVariable)?.Trim().ToLowerInvariant();
        static bool Has(IReadOnlyDictionary<string, string>? values, string name) => Read(values, name) is not null;

        return provider switch
        {
            "gemini" => new ModelProviderSettings(
                "gemini",
                Read(overrides, "GEMINI_MODEL") ?? "gemini-2.5-flash-lite",
                "GEMINI_BASE_URL",
                "GEMINI_API_KEY",
                "GEMINI_MODEL",
                IsCloud: true,
                EndpointConfigured: Has(overrides, "GEMINI_BASE_URL"),
                ApiKeyConfigured: Has(overrides, "GEMINI_API_KEY")),
            "local" or "ollama" => new ModelProviderSettings(
                "local",
                Read(overrides, LoopbackLocalModelTransport.DefaultModelEnvironmentVariable) ?? "qwen2.5:3b",
                LoopbackLocalModelTransport.DefaultEndpointEnvironmentVariable,
                "",
                LoopbackLocalModelTransport.DefaultModelEnvironmentVariable,
                IsCloud: false,
                EndpointConfigured: Has(overrides, LoopbackLocalModelTransport.DefaultEndpointEnvironmentVariable),
                ApiKeyConfigured: true),
            _ => new ModelProviderSettings(
                "openai",
                Read(overrides, "OPENAI_MODEL")
                    ?? Read(overrides, HttpCloudModelTransport.DefaultModelEnvironmentVariable)
                    ?? "gpt-4o-mini",
                "OPENAI_BASE_URL",
                "OPENAI_API_KEY",
                "OPENAI_MODEL",
                IsCloud: true,
                EndpointConfigured: Has(overrides, "OPENAI_BASE_URL") || Has(overrides, HttpCloudModelTransport.DefaultEndpointEnvironmentVariable),
                ApiKeyConfigured: Has(overrides, "OPENAI_API_KEY") || Has(overrides, HttpCloudModelTransport.DefaultApiKeyEnvironmentVariable)),
        };
    }

    /// <summary>Whether the resolved provider has an endpoint/key configuration (no network I/O).</summary>
    public bool IsConfigured => IsCloud ? EndpointConfigured && ApiKeyConfigured : EndpointConfigured;

    public string? ReadEndpoint(IReadOnlyDictionary<string, string>? overrides = null) => Read(overrides, EndpointEnvironmentVariable);

    public string? ReadApiKey(IReadOnlyDictionary<string, string>? overrides = null) => Read(overrides, ApiKeyEnvironmentVariable);

    public static string? Read(IReadOnlyDictionary<string, string>? overrides, string name)
    {
        if (overrides is not null && overrides.TryGetValue(name, out var overrideValue) && !string.IsNullOrWhiteSpace(overrideValue))
        {
            return overrideValue;
        }

        var value = Environment.GetEnvironmentVariable(name);
        return string.IsNullOrWhiteSpace(value) ? null : value;
    }

}
