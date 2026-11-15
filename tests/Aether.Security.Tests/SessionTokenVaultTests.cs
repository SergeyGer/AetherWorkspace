using System.Collections.Concurrent;
using Aether.Security.Classification;
using Aether.Security.Vault;
using Aether.Testing;
using Xunit;

namespace Aether.Security.Tests;

public class SessionTokenVaultTests
{
    private static readonly DateTimeOffset Start = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    private static SessionTokenVault CreateVault(
        int capacity = SessionTokenVaultOptions.DefaultCapacity,
        TimeSpan? ttl = null,
        bool evictOldestWhenFull = true,
        TimeProvider? timeProvider = null,
        string sessionId = "test-session") =>
        new(sessionId, new SessionTokenVaultOptions
        {
            Capacity = capacity,
            EntryTimeToLive = ttl,
            EvictOldestWhenFull = evictOldestWhenFull,
        }, timeProvider);

    private static ManualTimeProvider Clock() => new(Start);

    [Fact]
    public void MaskReturnsPlaceholderAndRoundtrips()
    {
        var vault = CreateVault();

        var placeholder = vault.Mask("analyst@example.invalid");

        Assert.Equal("[TOKEN:1]", placeholder);
        Assert.True(SessionTokenVault.IsPlaceholder(placeholder));
        Assert.True(SessionTokenVault.TryParsePlaceholder(placeholder, out var index));
        Assert.Equal(1L, index);
        Assert.Equal("analyst@example.invalid", vault.Unmask(placeholder));
        Assert.True(vault.TryUnmask(placeholder, out var rawToken));
        Assert.Equal("analyst@example.invalid", rawToken);
        Assert.Equal(1, vault.Count);
    }

    [Fact]
    public void SameRawTokenMapsToSamePlaceholderAndDifferentValuesDoNotCollide()
    {
        var vault = CreateVault();

        var first = vault.Mask("ACC-00000001");
        var second = vault.Mask("ACC-00000001");
        var other = vault.Mask("ACC-00000002");

        Assert.Equal(first, second);
        Assert.NotEqual(first, other);
        Assert.Equal(2, vault.Count);
        Assert.Equal("ACC-00000002", vault.Unmask(other));
    }

    [Fact]
    public void MaskContentMasksKnownPatternsAndUnmaskContentRestoresThem()
    {
        var vault = CreateVault();
        const string content =
            "Account ACC-00000001, email analyst@example.invalid, IBAN GB00AETH00000000000001, ISIN XS1234567890.";

        var masked = vault.MaskContent(content);

        Assert.Contains("[TOKEN:", masked, StringComparison.Ordinal);
        Assert.DoesNotContain("ACC-00000001", masked, StringComparison.Ordinal);
        Assert.DoesNotContain("analyst@example.invalid", masked, StringComparison.Ordinal);
        Assert.DoesNotContain("GB00AETH00000000000001", masked, StringComparison.Ordinal);
        Assert.DoesNotContain("XS1234567890", masked, StringComparison.Ordinal);
        Assert.Equal(content, vault.UnmaskContent(masked));

        var categories = vault.GetAuditSnapshot().Entries.Select(entry => entry.Category).ToArray();
        Assert.Contains("synthetic-account-id", categories);
        Assert.Contains("email-like", categories);
        Assert.Contains("iban-like", categories);
        Assert.Contains("isin-like", categories);
    }

    [Fact]
    public void MaskingIsIdempotent()
    {
        var vault = CreateVault();
        const string content = "Call 555-123-4567 and email analyst@example.invalid.";

        var once = vault.MaskContent(content);
        var twice = vault.MaskContent(once);
        var again = vault.MaskContent(content);

        Assert.Equal(once, twice);
        Assert.Equal(once, again);
        Assert.Equal(2, vault.Count);
    }

    [Fact]
    public void MaskContentUsesOptionalCustomPatternList()
    {
        var vault = new SessionTokenVault("custom", new SessionTokenVaultOptions
        {
            MaskContentPatterns = [new SensitivePattern("custom-reference", @"\bZZ-[0-9]{4}\b")],
        });

        var masked = vault.MaskContent("reference ZZ-1234 and email analyst@example.invalid");

        Assert.Equal("reference [TOKEN:1] and email analyst@example.invalid", masked);
        Assert.Equal("custom-reference", Assert.Single(vault.GetAuditSnapshot().Entries).Category);
    }

