using System.Net;
using System.Text;
using System.Text.Json;
using Aether.AI.Providers;
using Aether.Contracts;
using Aether.Contracts.Errors;
using Aether.Security.Policy;
using Aether.Security.Secrets;
using Xunit;

namespace Aether.AI.Tests;

public class GeminiTransportTests
{
    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly HttpStatusCode _status;
        private readonly string _body;
        public HttpRequestMessage? LastRequest { get; private set; }
        public string? LastBody { get; private set; }

        public StubHandler(HttpStatusCode status, string body)
        {
            _status = status;
            _body = body;
        }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastRequest = request;
            LastBody = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
            return new HttpResponseMessage(_status) { Content = new StringContent(_body, Encoding.UTF8, "application/json") };
        }
    }

    private static ISecretProvider Secrets() => new EnvironmentSecretProvider(new Dictionary<string, string>
    {
        ["GEMINI_API_KEY"] = "synthetic-test-key",
    });

    private static CloudModelRequest Request() => new(
        "trace-gemini", AiTaskKind.PlanGeneration, DataClassification.Public, "command", 20,
        "system prompt", "user content");

    [Fact]
    public async Task ValidGeminiResponseIsParsedWithNativeHeadersAndModelPath()
    {
        var handler = new StubHandler(HttpStatusCode.OK,
            """{"candidates":[{"content":{"parts":[{"text":"{\"ok\":true}"}]}}]}""");
        using var transport = new GeminiCloudModelTransport(Secrets(), baseUrl: "https://example.invalid/v1beta",
            model: "gemini-2.5-flash-lite", httpClient: new HttpClient(handler));

        var response = await transport.SendAsync(Request(), CancellationToken.None);

        Assert.True(response.Success);
        Assert.Equal("{\"ok\":true}", response.Content);
        var request = handler.LastRequest!;
        Assert.Equal("https://example.invalid/v1beta/models/gemini-2.5-flash-lite:generateContent", request.RequestUri!.ToString());
        Assert.True(request.Headers.TryGetValues("x-goog-api-key", out var keys));
        Assert.Equal("synthetic-test-key", keys!.Single());
        using var body = JsonDocument.Parse(handler.LastBody!);
        Assert.Equal("system prompt", body.RootElement.GetProperty("systemInstruction").GetProperty("parts")[0].GetProperty("text").GetString());
        Assert.Equal("user content", body.RootElement.GetProperty("contents")[0].GetProperty("parts")[0].GetProperty("text").GetString());
    }

    [Fact]
    public async Task MissingApiKeyFailsClosedWithoutNetworkCall()
    {
        var handler = new StubHandler(HttpStatusCode.OK, "{}");
        using var transport = new GeminiCloudModelTransport(
            new EnvironmentSecretProvider(new Dictionary<string, string>()),
            baseUrl: "https://example.invalid", httpClient: new HttpClient(handler));

        var response = await transport.SendAsync(Request(), CancellationToken.None);

        Assert.False(response.Success);
        Assert.Equal(AetherErrorCodes.AiProviderMissing, response.ErrorCode);
        Assert.Null(handler.LastRequest);
    }

    [Fact]
    public async Task HttpFailureAndMalformedOutputAreMappedToSafeErrorCodes()
    {
        var failing = new StubHandler(HttpStatusCode.TooManyRequests, "{}");
        using var transport = new GeminiCloudModelTransport(Secrets(), baseUrl: "https://example.invalid",
            httpClient: new HttpClient(failing));
        Assert.Equal(AetherErrorCodes.AiProviderUnavailable, (await transport.SendAsync(Request(), CancellationToken.None)).ErrorCode);

        var malformed = new StubHandler(HttpStatusCode.OK, "{\"candidates\":[]}");
        using var transport2 = new GeminiCloudModelTransport(Secrets(), baseUrl: "https://example.invalid",
            httpClient: new HttpClient(malformed));
        Assert.Equal(AetherErrorCodes.AiOutputInvalid, (await transport2.SendAsync(Request(), CancellationToken.None)).ErrorCode);
    }
}
