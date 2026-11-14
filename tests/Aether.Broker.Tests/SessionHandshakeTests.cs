using Aether.Broker;
using Aether.Broker.Launching;
using Aether.Broker.Sessions;
using Aether.Contracts;
using Aether.Broker.Client;
using Aether.Contracts.Broker;
using Aether.Contracts.Errors;
using Aether.Contracts.V1;
using Aether.Testing;
using Grpc.Core;
using Xunit;

namespace Aether.Broker.Tests;

public class SessionHandshakeTests
{
    private static readonly string TestExecutablePath = Path.Combine(Path.GetTempPath(), "aether-test-app");

    private static async Task<(BrokerTestContext Context, FakeProcessIdentityResolver Identity, ManualTimeProvider Time)> StartAsync()
    {
        var identity = new FakeProcessIdentityResolver();
        var time = new ManualTimeProvider(new DateTimeOffset(2026, 11, 14, 12, 0, 0, TimeSpan.Zero));
        identity.Register(Environment.ProcessId, TestExecutablePath, time.GetUtcNow());
        var context = BrokerTestContext.Create(identityResolver: identity, timeProvider: time, testName: "session");
        await context.StartAsync();
        return (context, identity, time);
    }

    private static BrokerClient AppClient(BrokerTestContext context) => context.CreateClient();

    [Fact]
    public async Task ValidSingleUseTokenBindsTheSessionToTheLaunchedIdentity()
    {
        var (context, _, time) = await StartAsync();
        await using var contextScope = context;
        var registry = TestData.CreateRegistry();
        var app = registry.GetRequired("sample.watchlist");
        const string token = "single-use-token";
        var instanceId = "inst-handshake-1";
        context.Host.Sessions.AddPendingRegistration(
            app,
            instanceId,
            token,
            new ProcessIdentity(Environment.ProcessId, TestExecutablePath, time.GetUtcNow()),
            time.GetUtcNow());

        await using var client = AppClient(context);
        var registration = await client.RegisterAsync("sample.watchlist", token, instanceId);

        Assert.False(string.IsNullOrEmpty(registration.SessionId));
        Assert.False(string.IsNullOrEmpty(registration.SessionSecret));
        Assert.Contains("window.report", registration.Capabilities);
        Assert.Equal(Classification.Restricted, registration.MaxClassification);
        Assert.True(registration.ExpiresAtUnixMs > 0);
    }

    [Fact]
    public async Task ReplayedRegistrationTokenIsRejected()
    {
        var (context, _, time) = await StartAsync();
        await using var contextScope = context;
        var app = TestData.CreateRegistry().GetRequired("sample.watchlist");
        const string token = "replay-token";
        context.Host.Sessions.AddPendingRegistration(
            app, "inst-replay", token,
            new ProcessIdentity(Environment.ProcessId, TestExecutablePath, time.GetUtcNow()),
            time.GetUtcNow());

        await using (var first = AppClient(context))
        {
            await first.RegisterAsync("sample.watchlist", token, "inst-replay");
        }

        await using var second = AppClient(context);
        var exception = await Assert.ThrowsAsync<BrokerRpcException>(() =>
            second.RegisterAsync("sample.watchlist", token, "inst-replay"));
        Assert.Equal(AetherErrorCodes.AuthTokenInvalid, exception.Code);
    }

    [Fact]
    public async Task UnknownRegistrationTokenIsRejected()
    {
        var (context, _, _) = await StartAsync();
        await using var contextScope = context;
        await using var client = AppClient(context);

        var exception = await Assert.ThrowsAsync<BrokerRpcException>(() =>
            client.RegisterAsync("sample.watchlist", "not-a-real-token", "inst-unknown"));
        Assert.Equal(AetherErrorCodes.AuthTokenInvalid, exception.Code);
    }

    [Fact]
    public async Task ExpiredPendingTokenIsRejected()
    {
        var (context, _, time) = await StartAsync();
        await using var contextScope = context;
        var app = TestData.CreateRegistry().GetRequired("sample.watchlist");
        const string token = "expired-token";
        context.Host.Sessions.AddPendingRegistration(
            app, "inst-expired", token,
            new ProcessIdentity(Environment.ProcessId, TestExecutablePath, time.GetUtcNow()),
            time.GetUtcNow()
        );

        time.Advance(TimeSpan.FromMinutes(5));

        await using var client = AppClient(context);
        var exception = await Assert.ThrowsAsync<BrokerRpcException>(() =>
            client.RegisterAsync("sample.watchlist", token, "inst-expired"));
        Assert.Equal(AetherErrorCodes.AuthTokenInvalid, exception.Code);
    }

    [Fact]
    public async Task ProcessIdentityMismatchIsRejected()
    {
        var (context, identity, time) = await StartAsync();
        await using var contextScope = context;
        var app = TestData.CreateRegistry().GetRequired("sample.watchlist");
        const string token = "identity-token";
        context.Host.Sessions.AddPendingRegistration(
            app, "inst-mismatch", token,
            new ProcessIdentity(Environment.ProcessId, TestExecutablePath, time.GetUtcNow()),
            time.GetUtcNow());

        identity.Register(Environment.ProcessId, Path.Combine(Path.GetTempPath(), "some-other-binary"), time.GetUtcNow());

        await using var client = AppClient(context);
        var exception = await Assert.ThrowsAsync<BrokerRpcException>(() =>
            client.RegisterAsync("sample.watchlist", token, "inst-mismatch"));
        Assert.Equal(AetherErrorCodes.AuthIdentityMismatch, exception.Code);
    }

    [Fact]
    public async Task OperatorTokenMustMatchExactly()
    {
        var (context, _, _) = await StartAsync();
        await using var contextScope = context;
        await using var client = AppClient(context);

        var exception = await Assert.ThrowsAsync<BrokerRpcException>(() =>
            client.RegisterAsync(context.Endpoint.OperatorAppId, "wrong-operator-token", "op-wrong"));
        Assert.Equal(AetherErrorCodes.AuthOperatorTokenInvalid, exception.Code);
    }

    [Fact]
    public async Task UnsupportedProtocolVersionIsRejected()
    {
        var (context, _, time) = await StartAsync();
        await using var contextScope = context;

        var result = context.Host.Sessions.Register(new RegisterRequest
        {
            AppId = "sample.watchlist",
            InstanceId = "inst-proto",
            RegistrationToken = "whatever",
            ProcessId = Environment.ProcessId,
            ProtocolVersion = "9",
        }, time.GetUtcNow());

        Assert.False(result.Success);
        Assert.Equal(AetherErrorCodes.AuthProtocolUnsupported, result.DenialCode);
    }

    [Fact]
    public async Task HeartbeatWorksWhileTheSessionIsValidAndFailsAfterExpiry()
    {
        var (context, _, time) = await StartAsync();
        await using var contextScope = context;
        var app = TestData.CreateRegistry().GetRequired("sample.risk");
        const string token = "heartbeat-token";
        context.Host.Sessions.AddPendingRegistration(
            app, "inst-heartbeat", token,
            new ProcessIdentity(Environment.ProcessId, TestExecutablePath, time.GetUtcNow()),
            time.GetUtcNow());

        await using var client = AppClient(context);
        await client.RegisterAsync("sample.risk", token, "inst-heartbeat");
        var heartbeat = await client.HeartbeatAsync();
        Assert.True(heartbeat.SessionValid);

        time.Advance(TimeSpan.FromHours(3));

        var exception = await Assert.ThrowsAsync<BrokerRpcException>(() => client.HeartbeatAsync());
        Assert.Equal(AetherErrorCodes.AuthSessionInvalid, exception.Code);
    }
}
