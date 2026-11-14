using Aether.Execution.Validation;
using BenchmarkDotNet.Attributes;

namespace Aether.Benchmarks;

[MemoryDiagnoser]
[ShortRunJob]
public class PlanValidationBenchmarks
{
    [Benchmark]
    public bool ValidateSamplePlan() =>
        BenchmarkFixtures.Validator.Validate(BenchmarkFixtures.SamplePlan, BenchmarkFixtures.Catalog).IsValid;
}