    [Theory]
    [InlineData("[TOKEN:999]")]
    [InlineData("[TOKEN:0]")]
    [InlineData("token:1")]
    [InlineData("[token:1]")]
    [InlineData("")]
    public void UnknownOrMalformedPlaceholdersCannotBeRehydrated(string placeholder)
    {
        var vault = CreateVault();
        vault.Mask("some-secret-value");

        Assert.False(vault.TryUnmask(placeholder, out var rawToken));
        Assert.Empty(rawToken);
        Assert.Null(vault.Unmask(placeholder));
    }

    [Fact]
    public void ExpiredMappingsCannotBeRehydrated()
    {
        var clock = Clock();
        var vault = CreateVault(ttl: TimeSpan.FromMinutes(1), timeProvider: clock);
        var placeholder = vault.Mask("expiring-secret");

        clock.Advance(TimeSpan.FromSeconds(30));
        Assert.True(vault.TryUnmask(placeholder, out _));

        clock.Advance(TimeSpan.FromSeconds(31));
        Assert.False(vault.TryUnmask(placeholder, out _));
        Assert.Null(vault.Unmask(placeholder));
        Assert.Equal(0, vault.Count);
        Assert.Empty(vault.GetAuditSnapshot().Entries);
    }

    [Fact]
    public void RemoveExpiredReportsAndClearsOnlyExpiredMappings()
    {
        var clock = Clock();
        var vault = CreateVault(ttl: TimeSpan.FromMinutes(1), timeProvider: clock);
        vault.Mask("first");
        vault.Mask("second");
        clock.Advance(TimeSpan.FromSeconds(30));
        var third = vault.Mask("third");

        clock.Advance(TimeSpan.FromSeconds(40));
        var removed = vault.RemoveExpired();

        Assert.Equal(2, removed);
        Assert.Equal(1, vault.Count);
        Assert.Equal("third", vault.Unmask(third));
        Assert.Equal(0, vault.RemoveExpired());
    }

    [Fact]
    public void ExpireAliasClearsExpiredMappings()
    {
        var clock = Clock();
        var vault = CreateVault(ttl: TimeSpan.FromMinutes(1), timeProvider: clock);
        vault.Mask("secret");
        clock.Advance(TimeSpan.FromMinutes(2));

        vault.Expire();

        Assert.Equal(0, vault.Count);
        Assert.Empty(vault.GetAuditSnapshot().Entries);
    }

