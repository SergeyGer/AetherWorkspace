using Aether.Contracts.Json;
using BenchmarkDotNet.Attributes;

namespace Aether.Benchmarks;

[MemoryDiagnoser]
[ShortRunJob]
public class SerializationBenchmarks
{
    private static readonly string Json = BenchmarkFixtures.EnvelopeJson;

    [Benchmark]
    public int ParseEnvelopeShape()
    {
        using var document = System.Text.Json.JsonDocument.Parse(Json);
        return document.RootElement.GetProperty("payload").GetProperty("Type").GetString()!.Length;
    }

    [Benchmark]
    public string SerializePlan() => AetherJson.Serialize(BenchmarkFixtures.SamplePlan);
}
