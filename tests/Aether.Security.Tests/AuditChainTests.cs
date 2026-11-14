using Aether.Security.Audit;
using Aether.Testing;
using Xunit;

namespace Aether.Security.Tests;

public class AuditChainTests
{
    private static AuditEventDraft Draft(string traceId, string eventType = AuditEventTypes.ExecutionStep, string outcome = AuditOutcomes.Succeeded) => new()
    {
        TraceId = traceId,
        EventType = eventType,
        Outcome = outcome,
        Code = null,
        Classification = "RESTRICTED",
        RedactionCount = 2,
        PayloadBytes = 512,
        DurationMs = 3,
        BrokeredAppId = "sample.watchlist",
    };

    [Fact]
    public void ChainVerifiesForUntamperedEntries()
    {
        var log = new HashChainAuditLog();
        log.Append(Draft("trace-1"));
        log.Append(Draft("trace-2"));
        log.Append(Draft("trace-3"));

        var verification = log.Verify();
        Assert.True(verification.Valid);
        Assert.Equal(3, verification.EntryCount);
        Assert.Equal([1L, 2L, 3L], log.Snapshot().Select(entry => entry.Sequence));
        Assert.Equal(log.Snapshot()[0].Hash, log.Snapshot()[1].PreviousHash);
    }

    [Fact]
    public void PersistedChainVerifiesFromDisk()
    {
        using var directory = new TempDirectory("audit");
        var path = directory.PathTo("audit.jsonl");
        var log = new HashChainAuditLog(store: new JsonlAuditStore(path));
        for (var index = 0; index < 5; index++)
        {
            log.Append(Draft($"trace-{index}"));
        }

        var verification = AuditChainVerifier.VerifyFile(path);
        Assert.True(verification.Valid);
        Assert.Equal(5, verification.EntryCount);
    }

    [Fact]
    public void ModifiedHistoricalEntryIsDetected()
    {
        using var directory = new TempDirectory("audit-tamper");
        var path = directory.PathTo("audit.jsonl");
        var log = new HashChainAuditLog(store: new JsonlAuditStore(path));
        for (var index = 0; index < 5; index++)
        {
            log.Append(Draft($"trace-{index}"));
        }

        var lines = File.ReadAllLines(path);
        lines[2] = lines[2].Replace("\"outcome\":\"succeeded\"", "\"outcome\":\"denied\"");
        File.WriteAllLines(path, lines);

        var verification = AuditChainVerifier.VerifyFile(path);
        Assert.False(verification.Valid);
        Assert.Equal(3, verification.FirstInvalidSequence);
    }

    [Fact]
    public void DeletedEntryIsDetectedAsSequenceGap()
    {
        using var directory = new TempDirectory("audit-gap");
        var path = directory.PathTo("audit.jsonl");
        var log = new HashChainAuditLog(store: new JsonlAuditStore(path));
        for (var index = 0; index < 4; index++)
        {
            log.Append(Draft($"trace-{index}"));
        }

        var lines = File.ReadAllLines(path).ToList();
        lines.RemoveAt(1);
        File.WriteAllLines(path, lines);

        var verification = AuditChainVerifier.VerifyFile(path);
        Assert.False(verification.Valid);
        Assert.NotNull(verification.FirstInvalidSequence);
    }

    [Fact]
    public void HmacChainRejectsVerificationWithAnotherKey()
    {
        var key = "aether-test-audit-key"u8.ToArray();
        var log = new HashChainAuditLog(hmacKey: key);
        log.Append(Draft("trace-1"));
        log.Append(Draft("trace-2"));

        Assert.True(log.Verify().Valid);
        Assert.False(AuditChainVerifier.Verify(log.Snapshot(), "different-key"u8.ToArray()).Valid);
    }

    [Fact]
    public void AuditEventsContainMetadataOnlyNoRawPayloads()
    {
        var log = new HashChainAuditLog();
        log.Append(new AuditEventDraft
        {
            TraceId = "trace-privacy",
            EventType = AuditEventTypes.ContextPublish,
            Outcome = AuditOutcomes.Succeeded,
            Classification = "RESTRICTED",
            RedactionCount = 3,
            PayloadBytes = 1024,
        });

        var serialized = string.Join("\n", log.Snapshot().Select(entry => Aether.Contracts.Json.AetherJson.Serialize(entry)));
        Assert.DoesNotContain("ACC-00000001", serialized);
        Assert.DoesNotContain("Northstar Capital", serialized);
        Assert.Contains("\"redactionCount\":3", serialized);
    }
}
