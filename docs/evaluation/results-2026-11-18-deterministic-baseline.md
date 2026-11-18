# Deterministic evaluation-harness baseline — 2026-11-18

This is a **harness/corpus calibration report**, not a model-quality claim. It runs the committed
labelled v1.3 gold corpus through the deterministic planner on the CI/Linux host to verify that the
harness, canonical plans and denial scoring work end to end. Actual OpenAI/Gemini/local model results
must be produced by the operator with the commands below and attached separately; no model numbers
are inferred from this baseline.

## Command

```bash
dotnet run -c Release --project tools/Aether.ModelEval -- \
  --provider deterministic \
  --corpus docs/evaluation/command-corpus-v1.3.json \
  --out .aether/evaluation/deterministic-baseline.json \
  --trials 1
```

## Corpus and host

| Field | Value |
|---|---|
| Corpus | `docs/evaluation/command-corpus-v1.3.json` (schema 1.1) |
| Corpus SHA-256 | `e656ac74d043a9e6be092b30350ec3b56a8341cff675f0dfd74498d8cd94340c` |
| Items | 120 total: 80 canonical-plan, 40 adversarial negative |
| Provider | `deterministic-grammar` (no model call) |
| Host | Ubuntu 24.04.5 LTS, x64, 8 logical CPUs, ~15.6 GB available memory, .NET 10.0.15 |

## Results (harness baseline only)

| Metric | Value |
|---|---|
| Requests | 120 |
| Provider failures | 0 |
| Semantic plan accuracy | 1.0000 (80/80 plan expectations) |
| Denial accuracy | 1.0000 (40/40 negative expectations) |
| Syntactic valid-plan rate | 0.6667 (80 valid plans; 40 expected non-plans are counted) |
| Latency p50 | 0.2 ms |
| Latency p95 / p99 | 29.2 ms / 29.2 ms (one cold/JIT outlier) |
| Tokens/cost | 0 (deterministic planner) |

## Explicit limitations

- **Not a model evaluation:** no OpenAI, Gemini or local model was called. This proves only the
  corpus/harness/semantic comparator.
- **TTFT is unavailable:** the current non-streaming adapters cannot measure time-to-first-token.
  The report emits `ttftMs: null` with a limitation; a streaming transport is required before
  claiming TTFT. This is a reported deviation, not a fabricated number.
- Character-based token/cost estimates are labelled estimates; provider billing is not available.
- The determinism of this baseline does not imply that an LLM meets the ≥95% target. A live run with
  the operator's provider credentials and hardware fingerprint is required for #88 closure.
