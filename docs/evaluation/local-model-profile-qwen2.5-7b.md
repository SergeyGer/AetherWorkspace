# Supported local model profile — Qwen2.5 7B Instruct (Ollama)

Status: **supported v1.3 profile definition** (2026-11-18). The model weights are **not bundled**;
the operator pulls them from the Ollama registry. Local inference was not exercised on the CI host
(no model runtime); the reproducible physical measurement is an operator step and is not claimed
until the measured report is attached.

## Pinned profile

| Field | Value |
|---|---|
| Model | `qwen2.5:7b` (Qwen2.5-7B-Instruct instruction-tuned) |
| Runtime | Ollama (`ollama serve`), OpenAI-compatible endpoint `http://127.0.0.1:11434/v1/chat/completions` |
| Model layer digest | `sha256:2bada8a7450677000f678be90653b85d364de7db25eb5ea54136ada5f3933730` |
| Model layer size | 4,683,073,952 bytes (≈4.68 GB) |
| Config layer digest | `sha256:161ddde4c9cd07c9f1ccb4e0167c434bce72caeb3fc1844262fa66bc877b0426` |
| Ollama system/template layer digests | `sha256:66b9ea09bd5b7099cbb4fc820f31b575c0366fa439b08245566692c6784e281e` / `sha256:eb4402837c7829a690fa845de4d7f3fd842c2adee476d5341da8a46ea9255175` |
| Licence | **Apache License 2.0** (license layer `sha256:832dd9e00a68dd83b3c3fb9f5588dad7dcf337a0db50f7d9483f310cd292e92e`) |
| Model family/source | Qwen2.5-7B-Instruct, Alibaba Cloud; see the model card/licence for redistribution terms |

The digest pins the exact Ollama layer; `ollama pull qwen2.5:7b` resolves the tag to this layer at the
time of writing. Verify the digest with `ollama show qwen2.5:7b --modelfile` / registry manifest before
an evaluation run, because registry tags can move.

## Hardware and memory expectations

- 4.68 GB weights on disk; reserve **≥8 GB RAM free** for CPU inference plus the rest of the
  workspace. A GPU with ≥6–8 GB VRAM can accelerate it, but GPU is optional.
- CPU-only inference is supported for the demo but is slower; measure it rather than assuming a
  latency SLA. No performance numbers are claimed until the physical report is attached.
- First load includes model initialization; evaluation must warm up once before measuring.

## Install and verify

```powershell
ollama pull qwen2.5:7b
ollama show qwen2.5:7b
# optional: confirm the layer digest against the pinned manifest above
```

Then configure the headless demo (no cloud, no API key):

```powershell
$env:AETHER_PLANNER_PROVIDER = 'local'
$env:AETHER_SUMMARY_PROVIDER = 'local'
$env:AETHER_LOCAL_MODEL_ENDPOINT = 'http://127.0.0.1:11434/v1/chat/completions'
$env:AETHER_LOCAL_MODEL = 'qwen2.5:7b'
dotnet run -c Release --project src/Aether.Shell.Headless -- `
  --endpoint-file .aether\endpoint.json --classification INTERNAL `
  --planner local --use-local-model --command 'Open Northstar bond analytics, show the associated risk view, synchronize this instrument across the workspace and summarize the exposure privately.'
```

The local endpoint is validated as **loopback-only** by `ModelProviderSettings.Validate`; a
non-loopback URL is rejected as a configuration error and never silently sent.

## Measurement requirement (operator)

Run the v1.3 evaluation harness with `--provider local` and attach the sanitized report with:
model/layer digest, hardware fingerprint, warmup, repeated trials, p50/p95/p99 latency, tokens and
peak working-set memory. Do not claim latency or memory values before that report exists.
