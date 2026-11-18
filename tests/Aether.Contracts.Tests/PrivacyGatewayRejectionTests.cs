using Aether.Security.Policy;
using Aether.Testing;
using Xunit;

namespace Aether.Contracts.Tests;

/// <summary>
/// v1.4 ADR-011 architecture guard: rejected sanitized INTERNAL routing must not be wired into
/// provider paths, and the PUBLIC-only default must stay in the committed policy.
/// </summary>
public sealed class PrivacyGatewayRejectionTests
{
    [Fact]
    public void DefaultPolicyKeepsCloudPublicOnlyAndSanitizedInternalDisabled()
    {
        var policy = PolicyDocument.CreateDefault().EnsureValid();
        Assert.False(policy.Cloud.AllowSanitizedInternal);
        Assert.Equal(PolicyWireNames.OptIn, policy.DataClasses["PUBLIC"].Cloud);
        Assert.Equal(PolicyWireNames.DenyByDefault, policy.DataClasses["INTERNAL"].Cloud);
        Assert.Equal(PolicyWireNames.Deny, policy.DataClasses["RESTRICTED"].Cloud);
        Assert.Equal(PolicyWireNames.Deny, policy.DataClasses["UNKNOWN"].Cloud);
        Assert.False(PolicyEngine.SanitizedInternalCloudRoutingEnabled);
    }

    [Fact]
    public void SessionTokenVaultIsNotWiredIntoTheAiProviderPath()
    {
        var aiRoot = TestRepo.PathTo("src", "Aether.AI");
        var offenders = Directory.EnumerateFiles(aiRoot, "*.cs", SearchOption.AllDirectories)
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                           && !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .Where(path => File.ReadAllText(path).Contains("SessionTokenVault", StringComparison.Ordinal))
            .Select(path => Path.GetRelativePath(TestRepo.Root, path))
            .ToArray();

        Assert.Empty(offenders);
    }
}
