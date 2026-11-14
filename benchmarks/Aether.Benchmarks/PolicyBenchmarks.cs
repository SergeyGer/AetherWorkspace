using Aether.Contracts;
using Aether.Security.Policy;
using BenchmarkDotNet.Attributes;

namespace Aether.Benchmarks;

[MemoryDiagnoser]
[ShortRunJob]
public class PolicyBenchmarks
{
    private static readonly PolicyRequest CloudPublic = new(
        DataClassification.Public, AiProviderKind.Cloud, AiTaskKind.PlanGeneration, true, true, false);

    private static readonly PolicyRequest CloudRestricted = new(
        DataClassification.Restricted, AiProviderKind.Cloud, AiTaskKind.Summarization, true, true, false);

    [Benchmark]
    public bool CloudPublicAllowed() => BenchmarkFixtures.PolicyEngine.Evaluate(CloudPublic).Allowed;

    [Benchmark]
    public bool CloudRestrictedDenied() => !BenchmarkFixtures.PolicyEngine.Evaluate(CloudRestricted).Allowed;
}
