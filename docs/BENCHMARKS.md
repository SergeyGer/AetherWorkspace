# Benchmarks and measured baselines

This document records **raw measured baselines** from the harnesses committed in
`benchmarks/`. Values are engineering measurements on one machine, not universal
performance claims. The reference target in the specification (NFR-01) applies to
a pinned Windows reference machine at the v0.2 gate; the numbers below are the
Linux/headless development baseline used while Windows hardware is unavailable.

## Reproduce

```bash
# Microbenchmarks (BenchmarkDotNet, short runs for smoke evidence)
dotnet run -c Release --project benchmarks/Aether.Benchmarks -- --filter "*"

# End-to-end IPC latency (starts a real broker process and three sample processes)
dotnet run -c Release --project benchmarks/Aether.IpcBench -- --samples 1000 --warmup 100 --payload-bytes 1024
```

Raw results are written to `BenchmarkDotNet.Artifacts/` (git-ignored) and printed
to stdout respectively. `scripts/run-benchmarks.sh` wraps both commands and
stores results under `.aether/benchmarks/`.

## Measured environment

| Property | Value |
|---|---|
| Date | 2026-11-14 |
| OS | Ubuntu 24.04.5 LTS (Linux), x64 |
| CPU | Intel Xeon (Skylake/IBRS) 2.29 GHz, 8 logical/physical cores |
| Runtime | .NET 10.0.15, SDK 10.0.404, Release build |
| Transport | Unix domain socket (headless development/CI substitute) |
| Participants | 1 broker process + 1 shell process + 3 sample-app processes |
| Payload | ~1 KB typed `fdc3.instrument` context (`synthetic.instrument.bench`) |
| Subscribers | 3 |

The Windows reference profile (Windows 11 x64, named pipes, WPF samples) must be
measured separately before any Windows performance statement is made.

## End-to-end IPC latency (measured)

1,000 publish round-trips after 100 warmup publishes. Each publish is a
`PublishContext` RPC that validates classification/capabilities, resolves the
trusted catalog payload, routes to all three subscriber queues and returns
delivery accounting. Latency is a `Stopwatch` around the full RPC.

| Metric | p50 | p95 | p99 | max |
|---|---|---|---|---|
| 1 KB context publish round-trip, 3 subscribers | 1.184 ms | 3.037 ms | 5.593 ms | 18.465 ms |
| Health probe round-trip | 0.498 ms | 1.275 ms | 2.306 ms | 11.813 ms |

Interpretation, scoped to this environment:

- The NFR-01 p95 target (≤25 ms for a local 1 KB payload with 3 apps after warmup)
  is met by the measured p95 of 3.037 ms on this Linux profile. This is supporting
  evidence for the v0.2 gate, not a substitute for the pinned Windows measurement.
- The distribution has a warm-up tail (first ~50 samples include JIT/runtime
  effects) and occasional GC pauses (max 18.465 ms). Report percentiles, not just
  the mean.
- No retries or duplicate deliveries were counted separately in this run; retry
  and duplicate behaviour is covered by `ChannelRouterTests` and the deduplication
  design note in ADR/architecture docs.

## Microbenchmarks (measured, ShortRun: 3 iterations, 3 warmup)

| Benchmark | Mean | Allocated | Notes |
|---|---|---|---|
| `PlanValidationBenchmarks.ValidateSamplePlan` | 2.345 µs | 512 B | Validate a 5-step plan against the trusted catalog |
| `PolicyBenchmarks.CloudPublicAllowed` | 54.93 ns | 48 B | Policy evaluation, PUBLIC cloud with consent |
| `PolicyBenchmarks.CloudRestrictedDenied` | 59.09 ns | 48 B | Policy evaluation, RESTRICTED cloud denial |
| `SerializationBenchmarks.SerializePlan` | 3.619 µs | 1.69 KB | Strict camelCase JSON serialization of a plan |
| `SerializationBenchmarks.ParseEnvelopeShape` | 2.302 µs | – | Parse a ~1 KB envelope JSON projection |
| `AuditBenchmarks.AppendAndVerify100` | 668.6 µs | 229 KB | Append 100 metadata-only events + verify hash chain |

These are short-run smoke baselines. For release evidence, run with a longer
BenchmarkDotNet job, pin the machine and record variance as required by the plan.

## What is deliberately not claimed

- No Windows reference-machine benchmark has been executed in this environment.
- No model-inference latency, TTFT, tokens/sec, VRAM or cost claims are made;
  the optional cloud/local model adapters are disabled by default and only
  exercised with test doubles. Model evaluation is deferred to the v0.4 backlog
  (AW-027/AW-028).
- No CPU/memory claim for WPF rendering (WPF shell is compile-verified only here).
- Percentiles from one run are indicative; treat differences smaller than the
  observed variance as noise.
