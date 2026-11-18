using Aether.Security.Secrets;
using Xunit;

namespace Aether.Security.Tests;

/// <summary>v1.3 AW-067: credential-source selection, no secret leakage in errors, CM guarded by OS.</summary>
public sealed class SecretProviderFactoryTests
{
    [Fact]
    public void DefaultIsExplicitEnvironmentInjection()
    {
        var provider = SecretProviderFactory.Create(new Dictionary<string, string>
        {
            ["OPENAI_API_KEY"] = "synthetic-environment-value",
        });

        Assert.Equal("synthetic-environment-value", provider.GetSecret("OPENAI_API_KEY"));
        Assert.True(provider.HasSecret("OPENAI_API_KEY"));
        Assert.False(provider.HasSecret("MISSING_KEY"));
    }

    [Fact]
    public void UnsupportedStoreFailsClosedWithoutEchoingValues()
    {
        var error = Assert.Throws<ArgumentException>(() => SecretProviderFactory.Create(new Dictionary<string, string>
        {
            [SecretProviderFactory.CredentialStoreEnvironmentVariable] = "plain-text-file",
            ["OPENAI_API_KEY"] = "should-not-appear",
        }));

        Assert.Contains("Unsupported", error.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("should-not-appear", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void WindowsCredentialManagerStoreRequiresWindows()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        Assert.Throws<PlatformNotSupportedException>(() => SecretProviderFactory.Create(new Dictionary<string, string>
        {
            [SecretProviderFactory.CredentialStoreEnvironmentVariable] = SecretProviderFactory.WindowsCredentialManagerStore,
        }));
    }

    [Fact]
    public void EnvironmentStoreCanBeConfiguredExplicitly()
    {
        var provider = SecretProviderFactory.Create(new Dictionary<string, string>
        {
            [SecretProviderFactory.CredentialStoreEnvironmentVariable] = SecretProviderFactory.EnvironmentStore,
            ["GEMINI_API_KEY"] = "synthetic-gemini-value",
        });

        Assert.IsType<EnvironmentSecretProvider>(provider);
        Assert.Equal("synthetic-gemini-value", provider.GetSecret("GEMINI_API_KEY"));
    }
}
