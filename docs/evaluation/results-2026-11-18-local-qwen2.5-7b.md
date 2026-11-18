# Local model measurement — qwen2.5:7b on a fresh Linux host (2026-11-18)

Physical/fresh-machine local E2E for #95 / AW-067. The model was pulled from the Ollama registry
(Apache-2.0 profile) and served locally; the measurement harness recorded real process working sets
and latency. This is **not** a Windows run and not a quality benchmark; the full 120-item corpus was
not run on CPU (scope is reported below).

## Host and profile

| Field | Value |
|---|---|
| Host OS | Ubuntu 24.04.5 LTS, x64, 8 logical CPUs, 15.2 GB RAM |
| CPU | Intel Xeon (Skylake), CPU-only inference (no GPU offload; Ollama `size_vram=0`) |
| Runtime | Ollama 0.40.16, `qwen2.5:7b` Q4_K_M |
| Model layer | `sha256:2bada8a7450677000f678be90653b85d364de7db25eb5ea54136ada5f3933730` (4.68 GB) |
| Licence | Apache-2.0 (license layer `sha256:832dd9e0…e92e`) |
| Endpoint | loopback `http://127.0.0.1:11434/v1/chat/completions` |
| Measurement scope | first **4** canonical-plan corpus items, 1 trial (CPU full-corpus run would take ~45+ min) |

## Measured results

| Metric | Value |
|---|---|
| Requests / provider failures | 4 / 0 |
| Semantic plan accuracy (limited scope) | **1.0000** (4/4) |
| Denial accuracy | not measured (no negative items in the limited slice) |
| Latency p50 / p95 / p99 | **23,538.6 / 23,728.1 / 23,728.1 ms** |
| TTFT | **unavailable** — local adapter is non-streaming; explicit deviation, no estimate |
| Ollama server RSS | 43.2 MB |
| Model-runner (`llama-server`) RSS | **4,875.1 MB** |
| Total local-inference working set | **4,918.3 MB** |
| Ollama `/api/ps` loaded model size | 4,835.0 MB |
| Estimated input/output tokens | 1,913 / 396 (character-based) |
| Estimated cost | $0 (local hardware; no API billing) |

## Reproduction

```bash
# Ollama must already serve the supported model on loopback
dotnet run -c Release --project tools/Aether.ModelEval -- \
  --provider local --corpus docs/evaluation/command-corpus-v1.3.json \
  --out .aether/evaluation/local-model-eval.json --trials 1 --limit 4
```

Or the operator script: `pwsh -File tools/model-eval/Measure-LocalModelProfile.ps1 -Trials 1 -MaxItems 4`.

## Interpretation and limits

- The local route is functional end to end (broker-independent planning prompt → local HTTP model →
  strict plan parsing) and uses ~4.9 GB RSS on this host.
- **No Windows hardware run was performed.** The issue's required evidence allows the local E2E
  report + docs (Windows run optional); this report explicitly records the Linux host and must not be
  presented as a Windows measurement.
- TTFT is not available for the non-streaming local adapter; it is reported as unavailable rather
  than estimated. A streaming transport/Gemini/local TTFT extension remains future work.
- The limited scope (4 plan items) proves the profile/measurement path; it is not a full-corpus
  quality claim. The 120-item evaluation remains the OpenAI run / deterministic baseline.
- Raw model weights, provider credentials and Ollama caches are local-only and not committed.

Sanitized JSON: `local-model-measurement.json` and `local-model-eval.json` in this directory.
