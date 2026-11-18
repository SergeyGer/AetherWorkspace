using System.Diagnostics;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Aether.Contracts.Errors;
using Aether.Contracts.Json;
using Aether.Security.Secrets;

namespace Aether.AI.Providers;

/// <summary>Result of one streaming model attempt, including the measured time to first token.</summary>
public sealed record StreamingCloudModelResult(CloudModelResponse Response, double? TimeToFirstTokenMs);

/// <summary>
/// Optional streaming OpenAI-compatible transport (v1.3 evaluation). It uses the same
/// chat-completions contract with <c>stream=true</c>, parses SSE chunks and measures the time until
/// the first non-empty content delta. The stopwatch starts immediately before the HTTP send; no
/// TTFT is estimated or fabricated when no token arrives.
/// </summary>
public sealed class StreamingOpenAiTransport : ICloudModelTransport, IDisposable
{
    private readonly HttpClient _httpClient;
    private readonly ISecretProvider _secretProvider;
    private readonly string _endpoint;
    private readonly string _modelId;
    private readonly string _apiKeyEnvironmentVariable;

    public StreamingOpenAiTransport(
        ISecretProvider secretProvider,
        string? endpoint = null,
        string? modelId = null,
        string? apiKeyEnvironmentVariable = null,
        HttpClient? httpClient = null)
    {
        _secretProvider = secretProvider ?? throw new ArgumentNullException(nameof(secretProvider));
        _endpoint = endpoint ?? Environment.GetEnvironmentVariable(HttpCloudModelTransport.DefaultEndpointEnvironmentVariable) ?? "";
        _modelId = modelId ?? Environment.GetEnvironmentVariable(HttpCloudModelTransport.DefaultModelEnvironmentVariable) ?? "configured-model";
        _apiKeyEnvironmentVariable = apiKeyEnvironmentVariable ?? HttpCloudModelTransport.DefaultApiKeyEnvironmentVariable;
        _httpClient = httpClient ?? new HttpClient { Timeout = TimeSpan.FromSeconds(60) };
    }

    public string ProviderId => "cloud-openai-compatible";

    public async Task<CloudModelResponse> SendAsync(CloudModelRequest request, CancellationToken cancellationToken) =>
        (await SendStreamingAsync(request, cancellationToken).ConfigureAwait(false)).Response;

    public async Task<StreamingCloudModelResult> SendStreamingAsync(CloudModelRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (string.IsNullOrWhiteSpace(_endpoint))
        {
            return new StreamingCloudModelResult(CloudModelResponse.Fail(AetherErrorCodes.AiCloudNotConfigured), null);
        }

        var apiKey = _secretProvider.GetSecret(_apiKeyEnvironmentVariable);
        if (string.IsNullOrEmpty(apiKey))
        {
            return new StreamingCloudModelResult(CloudModelResponse.Fail(AetherErrorCodes.AiProviderMissing), null);
        }

        var payload = AetherJson.Serialize(new
        {
            model = _modelId,
            stream = true,
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
        httpRequest.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));

        var stopwatch = Stopwatch.StartNew();
        double? firstTokenMs = null;
        try
        {
            using var response = await _httpClient
                .SendAsync(httpRequest, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
                .ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                return new StreamingCloudModelResult(CloudModelResponse.Fail(AetherErrorCodes.AiProviderUnavailable), null);
            }

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            using var reader = new StreamReader(stream, Encoding.UTF8);
            var content = new StringBuilder();
            while (await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false) is { } line)
            {
                if (string.IsNullOrWhiteSpace(line) || !line.StartsWith("data:", StringComparison.Ordinal))
                {
                    continue;
                }

                var data = line[5..].Trim();
                if (data == "[DONE]")
                {
                    break;
                }

                if (!TryReadDelta(data, out var delta))
                {
                    continue;
                }

                if (!string.IsNullOrEmpty(delta))
                {
                    firstTokenMs ??= stopwatch.Elapsed.TotalMilliseconds;
                    content.Append(delta);
                }
            }

            stopwatch.Stop();
            if (content.Length == 0)
            {
                return new StreamingCloudModelResult(CloudModelResponse.Fail(AetherErrorCodes.AiOutputInvalid), null);
            }

            return new StreamingCloudModelResult(CloudModelResponse.Ok(content.ToString()), firstTokenMs);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException)
        {
            return new StreamingCloudModelResult(CloudModelResponse.Fail(AetherErrorCodes.AiProviderUnavailable), null);
        }
        catch (HttpRequestException)
        {
            return new StreamingCloudModelResult(CloudModelResponse.Fail(AetherErrorCodes.AiProviderUnavailable), null);
        }
        catch (JsonException)
        {
            return new StreamingCloudModelResult(CloudModelResponse.Fail(AetherErrorCodes.AiOutputInvalid), null);
        }
    }

    private static bool TryReadDelta(string data, out string? delta)
    {
        delta = null;
        try
        {
            using var document = JsonDocument.Parse(data);
            if (document.RootElement.TryGetProperty("choices", out var choices)
                && choices.ValueKind == JsonValueKind.Array
                && choices.GetArrayLength() > 0
                && choices[0].TryGetProperty("delta", out var deltaElement)
                && deltaElement.TryGetProperty("content", out var contentElement)
                && contentElement.ValueKind == JsonValueKind.String)
            {
                delta = contentElement.GetString();
                return true;
            }
        }
        catch (JsonException)
        {
        }

        return false;
    }

    public void Dispose() => _httpClient.Dispose();
}
