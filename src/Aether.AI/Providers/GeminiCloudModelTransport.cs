using System.Text;
using System.Text.Json;
using Aether.Contracts.Errors;
using Aether.Contracts.Json;
using Aether.Security.Secrets;

namespace Aether.AI.Providers;

/// <summary>
/// Native Google Gemini API transport (generateContent). Configuration is provided
/// by the resolved provider settings; the API key is read from the secret provider
/// on demand and never written to audit/logs/telemetry.
/// </summary>
public sealed class GeminiCloudModelTransport : ICloudModelTransport, IDisposable
{
    public const string DefaultBaseUrl = "https://generativelanguage.googleapis.com/v1beta";
    public const string DefaultApiKeyEnvironmentVariable = "GEMINI_API_KEY";
    public const string DefaultModelEnvironmentVariable = "GEMINI_MODEL";

    private readonly HttpClient _httpClient;
    private readonly ISecretProvider _secretProvider;
    private readonly string _baseUrl;
    private readonly string _model;
    private readonly string _apiKeyEnvironmentVariable;

    public GeminiCloudModelTransport(
        ISecretProvider secretProvider,
        string? baseUrl = null,
        string? model = null,
        string? apiKeyEnvironmentVariable = null,
        HttpClient? httpClient = null)
    {
        _secretProvider = secretProvider ?? throw new ArgumentNullException(nameof(secretProvider));
        _baseUrl = (baseUrl ?? Environment.GetEnvironmentVariable("GEMINI_BASE_URL") ?? DefaultBaseUrl).TrimEnd('/');
        _model = model ?? Environment.GetEnvironmentVariable(DefaultModelEnvironmentVariable) ?? "gemini-2.5-flash-lite";
        _apiKeyEnvironmentVariable = apiKeyEnvironmentVariable ?? DefaultApiKeyEnvironmentVariable;
        _httpClient = httpClient ?? new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
    }

    public string ProviderId => "gemini-native";

    public bool IsConfigured => !string.IsNullOrWhiteSpace(_baseUrl);

    public async Task<CloudModelResponse> SendAsync(CloudModelRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var apiKey = _secretProvider.GetSecret(_apiKeyEnvironmentVariable);
        if (string.IsNullOrEmpty(apiKey))
        {
            return CloudModelResponse.Fail(AetherErrorCodes.AiProviderMissing);
        }

        var payload = AetherJson.Serialize(new
        {
            systemInstruction = new { parts = new[] { new { text = request.SystemPrompt } } },
            contents = new[] { new { role = "user", parts = new[] { new { text = request.UserContent } } } },
            generationConfig = new { temperature = 0, maxOutputTokens = 1024, responseMimeType = "application/json" },
        });

        var endpoint = $"{_baseUrl}/models/{Uri.EscapeDataString(_model)}:generateContent";
        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, endpoint)
        {
            Content = new StringContent(payload, Encoding.UTF8, "application/json"),
        };
        httpRequest.Headers.TryAddWithoutValidation("x-goog-api-key", apiKey);

        try
        {
            using var response = await _httpClient.SendAsync(httpRequest, cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                return CloudModelResponse.Fail(AetherErrorCodes.AiProviderUnavailable);
            }

            var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            using var document = JsonDocument.Parse(body);
            if (document.RootElement.TryGetProperty("candidates", out var candidates) &&
                candidates.ValueKind == JsonValueKind.Array &&
                candidates.GetArrayLength() > 0 &&
                candidates[0].TryGetProperty("content", out var content) &&
                content.TryGetProperty("parts", out var parts) &&
                parts.ValueKind == JsonValueKind.Array)
            {
                var text = new StringBuilder();
                foreach (var part in parts.EnumerateArray())
                {
                    if (part.TryGetProperty("text", out var textElement) && textElement.ValueKind == JsonValueKind.String)
                    {
                        text.Append(textElement.GetString());
                    }
                }

                return text.Length == 0
                    ? CloudModelResponse.Fail(AetherErrorCodes.AiOutputInvalid)
                    : CloudModelResponse.Ok(text.ToString());
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