    [Fact]
    public void CancelClearsMappingsAndFailsClosed()
    {
        var vault = CreateVault();
        var placeholder = vault.Mask("cancelled-secret");

        vault.Cancel();
        vault.Cancel();

        Assert.True(vault.IsCancelled);
        Assert.Equal(0, vault.Count);
        Assert.False(vault.TryUnmask(placeholder, out _));
        Assert.Null(vault.Unmask(placeholder));
        Assert.False(vault.TryMask("cancelled-secret", out _));
        var exception = Assert.Throws<InvalidOperationException>(() => vault.Mask("other-secret"));
        Assert.DoesNotContain("other-secret", exception.ToString(), StringComparison.Ordinal);
        Assert.Contains("cancelled", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void DisposeClearsMappingsAndRejectsFurtherWork()
    {
        var vault = CreateVault();
        var placeholder = vault.Mask("disposed-secret");

        vault.Dispose();
        vault.Dispose();

        Assert.Empty(vault.GetAuditSnapshot().Entries);
        Assert.False(vault.TryUnmask(placeholder, out _));
        Assert.Throws<ObjectDisposedException>(() => vault.Mask("disposed-secret"));
        Assert.Throws<ObjectDisposedException>(() => vault.MaskContent("call 555-123-4567"));
    }

    [Fact]
    public void CapacityEvictsOldestMappingWithFlag()
    {
        var vault = CreateVault(capacity: 2);
        var first = vault.Mask("first-secret");
        var second = vault.Mask("second-secret");

        Assert.True(vault.TryMask("third-secret", category: null, out var third, out var evicted));
        Assert.True(evicted);
        Assert.Equal(2, vault.Count);
        Assert.Equal(1L, vault.EvictedCount);
        Assert.Null(vault.Unmask(first));
        Assert.Equal("second-secret", vault.Unmask(second));
        Assert.Equal("third-secret", vault.Unmask(third));
        Assert.Equal(1L, vault.GetAuditSnapshot().EvictedCount);
    }

    [Fact]
    public void CapacityWithoutEvictionFailsClosedWithoutLeakingRawToken()
    {
        var vault = CreateVault(capacity: 2, evictOldestWhenFull: false);
        vault.Mask("first-secret");
        vault.Mask("second-secret");

        Assert.False(vault.TryMask("third-secret", out _));
        var exception = Assert.Throws<InvalidOperationException>(() => vault.Mask("third-secret"));
        Assert.DoesNotContain("third-secret", exception.ToString(), StringComparison.Ordinal);
        Assert.Contains("capacity", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(2, vault.Count);
    }

    [Fact]
    public void ExpiredMappingsFreeCapacityBeforeEviction()
    {
        var clock = Clock();
        var vault = CreateVault(capacity: 1, ttl: TimeSpan.FromMinutes(1), timeProvider: clock);
        vault.Mask("old-secret");
        clock.Advance(TimeSpan.FromMinutes(2));

        Assert.True(vault.TryMask("new-secret", category: null, out var placeholder, out var evicted));
        Assert.False(evicted);
        Assert.Equal("new-secret", vault.Unmask(placeholder));
        Assert.Equal(0L, vault.EvictedCount);
    }

    [Fact]
    public void SameRawTokenAfterExpiryGetsNewPlaceholder()
    {
        var clock = Clock();
        var vault = CreateVault(ttl: TimeSpan.FromSeconds(5), timeProvider: clock);
        var first = vault.Mask("rotating-secret");
        clock.Advance(TimeSpan.FromSeconds(6));

        var second = vault.Mask("rotating-secret");

        Assert.NotEqual(first, second);
        Assert.False(vault.TryUnmask(first, out _));
        Assert.Equal("rotating-secret", vault.Unmask(second));
    }

    [Fact]
    public void ForeignVaultCannotRehydratePlaceholders()
    {
        var issuing = CreateVault(sessionId: "session-a");
        var foreign = CreateVault(sessionId: "session-b");
        var placeholder = issuing.Mask("session-secret");

        Assert.False(foreign.TryUnmask(placeholder, out _));
        Assert.Equal(placeholder, foreign.UnmaskContent(placeholder, out var unresolved));
        Assert.Equal(new[] { placeholder }, unresolved);
    }

    [Fact]
    public void UnmaskContentLeavesUnknownPlaceholdersAndReportsThem()
    {
        var vault = CreateVault();
        var known = vault.Mask("known-secret");
        var content = $"known {known} unknown [TOKEN:999]";

        var restored = vault.UnmaskContent(content, out var unresolved);

        Assert.Contains("known-secret", restored, StringComparison.Ordinal);
        Assert.Contains("[TOKEN:999]", restored, StringComparison.Ordinal);
        Assert.Equal(new[] { "[TOKEN:999]" }, unresolved);
    }

    [Fact]
    public void MaskContentHandlesNullAndEmptyContent()
    {
        var vault = CreateVault();

        Assert.Equal(string.Empty, vault.MaskContent(null));
        Assert.Equal(string.Empty, vault.MaskContent(string.Empty));
        Assert.Equal(string.Empty, vault.UnmaskContent(null));
        Assert.Equal(string.Empty, vault.UnmaskContent(string.Empty));
    }

    [Fact]
    public void InvalidInputFailsClosed()
    {
        var vault = CreateVault();

        Assert.False(vault.TryMask(string.Empty, out _));
        Assert.False(vault.TryMask("   ", out _));
        Assert.Throws<ArgumentException>(() => vault.Mask(string.Empty));
        Assert.Equal(0, vault.Count);
    }

    [Fact]
    public void ConcurrentMaskingCreatesOneMappingPerRawValue()
    {
        var vault = CreateVault(capacity: 128);
        var results = new ConcurrentBag<(string Raw, string Placeholder)>();

        Parallel.For(0, 400, index =>
        {
            var raw = $"token-{index % 40}";
            results.Add((raw, vault.Mask(raw)));
        });

        Assert.Equal(40, vault.Count);
        for (var i = 0; i < 40; i++)
        {
            var raw = $"token-{i}";
            var placeholders = results
                .Where(result => result.Raw == raw)
                .Select(result => result.Placeholder)
                .Distinct(StringComparer.Ordinal)
                .ToArray();
            var single = Assert.Single(placeholders);
            Assert.Equal(raw, vault.Unmask(single));
        }

        Assert.Equal(40, vault.GetAuditSnapshot().Entries.Count);
    }

    [Fact]
    public void ConcurrentMaskUnmaskRoundTripsWithoutCorruption()
    {
        var vault = CreateVault(capacity: 64);

        Parallel.For(0, 500, index =>
        {
            var raw = $"parallel-{index % 20}";
            var placeholder = vault.Mask(raw);
            Assert.True(vault.TryUnmask(placeholder, out var restored));
            Assert.Equal(raw, restored);
        });

        Assert.Equal(20, vault.Count);
    }

    [Fact]
    public void AuditSnapshotAndToStringNeverContainRawTokens()
    {
        var vault = CreateVault();
        var rawTokens = new[]
        {
            "ACC-00000001",
            "analyst@example.invalid",
            "GB00AETH00000000000001",
            "AETH0000000000000123",
            "XS1234567890",
            "555-123-4567",
        };
        foreach (var rawToken in rawTokens)
        {
            vault.Mask(rawToken, category: "test-category");
        }

        var snapshot = vault.GetAuditSnapshot();

        foreach (var rawToken in rawTokens)
        {
            Assert.DoesNotContain(rawToken, snapshot.ToString(), StringComparison.Ordinal);
            Assert.DoesNotContain(rawToken, vault.ToString(), StringComparison.Ordinal);
            Assert.All(snapshot.Entries, entry => Assert.DoesNotContain(rawToken, entry.ToString(), StringComparison.Ordinal));
        }

        Assert.Equal(rawTokens.Length, snapshot.Count);
        Assert.Equal(6, snapshot.Entries.Count);
        Assert.All(snapshot.Entries, entry => Assert.Equal("test-category", entry.Category));
        Assert.All(snapshot.Entries, entry => Assert.True(SessionTokenVault.IsPlaceholder(entry.Placeholder)));
    }

    [Fact]
    public void SessionIdentifierIsPreservedOrGenerated()
    {
        Assert.Equal("session-x", CreateVault(sessionId: "session-x").SessionId);
        Assert.False(string.IsNullOrWhiteSpace(new SessionTokenVault().SessionId));
        Assert.NotEqual(new SessionTokenVault().SessionId, new SessionTokenVault().SessionId);
    }

    [Fact]
    public void OptionsAreValidated()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new SessionTokenVault(options: new SessionTokenVaultOptions { Capacity = 0 }));
        Assert.Throws<ArgumentOutOfRangeException>(() => new SessionTokenVault(options: new SessionTokenVaultOptions { EntryTimeToLive = TimeSpan.Zero }));
        Assert.Throws<ArgumentOutOfRangeException>(() => new SessionTokenVault(options: new SessionTokenVaultOptions { RegexTimeout = TimeSpan.Zero }));
        Assert.Throws<ArgumentNullException>(() => new SessionTokenVault(options: new SessionTokenVaultOptions { MaskContentPatterns = null! }));
        Assert.Equal(7, CreateVault(capacity: 7).Capacity);
    }

    [Fact]
    public void NoTimeToLiveDisablesExpiry()
    {
        var clock = Clock();
        var vault = CreateVault(ttl: null, timeProvider: clock);
        var placeholder = vault.Mask("permanent-until-cancel");
        clock.Advance(TimeSpan.FromDays(365));

        Assert.Equal("permanent-until-cancel", vault.Unmask(placeholder));
        Assert.Equal(0, vault.RemoveExpired());
    }
}
