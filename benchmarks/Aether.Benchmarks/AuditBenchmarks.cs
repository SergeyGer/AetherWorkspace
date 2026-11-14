using Aether.Security.Audit;
using BenchmarkDotNet.Attributes;

namespace Aether.Benchmarks;

[MemoryDiagnoser]
[ShortRunJob]
public class AuditBenchmarks
{
    [Benchmark]
    public int AppendAndVerify100()
    {
        var log = new HashChainAuditLog();
        for (var index = 0; index < 100; index++)
        {
            log.Append(new AuditEventDraft
            {
                TraceId = TraceId20(index),
                EventType = AuditEventTypes.ExecutionStep,
                Outcome = AuditOutcomes.Succeeded,
                DurationMs = index,
            });
        }

        return log.Verify().EntryCount;
    }

    private static string TraceId20(int index) => (index.ToString("D32", System.Globalization.CultureInfo.InvariantCulture))[..32];
}
