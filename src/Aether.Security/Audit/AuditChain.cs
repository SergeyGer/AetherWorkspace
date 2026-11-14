using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Aether.Contracts.Json;

namespace Aether.Security.Audit;

/// <summary>
/// Tamper-evident, hash-chained local audit log. Each entry commits to the
/// previous entry's hash, so a modified historical entry is detected by the
/// verifier. "Tamper-evident" does not mean immutable against an administrator
/// or a compromised local system.
/// </summary>
public sealed class HashChainAuditLog : IAuditSink
{
    private readonly object _gate = new();
    private readonly List<AuditEvent> _events = [];
    private readonly byte[]? _hmacKey;
    private readonly IAuditEventStore? _store;
    private readonly TimeProvider _timeProvider;

    /// <param name="continueExistingChain">
    /// When true and the store is readable, existing persisted events are loaded and
    /// verified, and the new log continues the same hash chain (sequence + previous hash).
    /// This keeps a stable file across process restarts. An invalid existing chain is
    /// rejected (fail-closed) instead of silently starting a new one.
    /// </param>
    public HashChainAuditLog(
        byte[]? hmacKey = null,
        IAuditEventStore? store = null,
        TimeProvider? timeProvider = null,
        bool continueExistingChain = false)
    {
        _hmacKey = hmacKey;
        _store = store;
        _timeProvider = timeProvider ?? TimeProvider.System;

        if (continueExistingChain && store is IAuditEventReader reader)
        {
            var existing = reader.ReadAllRotated();
            if (existing.Count > 0)
            {
                var verification = AuditChainVerifier.Verify(existing, hmacKey, existing[0].Sequence);
                if (!verification.Valid)
                {
                    throw new Aether.Contracts.Errors.AetherContractException(
                        Aether.Contracts.Errors.AetherErrorCodes.AuditChainInvalid,
                        $"Existing audit chain failed verification: {verification.Reason}");
                }

                _events.AddRange(existing);
            }
        }
    }

    public int Count
    {
        get
        {
            lock (_gate)
            {
                return _events.Count;
            }
        }
    }

    public void Append(AuditEventDraft draft)
    {
        ArgumentNullException.ThrowIfNull(draft);

        lock (_gate)
        {
            var previousHash = _events.Count == 0 ? "" : _events[^1].Hash;
            var sequence = _events.Count + 1;
            var candidate = new AuditEvent
            {
                Sequence = sequence,
                TimestampUtc = _timeProvider.GetUtcNow(),
                TraceId = draft.TraceId,
                EventType = draft.EventType,
                Outcome = draft.Outcome,
                Code = draft.Code,
                PolicyVersion = draft.PolicyVersion,
                Classification = draft.Classification,
                ProviderCategory = draft.ProviderCategory,
                RedactionCount = draft.RedactionCount,
                PayloadBytes = draft.PayloadBytes,
                DurationMs = draft.DurationMs,
                BrokeredAppId = draft.BrokeredAppId,
                PreviousHash = previousHash,
            };

            var auditEvent = candidate with { Hash = AuditChainHasher.ComputeHash(candidate, _hmacKey) };
            _events.Add(auditEvent);
            _store?.Append(auditEvent);
        }
    }

    public IReadOnlyList<AuditEvent> Snapshot()
    {
        lock (_gate)
        {
            return _events.ToArray();
        }
    }

    public AuditVerificationResult Verify()
    {
        lock (_gate)
        {
            return AuditChainVerifier.Verify(
                _events,
                _hmacKey,
                _events.Count > 0 ? _events[0].Sequence : 1);
        }
    }
}

public sealed record AuditVerificationResult(bool Valid, int EntryCount, long? FirstInvalidSequence, string? Reason);

/// <summary>Serialises audit events to a local JSONL file (metadata only).</summary>
public sealed class JsonlAuditStore : IAuditEventStore, IAuditEventReader
{
    private readonly object _gate = new();
    private readonly string _path;

    public JsonlAuditStore(string path) => _path = Path.GetFullPath(path);

    public string FilePath => _path;

    public IReadOnlyList<AuditEvent> ReadAllRotated() => ReadAll(_path);

    public void Append(AuditEvent auditEvent)
    {
        var directory = System.IO.Path.GetDirectoryName(_path);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var line = AetherJson.Serialize(auditEvent);
        lock (_gate)
        {
            File.AppendAllText(_path, line + Environment.NewLine);
        }
    }

    public static IReadOnlyList<AuditEvent> ReadAll(string path)
    {
        var events = new List<AuditEvent>();
        if (!File.Exists(path))
        {
            return events;
        }

        foreach (var line in File.ReadLines(path))
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            events.Add(AetherJson.DeserializeStrict<AuditEvent>(line));
        }

        return events;
    }
}

public static class AuditChainVerifier
{
    /// <summary>
    /// Verifies a (possibly retained-prefix) audit chain. When <paramref name="expectedStartSequence"/>
    /// is greater than 1, the oldest retained entry is accepted with an unverifiable
    /// predecessor (rotation removed it); every subsequent link and hash is still checked.
    /// </summary>
    public static AuditVerificationResult Verify(
        IReadOnlyList<AuditEvent> entries,
        byte[]? hmacKey = null,
        long expectedStartSequence = 1)
    {
        ArgumentNullException.ThrowIfNull(entries);

        string? previousHash = expectedStartSequence > 1 ? null : "";
        var expectedSequence = expectedStartSequence;

        foreach (var entry in entries)
        {
            if (entry.Sequence != expectedSequence)
            {
                return new AuditVerificationResult(false, entries.Count, entry.Sequence, "Audit sequence gap detected.");
            }

            if (previousHash is not null && !string.Equals(entry.PreviousHash, previousHash, StringComparison.Ordinal))
            {
                return new AuditVerificationResult(false, entries.Count, entry.Sequence, "Audit previous-hash link mismatch.");
            }

            var expectedHash = AuditChainHasher.ComputeHash(entry, hmacKey);
            if (!string.Equals(entry.Hash, expectedHash, StringComparison.Ordinal))
            {
                return new AuditVerificationResult(false, entries.Count, entry.Sequence, "Audit entry hash mismatch (entry was modified).");
            }

            previousHash = entry.Hash;
            expectedSequence++;
        }

        return new AuditVerificationResult(true, entries.Count, null, null);
    }

    public static AuditVerificationResult VerifyFile(string path, byte[]? hmacKey = null) =>
        Verify(JsonlAuditStore.ReadAll(path), hmacKey);
}

internal static class AuditChainHasher
{
    public static string ComputeHash(AuditEvent value, byte[]? hmacKey)
    {
        var canonical = string.Join(
            '\u001f',
            value.Sequence.ToString(CultureInfo.InvariantCulture),
            value.TimestampUtc.ToUnixTimeMilliseconds().ToString(CultureInfo.InvariantCulture),
            value.TraceId,
            value.EventType,
            value.Outcome,
            value.Code ?? "",
            value.PolicyVersion ?? "",
            value.Classification ?? "",
            value.ProviderCategory ?? "",
            value.RedactionCount.ToString(CultureInfo.InvariantCulture),
            value.PayloadBytes.ToString(CultureInfo.InvariantCulture),
            value.DurationMs?.ToString(CultureInfo.InvariantCulture) ?? "",
            value.BrokeredAppId ?? "",
            value.PreviousHash);

        var bytes = Encoding.UTF8.GetBytes(canonical);
        var hash = hmacKey is { Length: > 0 }
            ? HMACSHA256.HashData(hmacKey, bytes)
            : SHA256.HashData(bytes);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}
