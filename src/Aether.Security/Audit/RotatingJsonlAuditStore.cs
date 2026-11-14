namespace Aether.Security.Audit;

/// <summary>
/// Size-bounded JSONL audit store with bounded retention. When the current file would
/// exceed the configured size, it rotates to <c>path.1</c>, shifting older files up to
/// <c>path.N</c> and deleting files beyond the retention budget. The hash chain is not
/// reset by rotation: sequence numbers and previous-hash links continue across files, so
/// a verifier can read the rotated series in order and validate one continuous chain.
/// </summary>
public sealed class RotatingJsonlAuditStore : IAuditEventStore, IAuditEventReader
{
    private readonly object _gate = new();
    private readonly string _path;
    private readonly long _maxFileBytes;
    private readonly int _maxFiles;

    public RotatingJsonlAuditStore(string path, long maxFileBytes = 1_048_576, int maxFiles = 4)
    {
        if (maxFileBytes < 4096)
        {
            throw new ArgumentOutOfRangeException(nameof(maxFileBytes), "Audit rotation requires at least 4096 bytes per file.");
        }

        if (maxFiles is < 1 or > 100)
        {
            throw new ArgumentOutOfRangeException(nameof(maxFiles), "Audit retention must be between 1 and 100 files.");
        }

        _path = Path.GetFullPath(path);
        _maxFileBytes = maxFileBytes;
        _maxFiles = maxFiles;
    }

    public string FilePath => _path;

    public long MaxFileBytes => _maxFileBytes;

    public int MaxFiles => _maxFiles;

    public void Append(AuditEvent auditEvent)
    {
        ArgumentNullException.ThrowIfNull(auditEvent);

        var directory = Path.GetDirectoryName(_path);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var line = Aether.Contracts.Json.AetherJson.Serialize(auditEvent);
        var lineBytes = System.Text.Encoding.UTF8.GetByteCount(line) + System.Environment.NewLine.Length;
        lock (_gate)
        {
            if (File.Exists(_path) && new FileInfo(_path).Length + lineBytes > _maxFileBytes)
            {
                RotateLocked();
            }

            File.AppendAllText(_path, line + Environment.NewLine);
        }
    }

    private void RotateLocked()
    {
        for (var index = _maxFiles - 1; index >= 1; index--)
        {
            var source = StoragePath(index);
            if (!File.Exists(source))
            {
                continue;
            }

            var destination = StoragePath(index + 1);
            if (File.Exists(destination))
            {
                File.Delete(destination);
            }

            File.Move(source, destination);
        }

        if (File.Exists(_path))
        {
            var first = StoragePath(1);
            if (File.Exists(first))
            {
                File.Delete(first);
            }

            File.Move(_path, first);
        }
    }

    private string StoragePath(int index) => index <= 0 ? _path : _path + "." + index;

    /// <summary>
    /// Reads the rotated series in chronological order (oldest retained file first).
    /// </summary>
    public IReadOnlyList<AuditEvent> ReadAllRotated()
    {
        var events = new List<AuditEvent>();
        lock (_gate)
        {
            for (var index = _maxFiles; index >= 1; index--)
            {
                ReadInto(StoragePath(index), events);
            }

            ReadInto(_path, events);
        }

        return events;
    }

    public AuditVerificationResult Verify()
    {
        var events = ReadAllRotated();
        if (events.Count == 0)
        {
            return new AuditVerificationResult(true, 0, null, null);
        }

        return AuditChainVerifier.Verify(events, hmacKey: null, expectedStartSequence: events[0].Sequence);
    }

    private static void ReadInto(string path, List<AuditEvent> events)
    {
        if (!File.Exists(path))
        {
            return;
        }

        foreach (var line in File.ReadLines(path))
        {
            if (!string.IsNullOrWhiteSpace(line))
            {
                events.Add(Aether.Contracts.Json.AetherJson.DeserializeStrict<AuditEvent>(line));
            }
        }
    }

    /// <summary>Verifies a file plus its rotated siblings (<c>.1</c>…<c>.N</c>) as one chain.</summary>
    public static AuditVerificationResult VerifyFamily(string path, int maxFiles = 4)
    {
        var store = new RotatingJsonlAuditStore(path, maxFileBytes: 4096, maxFiles: maxFiles);
        return store.Verify();
    }
}
