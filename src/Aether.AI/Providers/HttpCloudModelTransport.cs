using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Aether.Contracts.Errors;
using Aether.Contracts.Json;
using Aether.Security.Secrets;

namespace Aether.AI.Providers;

/// <summary>
/// Optional OpenAI-compatible HTTP transport for the single configured cloud
/// adapter. The endpoint, model id and API key are configuration values; the
/// API key comes from the environment or Windows Credential Manager shim and is
/// never written to disk, audit events or telemetry.
/// </summary>
public sealed class HttpCloudModelTransport : ICloudModelTransport, IDisposable
{
    public const string DefaultEndpointEnvironmentVariable = "AETHER_CLOUD_ENDPOINT";
    public const string DefaultApiKeyEnvironmentVariable = "AETHER_CLOUD_API_KEY";
    public const string DefaultModelEnvironmentVariable = "AETHER_CLOUD_MODEL";

    private readonly HttpClient _httpClient;
    private readonly ISecretProvider _secretProvider;
    private readonly string _endpoint;
    private readonly string _modelId;
    private readonly string _apiKeyEnvironmentVariable;

    public HttpCloudModelTransport(
        ISecretProvider secretProvider,
        string? endpoint = null,
        string? modelId = null,
        string? apiKeyEnvironmentVariable = null,
        HttpClient? httpClient = null)
    {
        _secretProvider = secretProvider ?? throw new ArgumentNullException(nameof(secretProvider));
        _endpoint = endpoint ?? Environment.GetEnvironmentVariable(DefaultEndpointEnvironmentVariable) ?? "";
        _modelId = modelId ?? Environment.GetEnvironmentVariable(DefaultModelEnvironmentVariable) ?? "configured-model";
        _apiKeyEnvironmentVariable = apiKeyEnvironmentVariable ?? DefaultApiKeyEnvironmentVariable;
        _httpClient = httpClient ?? new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
    }

    public string ProviderId => "cloud-openai-compatible";

    /// <summary>Whether a cloud endpoint is configured (no network call is made).</summary>
    public static bool IsEndpointConfigured(IReadOnlyDictionary<string, string>? overrides = null)
    {
        if (overrides is not null &&
            overrides.TryGetValue(DefaultEndpointEnvironmentVariable, out var overridden) &&
            !string.IsNullOrWhiteSpace(overridden))
        {
            return true;
        }

        return !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(DefaultEndpointEnvironmentVariable));
    }

    public bool IsConfigured => !string.IsNullOrWhiteSpace(_endpoint);

    public async Task<CloudModelResponse> SendAsync(CloudModelRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (!IsConfigured)
        {
            return CloudModelResponse.Fail(AetherErrorCodes.AiCloudNotConfigured);
        }

        var apiKey = _secretProvider.GetSecret(_apiKeyEnvironmentVariable);
        if (string.IsNullOrEmpty(apiKey))
        {
            return CloudModelResponse.Fail(AetherErrorCodes.AiProviderMissing);
        }

        var payload = AetherJson.Serialize(new
        {
            model = _modelId,
            temperature = 0,
            messages = new[]
            {
                new { role = "system", content = request.SystemPrompt },
                new { role = "user", content = request.UserContent },
            },
        });

        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, _endpoint)
        {
            Content = new StringContent(payload, Encoding.UTF8, "application/json"),
        };
        httpRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);

        try
        {
            using var response = await _httpClient.SendAsync(httpRequest, cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                return CloudModelResponse.Fail(AetherErrorCodes.AiProviderUnavailable);
            }

            var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            using var document = JsonDocument.Parse(body);
            if (document.RootElement.TryGetProperty("choices", out var choices) &&
                choices.ValueKind == JsonValueKind.Array &&
                choices.GetArrayLength() > 0 &&
                choices[0].TryGetProperty("message", out var message) &&
                message.TryGetProperty("content", out var content) &&
                content.ValueKind == JsonValueKind.String)
            {
                return CloudModelResponse.Ok(content.GetString() ?? "");
            }

            return CloudModelResponse.Fail(AetherErrorCodes.AiOutputInvalid);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (HttpRequestException)
        {
            return CloudModelResponse.Fail(AetherErrorCodes.AiProviderUnavailable);
        }
        catch (JsonException)
        {
            return CloudModelResponse.Fail(AetherErrorCodes.AiOutputInvalid);
        }
    }

    public void Dispose() => _httpClient.Dispose();
}
