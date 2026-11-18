namespace Aether.AI.Providers;

/// <summary>AI capability whose provider route is configured independently (v1.3).</summary>
public enum ModelCapability
{
    Planning = 0,
    Summarization = 1,
}

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
    public const string PlannerProviderEnvironmentVariable = "AETHER_PLANNER_PROVIDER";
    public const string SummaryProviderEnvironmentVariable = "AETHER_SUMMARY_PROVIDER";

    /// <summary>Validation error text (never contains secret values); null when valid.</summary>
    public string? ConfigurationError { get; init; }

    /// <summary>Capability this settings object was resolved for.</summary>
    public ModelCapability Capability { get; init; } = ModelCapability.Planning;

    /// <summary>Whether the resolved provider has a usable configuration (no network I/O).</summary>
    public bool IsConfigured => ProviderId == "deterministic" || (IsCloud ? EndpointConfigured && ApiKeyConfigured : EndpointConfigured);

    /// <summary>Whether the provider is configured and passed local validation.</summary>
    public bool IsValid => IsConfigured && ConfigurationError is null;

    public static ModelProviderSettings FromEnvironment(
        IReadOnlyDictionary<string, string>? overrides = null,
        ModelCapability capability = ModelCapability.Planning)
    {
        var providerVariable = capability == ModelCapability.Summarization
            ? SummaryProviderEnvironmentVariable
            : PlannerProviderEnvironmentVariable;
        var provider = (Read(overrides, providerVariable) ?? Read(overrides, DefaultProviderEnvironmentVariable))
            ?.Trim()
            .ToLowerInvariant();
        static bool Has(IReadOnlyDictionary<string, string>? values, string name) => Read(values, name) is not null;

        var settings = provider switch
        {
            "deterministic" => new ModelProviderSettings(
                "deterministic",
                "deterministic-grammar",
                "",
                "",
                "",
                IsCloud: false,
                EndpointConfigured: false,
                ApiKeyConfigured: false),
            "gemini" => new ModelProviderSettings(
                "gemini",
                Read(overrides, "GEMINI_MODEL") ?? "gemini-3.1-flash-lite",
                "GEMINI_BASE_URL",
                "GEMINI_API_KEY",
                "GEMINI_MODEL",
                IsCloud: true,
                EndpointConfigured: Has(overrides, "GEMINI_BASE_URL"),
                ApiKeyConfigured: Has(overrides, "GEMINI_API_KEY")),
            "local" or "ollama" => new ModelProviderSettings(
                "local",
                Read(overrides, LoopbackLocalModelTransport.DefaultModelEnvironmentVariable) ?? "qwen2.5:7b",
                LoopbackLocalModelTransport.DefaultEndpointEnvironmentVariable,
                "",
                LoopbackLocalModelTransport.DefaultModelEnvironmentVariable,
                IsCloud: false,
                EndpointConfigured: Has(overrides, LoopbackLocalModelTransport.DefaultEndpointEnvironmentVariable),
                ApiKeyConfigured: true),
            "openai" or "openai-compatible" or null => new ModelProviderSettings(
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
            _ => new ModelProviderSettings(
                provider,
                "",
                "",
                "",
                "",
                IsCloud: false,
                EndpointConfigured: false,
                ApiKeyConfigured: false)
            {
                ConfigurationError = $"Unsupported provider '{provider}' in {providerVariable} (expected openai, gemini, local, ollama or deterministic).",
            },
        };

        settings = settings with { Capability = capability };
        return Validate(settings, overrides);
    }

    /// <summary>
    /// Validates syntax only; never reads or logs secret values. Cloud endpoints must be HTTPS
    /// (except an explicit local development gateway on a loopback host); local endpoints must be
    /// loopback HTTP(S). The docs state that a loopback service is not automatically air-gapped.
    /// </summary>
    public static ModelProviderSettings Validate(ModelProviderSettings settings, IReadOnlyDictionary<string, string>? overrides = null)
    {
        ArgumentNullException.ThrowIfNull(settings);
        if (settings.ConfigurationError is not null || settings.ProviderId == "deterministic")
        {
            return settings;
        }

        if (string.IsNullOrWhiteSpace(settings.Model))
        {
            return settings with { ConfigurationError = "The provider model id is empty." };
        }

        var endpoint = settings.ReadEndpoint(overrides);
        if (string.IsNullOrWhiteSpace(endpoint))
        {
            // Missing endpoint/key is an unconfigured provider (normal fallback), not an invalid
            // configuration directive. The router reports provider.cloud_not_configured for it.
            return settings;
        }

        if (!Uri.TryCreate(endpoint, UriKind.Absolute, out var uri))
        {
            return settings with { ConfigurationError = "The provider endpoint is not an absolute URI." };
        }

        if (settings.IsCloud)
        {
            if (uri.Scheme != Uri.UriSchemeHttps && !IsLoopback(uri))
            {
                return settings with { ConfigurationError = "Cloud provider endpoints must use HTTPS unless they are a loopback development gateway." };
            }

            return settings;
        }

        if (!IsLoopback(uri))
        {
            return settings with { ConfigurationError = "The local model endpoint must be loopback-only (localhost/127.0.0.1/::1)." };
        }

        return settings;
    }

    private static bool IsLoopback(Uri uri) =>
        uri.IsLoopback
        || string.Equals(uri.Host, "localhost", StringComparison.OrdinalIgnoreCase)
        || string.Equals(uri.Host, "127.0.0.1", StringComparison.Ordinal)
        || string.Equals(uri.Host, "::1", StringComparison.Ordinal);

    public string? ReadEndpoint(IReadOnlyDictionary<string, string>? overrides = null) => Read(overrides, EndpointEnvironmentVariable);

    public string? ReadApiKey(IReadOnlyDictionary<string, string>? overrides = null) => Read(overrides, ApiKeyEnvironmentVariable);

    public static string? Read(IReadOnlyDictionary<string, string>? overrides, string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return null;
        }

        if (overrides is not null && overrides.TryGetValue(name, out var overrideValue) && !string.IsNullOrWhiteSpace(overrideValue))
        {
            return overrideValue;
        }

        var value = Environment.GetEnvironmentVariable(name);
        return string.IsNullOrWhiteSpace(value) ? null : value;
    }
}

/// <summary>
/// Per-capability provider configuration (v1.3 AW-059): planning and summarization may select
/// different providers, falling back to <c>AETHER_MODEL_PROVIDER</c>. Configuration errors are
/// fail-closed inputs to routing; they are never silently replaced with another brand.
/// </summary>
public sealed record ModelProviderCatalog(
    ModelProviderSettings Planner,
    ModelProviderSettings Summarization,
    string? ConfigurationError)
{
    public static ModelProviderCatalog FromEnvironment(IReadOnlyDictionary<string, string>? overrides = null)
    {
        var planner = ModelProviderSettings.FromEnvironment(overrides, ModelCapability.Planning);
        var summarization = ModelProviderSettings.FromEnvironment(overrides, ModelCapability.Summarization);
        var errors = new[] { planner.ConfigurationError, summarization.ConfigurationError }
            .Where(error => !string.IsNullOrWhiteSpace(error))
            .ToArray();
        return new ModelProviderCatalog(planner, summarization, errors.Length == 0 ? null : string.Join(" ", errors));
    }

    public bool HasConfigurationError => ConfigurationError is not null;
}
