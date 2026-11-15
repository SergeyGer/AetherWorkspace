# Model selection for v0.4 evaluation (cost-optimized defaults)

Decision date: 2026-11-15. The v0.4 model evaluation must measure real providers, but the
**default configuration targets the cheapest capable tiers**, not flagship models. The
evaluation harness will record TTFT, latency, cost and quality per command corpus; defaults
can be overridden in `.env` without code changes.

| Provider | Default model | Role | Published price (per 1M tokens, input/output) | Notes |
|---|---|---|---|---|
| OpenAI | `gpt-4o-mini` | cloud planner/adapter baseline | cheapest widely-available capable tier (verify current list price before runs) | Cheaper than mini/flagship variants for short planning/summarization calls; sufficient for the bounded plan grammar. Override with a newer mini/nano tier only after the evaluation shows a quality gain that justifies cost |
| Google Gemini | `gemini-3.1-flash-lite` | cloud planner/adapter alternative | **$0.25 / $1.50** — see [Gemini API pricing (Nov 2026)](https://www.morphllm.com/gemini-api-pricing) | `gemini-2.5-flash-lite` ($0.10/$0.40) was closed to new API users in November 2026 (live 404 from the provider), so the cheapest available tier was selected after a live availability check; `gemini-3.5-flash-lite` ($0.30/$2.50) is the provider-recommended fallback |
| Local (Ollama-compatible) | `qwen2.5:3b` | offline/private deterministic-adjacent path | free (local hardware) | Small memory footprint (runs on CPU/laptop GPU); `llama3.2:3b` is an equivalent fallback. Quality is measured, not assumed |

## Why these are "optimal by cost"

- Output tokens are the dominant cost driver (3–8× input on Gemini tiers); bounded
  planning/summarization outputs are short, so cheapest-tier models are sufficient starting
  points.
- The project already has a deterministic planner and a local deterministic summary; model
  calls are optional and policy-gated. Expensive models would only be justified by measured
  quality gains in the v0.4 evaluation, and are never used by default.
- Budget controls in the v0.4 harness: max actions, bounded prompts, per-run cost caps,
  eval results stored under `.aether/evaluation` (git-ignored).

## How to configure

```bash
cp .env.example .env          # fill in your own keys/models
# .env is git-ignored; loaded only explicitly:
dotnet run --project src/Aether.Shell.Headless -- ... --env-file .env
```

## Open evaluation questions for v0.4

1. Which model/provider is approved for real data in your organisation (consent policy)?
2. Local hardware limits (VRAM/RAM) to size the local model.
3. Quality threshold the cloud models must beat over the deterministic planner before they
   are enabled by default.

No provider is called by any default path; no API keys are stored in the repository.
