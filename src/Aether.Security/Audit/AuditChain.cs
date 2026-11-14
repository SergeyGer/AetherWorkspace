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

    public HashChainAuditLog(byte[]? hmacKey = null, IAuditEventStore? store = null, TimeProvider? timeProvider = null)
    {
        _hmacKey = hmacKey;
        _store = store;
        _timeProvider = timeProvider ?? TimeProvider.System;
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
            return AuditChainVerifier.Verify(_events, _hmacKey);
        }
    }
}

public sealed record AuditVerificationResult(bool Valid, int EntryCount, long? FirstInvalidSequence, string? Reason);

/// <summary>Serialises audit events to a local JSONL file (metadata only).</summary>
public sealed class JsonlAuditStore : IAuditEventStore
{
    private readonly object _gate = new();
    private readonly string _path;

    public JsonlAuditStore(string path) => _path = Path.GetFullPath(path);

    public string FilePath => _path;

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
    public static AuditVerificationResult Verify(IReadOnlyList<AuditEvent> entries, byte[]? hmacKey = null)
    {
        ArgumentNullException.ThrowIfNull(entries);

        var previousHash = "";
        var expectedSequence = 1L;

        foreach (var entry in entries)
        {
            if (entry.Sequence != expectedSequence)
            {
                return new AuditVerificationResult(false, entries.Count, entry.Sequence, "Audit sequence gap detected.");
            }

            if (!string.Equals(entry.PreviousHash, previousHash, StringComparison.Ordinal))
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
