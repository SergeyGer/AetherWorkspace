using System.Net;
using System.Text;
using Aether.AI.Providers;
using Aether.Contracts;
using Aether.Contracts.Errors;
using Aether.Security.Policy;
using Aether.Security.Secrets;
using Xunit;

namespace Aether.AI.Tests;

/// <summary>v1.3 AW-060: streaming TTFT is measured from real SSE deltas, never estimated.</summary>
public sealed class StreamingOpenAiTransportTests
{
    private static readonly CloudModelRequest Request = new(
        "trace-stream", AiTaskKind.PlanGeneration, DataClassification.Public,
        "command", 12, "system", "user");

    [Fact]
    public async Task StreamingResponseCapturesFirstTokenAndFullContent()
    {
        const string first = "{\"schemaVersion\":\"1.0\",";
        const string second = "\"planId\":\"plan-1\",\"steps\":[]}";
        using var transport = new StreamingOpenAiTransport(
            new EnvironmentSecretProvider(new Dictionary<string, string> { ["AETHER_CLOUD_API_KEY"] = "synthetic" }),
            endpoint: "https://example.invalid/v1/chat/completions",
            modelId: "synthetic-model",
            apiKeyEnvironmentVariable: "AETHER_CLOUD_API_KEY",
            httpClient: new HttpClient(new SseHandler(Sse(first, second))));

        var result = await transport.SendStreamingAsync(Request, CancellationToken.None);

        Assert.True(result.Response.Success);
        Assert.Equal(first + second, result.Response.Content);
        Assert.NotNull(result.TimeToFirstTokenMs);
        Assert.True(result.TimeToFirstTokenMs >= 0);
    }

    [Fact]
    public async Task HttpErrorFailsWithoutFabricatingTtft()
    {
        using var transport = new StreamingOpenAiTransport(
            new EnvironmentSecretProvider(new Dictionary<string, string> { ["AETHER_CLOUD_API_KEY"] = "synthetic" }),
            endpoint: "https://example.invalid/v1/chat/completions",
            apiKeyEnvironmentVariable: "AETHER_CLOUD_API_KEY",
            httpClient: new HttpClient(new StaticHandler(HttpStatusCode.ServiceUnavailable)));

        var result = await transport.SendStreamingAsync(Request, CancellationToken.None);

        Assert.False(result.Response.Success);
        Assert.Equal(AetherErrorCodes.AiProviderUnavailable, result.Response.ErrorCode);
        Assert.Null(result.TimeToFirstTokenMs);
    }

    private static string Sse(params string[] deltas) =>
        string.Join("\n", deltas.Select(delta =>
            "data: {\"choices\":[{\"delta\":{\"content\":\"" + delta.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"}}]}"))
        + "\n\ndata: [DONE]\n\n";

    private sealed class SseHandler(string body) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(body, Encoding.UTF8, "text/event-stream"),
            });
    }

    private sealed class StaticHandler(HttpStatusCode status) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(status));
    }
}
