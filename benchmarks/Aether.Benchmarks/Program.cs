using BenchmarkDotNet.Running;

namespace Aether.Benchmarks;

/// <summary>
/// Reproducible microbenchmarks for validation, policy evaluation, serialization
/// and audit chaining. Run: dotnet run -c Release --project benchmarks/Aether.Benchmarks
/// </summary>
public static class Program
{
    public static void Main(string[] args) =>
        BenchmarkSwitcher.FromAssembly(typeof(Program).Assembly).Run(args);
}
