using Aether.Security.Audit;
using Aether.Testing;
using Xunit;

namespace Aether.Security.Tests;

public class AuditRotationTests
{
    private static AuditEventDraft Draft(string traceId) => new()
    {
        TraceId = traceId,
        EventType = AuditEventTypes.ExecutionStep,
        Outcome = AuditOutcomes.Succeeded,
        Classification = "RESTRICTED",
        PayloadBytes = 128,
        DurationMs = 1,
        BrokeredAppId = "sample.watchlist",
    };

    private static (HashChainAuditLog Log, RotatingJsonlAuditStore Store, string Path, TempDirectory Directory) Create(int maxFiles = 4)
    {
        var directory = new TempDirectory("audit-rotate");
        var path = directory.PathTo("audit.jsonl");
        var store = new RotatingJsonlAuditStore(path, maxFileBytes: 4096, maxFiles: maxFiles);
        return (new HashChainAuditLog(store: store), store, path, directory);
    }

    [Fact]
    public void ChainRemainsVerifiableAcrossRotatedFiles()
    {
        var (log, store, path, directory) = Create();
        using var _ = directory;
        for (var index = 0; index < 120; index++)
        {
            log.Append(Draft($"trace-{index:D3}"));
        }

        Assert.True(File.Exists(path));
        Assert.True(File.Exists(path + ".1"), "expected at least one rotated audit file");
        var verification = store.Verify();
        Assert.True(verification.Valid, verification.Reason);

        var all = store.ReadAllRotated();
        Assert.Equal(all.Count, verification.EntryCount);
        Assert.Equal(120L, all[^1].Sequence);
        for (var index = 1; index < all.Count; index++)
        {
            Assert.Equal(all[index - 1].Sequence + 1, all[index].Sequence);
        }
    }

    [Fact]
    public void RetentionBoundsTheNumberOfFiles()
    {
        var (log, store, path, directory) = Create(maxFiles: 2);
        using var _ = directory;
        for (var index = 0; index < 400; index++)
        {
            log.Append(Draft($"trace-{index:D3}"));
        }

        var files = Directory.GetFiles(directory.FullPath, "audit.jsonl*");
        Assert.InRange(files.Length, 1, 3); // current + at most 2 retained rotations
        Assert.True(store.Verify().Valid);
        var all = store.ReadAllRotated();
        Assert.NotEmpty(all);
        Assert.True(all[0].Sequence > 1, "retention should have discarded the oldest entries");
        for (var index = 1; index < all.Count; index++)
        {
            Assert.Equal(all[index - 1].Sequence + 1, all[index].Sequence);
        }
    }

    [Fact]
    public void TamperedRotatedFileIsDetected()
    {
        var (log, store, path, directory) = Create();
        using var _ = directory;
        for (var index = 0; index < 120; index++)
        {
            log.Append(Draft($"trace-{index:D3}"));
        }

        var rotatedPath = path + ".1";
        Assert.True(File.Exists(rotatedPath));
        var lines = File.ReadAllLines(rotatedPath);
        lines[0] = lines[0].Replace("\"outcome\":\"succeeded\"", "\"outcome\":\"denied\"");
        File.WriteAllLines(rotatedPath, lines);

        var verification = store.Verify();
        Assert.False(verification.Valid);
        Assert.NotNull(verification.FirstInvalidSequence);
    }

    [Fact]
    public void DeletedRotatedFileIsDetectedAsSequenceGap()
    {
        var (log, store, path, directory) = Create();
        using var _ = directory;
        for (var index = 0; index < 240; index++)
        {
            log.Append(Draft($"trace-{index:D3}"));
        }

        // Create enough rotation history that .1 sits between a retained older file and
        // the current file; deleting it must be observable as a sequence gap.
        for (var extra = 0; extra < 240 && !File.Exists(path + ".2"); extra++)
        {
            log.Append(Draft($"extra-{extra:D3}"));
        }

        Assert.True(File.Exists(path + ".1"));
        File.Delete(path + ".1");

        var verification = store.Verify();
        Assert.False(verification.Valid);
        Assert.NotNull(verification.FirstInvalidSequence);
    }

    [Fact]
    public void InvalidRotationOptionsAreRejected()
    {
        using var directory = new TempDirectory("audit-invalid");
        var path = directory.PathTo("audit.jsonl");
        Assert.Throws<ArgumentOutOfRangeException>(() => new RotatingJsonlAuditStore(path, maxFileBytes: 1024));
        Assert.Throws<ArgumentOutOfRangeException>(() => new RotatingJsonlAuditStore(path, maxFiles: 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new RotatingJsonlAuditStore(path, maxFiles: 101));
    }
}
