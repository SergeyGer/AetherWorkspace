using Aether.Contracts.Broker;
using Aether.Testing;
using Xunit;

namespace Aether.Contracts.Tests;

public class BrokerEndpointInfoTests
{
    [Fact]
    public void HandshakeFileRoundTripsAndIsUserOnlyOnUnix()
    {
        using var directory = new TempDirectory("endpoint");
        var path = directory.PathTo("broker-endpoint.json");
        var info = new BrokerEndpointInfo
        {
            Transport = BrokerTransports.UnixDomainSocket,
            Endpoint = "/tmp/aether/b.sock",
            OperatorAppId = "aether.shell",
            OperatorToken = "test-token-value",
            AuditFile = "/tmp/audit.jsonl",
        };

        info.Write(path);
        var parsed = BrokerEndpointInfo.Read(path);

        Assert.Equal(info.Transport, parsed.Transport);
        Assert.Equal(info.Endpoint, parsed.Endpoint);
        Assert.Equal(info.OperatorToken, parsed.OperatorToken);
        Assert.Equal(info.MaxActionsPerPlan, parsed.MaxActionsPerPlan);

        if (!OperatingSystem.IsWindows())
        {
            var mode = File.GetUnixFileMode(path);
            Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite, mode);
        }
    }
}
