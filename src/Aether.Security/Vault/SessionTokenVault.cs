using System.Globalization;
using System.Text.RegularExpressions;
using Aether.Security.Classification;

namespace Aether.Security.Vault;

/// <summary>One audit-safe token mapping: placeholder and coarse category only.</summary>
public sealed record SessionTokenAuditEntry(string Placeholder, string? Category);

/// <summary>
/// Audit-safe snapshot of a <see cref="SessionTokenVault"/>. It contains only
/// placeholders, categories and counts and therefore may be written to audit
/// records; raw token values are never exposed.
/// </summary>
public sealed record SessionTokenAuditSnapshot(
    int Count,
    long EvictedCount,
    IReadOnlyList<SessionTokenAuditEntry> Entries)
{
    public override string ToString() =>
        $"SessionTokenAuditSnapshot {{ Count = {Count}, EvictedCount = {EvictedCount} }}";
}

/// <summary>
/// Session-scoped, reversible, in-memory token vault used for explicitly
/// authorized sanitization. Raw values are replaced by stable placeholders of
/// the form <c>[TOKEN:N]</c> and can be rehydrated only by the same live vault
/// instance while the mapping exists. Mappings are volatile, capacity-bounded
/// and time-bounded; <see cref="Cancel"/> and <see cref="Dispose"/> clear all
/// mappings.
/// </summary>
/// <remarks>
/// This type provides redaction/rehydration for controlled flows. It is not an
/// anonymity mechanism and must never be treated as one. All members are
/// thread-safe; raw values never appear in exceptions, audit snapshots or
/// <see cref="ToString"/> output.
/// </remarks>
public sealed class SessionTokenVault : IDisposable
{
    /// <summary>Prefix of every placeholder.</summary>
    public const string PlaceholderPrefix = "[TOKEN:";

    /// <summary>Suffix of every placeholder.</summary>
    public const string PlaceholderSuffix = "]";

    private readonly object _gate = new();
    private readonly Dictionary<string, Entry> _byPlaceholder = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _byRawToken = new(StringComparer.Ordinal);
    private readonly SessionTokenVaultOptions _options;
    private readonly TimeProvider _timeProvider;
    private readonly IReadOnlyList<(SensitivePattern Pattern, Regex Regex)> _maskRules;
    private readonly Regex _placeholderScan;
    private long _nextIndex;
    private long _evictedCount;
    private bool _cancelled;
    private bool _disposed;

    /// <summary>
    /// Creates a vault. The optional session identifier scopes the mapping and
    /// is used only for diagnostics; a random opaque identifier is generated when
    /// omitted.
    /// </summary>
    public SessionTokenVault(
        string? sessionId = null,
        SessionTokenVaultOptions? options = null,
        TimeProvider? timeProvider = null)
    {
        _options = options ?? SessionTokenVaultOptions.Default;
        ValidateOptions(_options);
        _timeProvider = timeProvider ?? TimeProvider.System;
        SessionId = string.IsNullOrWhiteSpace(sessionId) ? Guid.NewGuid().ToString("N") : sessionId.Trim();
        _maskRules = _options.MaskContentPatterns
            .Select(pattern => (
                pattern,
                new Regex(
                    pattern.Pattern,
                    RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.NonBacktracking,
                    _options.RegexTimeout)))
            .ToArray();
        _placeholderScan = new Regex(
            @"\[TOKEN:[0-9]{1,19}\]",
            RegexOptions.CultureInvariant | RegexOptions.NonBacktracking,
            _options.RegexTimeout);
    }

    /// <summary>Opaque session identifier for this vault instance.</summary>
    public string SessionId { get; }

    /// <summary>Maximum number of live mappings retained by this vault.</summary>
    public int Capacity => _options.Capacity;

    /// <summary>Number of live (non-expired) mappings; expired mappings are swept first.</summary>
    public int Count
    {
        get
        {
            lock (_gate)
            {
                if (_cancelled || _disposed)
                {
                    return 0;
                }

                RemoveExpiredLocked(_timeProvider.GetUtcNow());
                return _byPlaceholder.Count;
            }
        }
    }

