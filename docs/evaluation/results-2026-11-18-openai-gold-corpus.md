# OpenAI gpt-4o-mini — v1.3 gold-corpus evaluation (2026-11-18)

Versioned live-provider evaluation for #88 / AW-060. Synthetic PUBLIC commands only; credentials
were loaded from a git-ignored local `.env` and are not present in this report. This report is a
single-run snapshot, not a benchmark claim across providers or hardware.

## Configuration

| Field | Value |
|---|---|
| Provider / endpoint | OpenAI-compatible chat completions (`cloud-openai-compatible`), api.openai.com |
| Model | `gpt-4o-mini` (configuration value, not hardcoded) |
| Transport | Streaming SSE (`stream=true`) for TTFT; full content assembled from deltas |
| Corpus | `docs/evaluation/command-corpus-v1.3.json`, 120 items (80 canonical-plan + 40 negatives), SHA-256 `e656ac74d043a9e6be092b30350ec3b56a8341cff675f0dfd74498d8cd94340c` |
| Trials | 2 (240 requests; per-item accuracy is stable across both trials) |
| Host | Ubuntu 24.04.5 LTS, x64, 8 logical CPUs, ~15.6 GB available memory, .NET 10.0.15 |
| Temperature | 0 |

## Results (measured)

| Metric | Value |
|---|---|
| Provider failures | 0 / 240 |
| Semantic plan accuracy | **0.9875** (79/80 per trial; one deterministic command consistently returned `error: unsupported`) |
| Denial accuracy | **1.0000** (40/40 adversarial negatives per trial) |
| Syntactic valid-plan rate | 0.6583 (80 plans / 240 requests; includes expected negatives) |
| Latency p50 / p95 / p99 | **1633.0 / 2172.1 / 2723.8 ms** |
| TTFT p50 / p95 / p99 | **531.5 / 822.1 / 1213.4 ms** (240/240 measured from first SSE content delta) |
| Estimated input/output tokens | 112,832 / 16,052 total; per-request p50/p95/p99 = 479/480/481 input and 99/99/99 output (character-based) |
| Estimated cost | **$0.026556** (`gpt-4o-mini` table); actual provider-billed cost: not available in this report |
| Hardware fingerprint | recorded in the JSON report (`hardware` block) |

**Target:** semantic plan accuracy and denial accuracy ≥95% — **met**. The single plan miss and the
estimated (not billed) cost are reported explicitly; no numbers are fabricated.

## Reproduction

```bash
dotnet run -c Release --project tools/Aether.ModelEval -- \
  --provider openai --env-file .env \
  --corpus docs/evaluation/command-corpus-v1.3.json \
  --out .aether/evaluation/openai-$(date +%Y%m%d-%H%M%S).json --trials 2
```

Full sanitized report: [`results-2026-11-18-openai-gpt-4o-mini.json`](results-2026-11-18-openai-gpt-4o-mini.json).

## Limitations

- TTFT is measured only for the OpenAI-compatible streaming transport. Gemini and local adapters are
  non-streaming and are excluded from TTFT; this is an explicit deviation, not an estimate.
- Token/cost figures are character-based estimates, not provider billing. The provider did not
  return usage/billing counters in this harness.
- The corpus is synthetic and bounded; this is not a general quality, compliance or production claim.
