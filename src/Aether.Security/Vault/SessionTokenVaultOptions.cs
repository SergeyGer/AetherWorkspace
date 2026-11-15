using Aether.Security.Classification;

namespace Aether.Security.Vault;

/// <summary>
/// Configuration for <see cref="SessionTokenVault"/>. Defaults are deliberately
/// small and volatile: the vault is a session-scoped reversible token map, not a
/// persistent store and not an anonymity mechanism.
/// </summary>
public sealed class SessionTokenVaultOptions
{
    /// <summary>Default maximum number of live token mappings.</summary>
    public const int DefaultCapacity = 128;

    /// <summary>Shared default options.</summary>
    public static SessionTokenVaultOptions Default { get; } = new();

    /// <summary>Maximum number of live mappings retained by the vault.</summary>
    public int Capacity { get; init; } = DefaultCapacity;

    /// <summary>
    /// Lifetime of a mapping. Expired mappings cannot be unmasked. A null value
    /// disables time-based expiry (for example in short-lived test scenarios).
    /// </summary>
    public TimeSpan? EntryTimeToLive { get; init; } = TimeSpan.FromMinutes(30);

    /// <summary>
    /// When true the oldest live mapping is evicted to make room for a new one;
    /// when false, masking fails closed once the vault is at capacity.
    /// </summary>
    public bool EvictOldestWhenFull { get; init; } = true;

    /// <summary>Regex timeout used by content masking to keep work bounded.</summary>
    public TimeSpan RegexTimeout { get; init; } = TimeSpan.FromMilliseconds(250);

    /// <summary>
    /// Patterns replaced by <see cref="SessionTokenVault.MaskContent(string)"/>;
    /// defaults to the built-in synthetic PII/financial detector patterns.
    /// Callers may supply the trusted policy patterns instead.
    /// </summary>
    public IReadOnlyList<SensitivePattern> MaskContentPatterns { get; init; } =
        SensitiveEntityDetectorOptions.DefaultPatterns();
}
