using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Aether.Contracts.Errors;
using Aether.Contracts.Json;

namespace Aether.AI.Providers;

/// <summary>
/// Optional loopback local inference adapter. A loopback service is not
/// automatically air-gapped; operators must verify network behavior before
/// claiming "offline verified". When no local model is installed the
/// deterministic local summary remains available (reduced-function path).
/// </summary>
public sealed class LoopbackLocalModelTransport : ILocalModelTransport, IDisposable
{
    public const string DefaultEndpointEnvironmentVariable = "AETHER_LOCAL_MODEL_ENDPOINT";
    public const string DefaultModelEnvironmentVariable = "AETHER_LOCAL_MODEL";

    private readonly HttpClient _httpClient;
    private readonly string _endpoint;
    private readonly string _modelId;

    public LoopbackLocalModelTransport(string? endpoint = null, string? modelId = null, HttpClient? httpClient = null)
    {
        _endpoint = endpoint ?? Environment.GetEnvironmentVariable(DefaultEndpointEnvironmentVariable) ?? "";
        _modelId = modelId ?? Environment.GetEnvironmentVariable(DefaultModelEnvironmentVariable) ?? "local-model";
        _httpClient = httpClient ?? new HttpClient { Timeout = TimeSpan.FromSeconds(60) };
    }

    public string ProviderId => "local-loopback";

    /// <summary>Whether a loopback local model endpoint is configured (no network call is made).</summary>
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
            return CloudModelResponse.Fail(AetherErrorCodes.AiProviderMissing);
        }

        var payload = AetherJson.Serialize(new
        {
            model = _modelId,
            stream = false,
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
        httpRequest.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

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
