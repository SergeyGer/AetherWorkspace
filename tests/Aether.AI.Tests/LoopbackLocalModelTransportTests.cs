using System.Net;
using Aether.AI.Providers;
using Aether.Contracts;
using Aether.Contracts.Errors;
using Aether.Security.Policy;
using Xunit;

namespace Aether.AI.Tests;

/// <summary>v1.3 AW-067: local provider failure/timeout semantics; no cloud fallback is possible here.</summary>
public sealed class LoopbackLocalModelTransportTests
{
    private static readonly CloudModelRequest Request = new(
        "trace-local", AiTaskKind.PlanGeneration, DataClassification.Public,
        "command", 12, "system", "user");

    [Fact]
    public async Task MissingEndpointReturnsProviderMissingWithoutNetworkCall()
    {
        using var transport = new LoopbackLocalModelTransport(endpoint: "");
        var response = await transport.SendAsync(Request, CancellationToken.None);
        Assert.False(response.Success);
        Assert.Equal(AetherErrorCodes.AiProviderMissing, response.ErrorCode);
    }

    [Fact]
    public async Task HttpFailureReturnsTypedUnavailable()
    {
        using var transport = new LoopbackLocalModelTransport(
            endpoint: "http://127.0.0.1:11434/v1/chat/completions",
            httpClient: new HttpClient(new StaticHandler(HttpStatusCode.ServiceUnavailable)));
        var response = await transport.SendAsync(Request, CancellationToken.None);
        Assert.False(response.Success);
        Assert.Equal(AetherErrorCodes.AiProviderUnavailable, response.ErrorCode);
    }

    [Fact]
    public async Task TimeoutReturnsTypedUnavailableInsteadOfThrowing()
    {
        using var client = new HttpClient(new SlowHandler(TimeSpan.FromSeconds(5)))
        {
            Timeout = TimeSpan.FromMilliseconds(25),
        };
        using var transport = new LoopbackLocalModelTransport(
            endpoint: "http://127.0.0.1:11434/v1/chat/completions",
            httpClient: client);
        var response = await transport.SendAsync(Request, CancellationToken.None);
        Assert.False(response.Success);
        Assert.Equal(AetherErrorCodes.AiProviderUnavailable, response.ErrorCode);
    }

    [Fact]
    public async Task MalformedProviderJsonReturnsInvalidOutput()
    {
        using var transport = new LoopbackLocalModelTransport(
            endpoint: "http://127.0.0.1:11434/v1/chat/completions",
            httpClient: new HttpClient(new StaticHandler(HttpStatusCode.OK, "not-json")));
        var response = await transport.SendAsync(Request, CancellationToken.None);
        Assert.False(response.Success);
        Assert.Equal(AetherErrorCodes.AiOutputInvalid, response.ErrorCode);
    }

    private sealed class StaticHandler(HttpStatusCode status, string? body = null) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(status)
            {
                Content = new StringContent(body ?? "{\"choices\":[{\"message\":{\"content\":\"{\\\"summary\\\":\\\"ok\\\"}\"}}]}"),
            });
    }

    private sealed class SlowHandler(TimeSpan delay) : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            await Task.Delay(delay, cancellationToken);
            return new HttpResponseMessage(HttpStatusCode.OK);
        }
    }
}
