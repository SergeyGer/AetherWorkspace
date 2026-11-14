using Aether.Security.Consent;
using Aether.Security.Secrets;
using Xunit;

namespace Aether.Security.Tests;

public class ConsentAndSecretsTests
{
    [Fact]
    public void DefaultConsentStateIsDisabled()
    {
        Assert.False(ConsentState.Disabled.CloudEnabled);
        Assert.False(ConsentState.Disabled.CloudConsentGranted);
        Assert.False(ConsentState.Disabled.IsCloudUsable);
    }

    [Fact]
    public void GrantEnablesCloudOnlyWithBothFlags()
    {
        var granted = ConsentState.Grant("v1", DateTimeOffset.UnixEpoch);
        Assert.True(granted.IsCloudUsable);
        Assert.False(new ConsentState(true, false, "v1", DateTimeOffset.UnixEpoch).IsCloudUsable);
    }

    [Fact]
    public void SecretsComeFromEnvironmentOrOverridesAndAreNeverDefaulted()
    {
        var provider = new EnvironmentSecretProvider(new Dictionary<string, string>
        {
            ["AETHER_TEST_SECRET"] = "synthetic-secret-value",
        });

        Assert.True(provider.HasSecret("AETHER_TEST_SECRET"));
        Assert.Equal("synthetic-secret-value", provider.GetSecret("AETHER_TEST_SECRET"));
        Assert.Null(provider.GetSecret("AETHER_TEST_SECRET_MISSING"));
        Assert.False(provider.HasSecret(" "));
    }
}