    /// <summary>Total number of mappings evicted because the vault was full.</summary>
    public long EvictedCount
    {
        get
        {
            lock (_gate)
            {
                return _evictedCount;
            }
        }
    }

    /// <summary>True after <see cref="Cancel"/> or <see cref="Dispose"/>; the vault rejects all further work.</summary>
    public bool IsCancelled
    {
        get
        {
            lock (_gate)
            {
                return _cancelled || _disposed;
            }
        }
    }

    /// <summary>Builds the canonical placeholder for a 1-based mapping index.</summary>
    public static string FormatPlaceholder(long index)
    {
        if (index < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(index), index, "Placeholder indices start at 1.");
        }

        return $"{PlaceholderPrefix}{index.ToString(CultureInfo.InvariantCulture)}{PlaceholderSuffix}";
    }

    /// <summary>True when the value is a well-formed placeholder such as <c>[TOKEN:12]</c>.</summary>
    public static bool TryParsePlaceholder(string? value, out long index)
    {
        index = 0;
        if (string.IsNullOrEmpty(value)
            || value.Length <= PlaceholderPrefix.Length + PlaceholderSuffix.Length
            || !value.StartsWith(PlaceholderPrefix, StringComparison.Ordinal)
            || !value.EndsWith(PlaceholderSuffix, StringComparison.Ordinal))
        {
            return false;
        }

        var digits = value.AsSpan(
            PlaceholderPrefix.Length,
            value.Length - PlaceholderPrefix.Length - PlaceholderSuffix.Length);
        return long.TryParse(digits, NumberStyles.None, CultureInfo.InvariantCulture, out index) && index >= 1;
    }

    /// <summary>True when the value is a well-formed placeholder such as <c>[TOKEN:12]</c>.</summary>
    public static bool IsPlaceholder(string? value) => TryParsePlaceholder(value, out _);

    /// <summary>
    /// Masks one raw token, returning its stable placeholder. Masking the same
    /// value again returns the same placeholder while the mapping is live.
    /// </summary>
    public string Mask(string rawToken) => Mask(rawToken, category: null);

    /// <summary>
    /// Masks one raw token, tagging the mapping with a coarse category (for
    /// example a detector category) that may appear in audit snapshots.
    /// </summary>
    public string Mask(string rawToken, string? category)
    {
        var status = TryMaskCore(rawToken, category, out var placeholder, out _);
        return status switch
        {
            MaskStatus.Success => placeholder,
            MaskStatus.InvalidInput => throw new ArgumentException("A non-empty raw token is required.", nameof(rawToken)),
            MaskStatus.Disposed => throw new ObjectDisposedException(nameof(SessionTokenVault)),
            MaskStatus.Cancelled => throw new InvalidOperationException("The session token vault has been cancelled."),
            _ => throw new InvalidOperationException("The session token vault is at capacity and eviction is disabled."),
        };
    }

    /// <summary>Attempts to mask one raw token; returns false for invalid input or an unavailable vault.</summary>
    public bool TryMask(string rawToken, out string placeholder) =>
        TryMask(rawToken, category: null, out placeholder, out _);

    /// <summary>
    /// Attempts to mask one raw token. <paramref name="evicted"/> reports whether
    /// an older mapping had to be evicted to make room for this one.
    /// </summary>
    public bool TryMask(string rawToken, string? category, out string placeholder, out bool evicted) =>
        TryMaskCore(rawToken, category, out placeholder, out evicted) == MaskStatus.Success;

    /// <summary>
    /// Rehydrates a placeholder issued by this live vault. Returns null for
    /// unknown, expired, foreign or unavailable placeholders; never throws for
    /// attacker-controlled input.
    /// </summary>
    public string? Unmask(string placeholder) =>
        TryUnmask(placeholder, out var rawToken) ? rawToken : null;

    /// <summary>Attempts to rehydrate a placeholder issued by this live vault.</summary>
    public bool TryUnmask(string placeholder, out string rawToken)
    {
        rawToken = string.Empty;
        if (string.IsNullOrEmpty(placeholder))
        {
            return false;
        }

        lock (_gate)
        {
            if (_cancelled || _disposed)
            {
                return false;
            }

            if (!_byPlaceholder.TryGetValue(placeholder, out var entry))
            {
                return false;
            }

            if (entry.ExpiresAt is { } expiresAt && _timeProvider.GetUtcNow() >= expiresAt)
            {
                RemoveLocked(entry);
                return false;
            }

            rawToken = entry.RawToken;
            return true;
        }
    }

    /// <summary>
    /// Masks every occurrence of the configured sensitive patterns in content
    /// (bounded regular expressions, no network). Returns an empty string for
    /// null/empty content. Masking already-masked content is idempotent.
    /// </summary>
    public string MaskContent(string? content)
    {
        if (string.IsNullOrEmpty(content))
        {
            return content ?? string.Empty;
        }

        var masked = content;
        try
        {
            foreach (var (pattern, regex) in _maskRules)
            {
                masked = regex.Replace(masked, match =>
                    match.Length == 0 ? match.Value : Mask(match.Value, pattern.Category));
            }
        }
        catch (RegexMatchTimeoutException)
        {
            // Fail closed: never return partially masked content silently.
            throw new InvalidOperationException("Content masking failed closed because a masking pattern timed out.");
        }

        return masked;
    }

    /// <summary>
    /// Rehydrates placeholders issued by this live vault and leaves unknown or
    /// expired placeholders untouched (they are never substituted).
    /// </summary>
    public string UnmaskContent(string? content) => UnmaskContent(content, out _);

    /// <summary>
    /// Rehydrates placeholders issued by this live vault and reports placeholders
    /// that could not be rehydrated (unknown, expired or foreign). Those values
    /// remain in the returned text as-is and are never replaced.
    /// </summary>
    public string UnmaskContent(string? content, out IReadOnlyList<string> unresolvedPlaceholders)
    {
        if (string.IsNullOrEmpty(content))
        {
            unresolvedPlaceholders = [];
            return content ?? string.Empty;
        }

        var unresolved = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        try
        {
            var unmasked = _placeholderScan.Replace(content, match =>
            {
                var placeholder = match.Value;
                if (TryUnmask(placeholder, out var rawToken))
                {
                    return rawToken;
                }

                if (seen.Add(placeholder))
                {
                    unresolved.Add(placeholder);
                }

                return placeholder;
            });

            unresolvedPlaceholders = unresolved;
            return unmasked;
        }
        catch (RegexMatchTimeoutException)
        {
            unresolvedPlaceholders = [];
            throw new InvalidOperationException("Content unmasking failed closed because the placeholder scan timed out.");
        }
    }

    /// <summary>Removes time-expired mappings and returns how many were removed.</summary>
    public int RemoveExpired()
    {
        lock (_gate)
        {
            return _cancelled || _disposed ? 0 : RemoveExpiredLocked(_timeProvider.GetUtcNow());
        }
    }

    /// <summary>Removes time-expired mappings.</summary>
    public void Expire() => RemoveExpired();

    /// <summary>Clears every mapping and rejects all further operations; idempotent.</summary>
    public void Cancel()
    {
        lock (_gate)
        {
            if (_cancelled)
            {
                return;
            }

            _cancelled = true;
            ClearLocked();
        }
    }

    /// <summary>Clears every mapping and releases the vault; idempotent.</summary>
    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _cancelled = true;
            ClearLocked();
        }
    }

    /// <summary>
    /// Returns an audit-safe snapshot containing placeholders, categories and
    /// counts only. Raw token values are never included.
    /// </summary>
    public SessionTokenAuditSnapshot GetAuditSnapshot()
    {
        lock (_gate)
        {
            if (!_cancelled && !_disposed)
            {
                RemoveExpiredLocked(_timeProvider.GetUtcNow());
            }

            var entries = _byPlaceholder.Values
                .OrderBy(entry => entry.Sequence)
                .Select(entry => new SessionTokenAuditEntry(entry.Placeholder, entry.Category))
                .ToArray();
            return new SessionTokenAuditSnapshot(entries.Length, _evictedCount, entries);
        }
    }

    /// <inheritdoc />
    public override string ToString() =>
        $"SessionTokenVault {{ SessionId = {SessionId}, Count = {Count} }}";

    private static void ValidateOptions(SessionTokenVaultOptions options)
    {
        if (options.Capacity < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(options), options.Capacity, "Capacity must be at least 1.");
        }

        if (options.EntryTimeToLive is { } ttl && ttl <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(options), ttl, "EntryTimeToLive must be positive when set.");
        }

        if (options.RegexTimeout <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(options), options.RegexTimeout, "RegexTimeout must be positive.");
        }

        if (options.MaskContentPatterns is null)
        {
            throw new ArgumentNullException(nameof(options), "MaskContentPatterns is required.");
        }
    }

    private MaskStatus TryMaskCore(string? rawToken, string? category, out string placeholder, out bool evicted)
    {
        placeholder = string.Empty;
        evicted = false;
        if (string.IsNullOrWhiteSpace(rawToken))
        {
            return MaskStatus.InvalidInput;
        }

        var normalizedCategory = string.IsNullOrWhiteSpace(category) ? null : category.Trim();
        lock (_gate)
        {
            if (_disposed)
            {
                return MaskStatus.Disposed;
            }

            if (_cancelled)
            {
                return MaskStatus.Cancelled;
            }

            var now = _timeProvider.GetUtcNow();
            RemoveExpiredLocked(now);
            if (_byRawToken.TryGetValue(rawToken, out var existing))
            {
                placeholder = existing;
                return MaskStatus.Success;
            }

            if (_byPlaceholder.Count >= _options.Capacity)
            {
                if (!_options.EvictOldestWhenFull)
                {
                    return MaskStatus.CapacityExceeded;
                }

                EvictOldestLocked();
                evicted = true;
            }

            var index = ++_nextIndex;
            placeholder = FormatPlaceholder(index);
            var expiresAt = _options.EntryTimeToLive is { } ttl ? now + ttl : (DateTimeOffset?)null;
            var entry = new Entry(index, placeholder, rawToken, normalizedCategory, expiresAt);
            _byPlaceholder.Add(placeholder, entry);
            _byRawToken.Add(rawToken, placeholder);
            return MaskStatus.Success;
        }
    }

    private int RemoveExpiredLocked(DateTimeOffset now)
    {
        if (_byPlaceholder.Count == 0)
        {
            return 0;
        }

        List<Entry>? expired = null;
        foreach (var entry in _byPlaceholder.Values)
        {
            if (entry.ExpiresAt is { } expiresAt && now >= expiresAt)
            {
                (expired ??= []).Add(entry);
            }
        }

        if (expired is null)
        {
            return 0;
        }

        foreach (var entry in expired)
        {
            RemoveLocked(entry);
        }

        return expired.Count;
    }

    private void EvictOldestLocked()
    {
        Entry? oldest = null;
        foreach (var entry in _byPlaceholder.Values)
        {
            if (oldest is null || entry.Sequence < oldest.Sequence)
            {
                oldest = entry;
            }
        }

        if (oldest is not null)
        {
            RemoveLocked(oldest);
            _evictedCount++;
        }
    }

    private void RemoveLocked(Entry entry)
    {
        _byPlaceholder.Remove(entry.Placeholder);
        _byRawToken.Remove(entry.RawToken);
    }

    private void ClearLocked()
    {
        _byPlaceholder.Clear();
        _byRawToken.Clear();
    }

    private enum MaskStatus
    {
        Success,
        InvalidInput,
        CapacityExceeded,
        Cancelled,
        Disposed,
    }

    private sealed class Entry
    {
        public Entry(long sequence, string placeholder, string rawToken, string? category, DateTimeOffset? expiresAt)
        {
            Sequence = sequence;
            Placeholder = placeholder;
            RawToken = rawToken;
            Category = category;
            ExpiresAt = expiresAt;
        }

        public long Sequence { get; }

        public string Placeholder { get; }

        public string RawToken { get; }

        public string? Category { get; }

        public DateTimeOffset? ExpiresAt { get; }
    }
}
