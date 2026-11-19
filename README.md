# AetherWorkspace

[![CI](https://github.com/SergeyGer/AetherWorkspace/actions/workflows/ci.yml/badge.svg)](https://github.com/SergeyGer/AetherWorkspace/actions/workflows/ci.yml)
[![coverage](docs/assets/coverage-badge.svg)](docs/testing/coverage.md)
[![tests](https://img.shields.io/badge/tests-590%20passing-brightgreen)](https://github.com/SergeyGer/AetherWorkspace/actions/workflows/ci.yml)
![.NET 10](https://img.shields.io/badge/.NET-10.0-512BD4?logo=dotnet&logoColor=white)
![C%23 14](https://img.shields.io/badge/C%23-14-239120)
![WPF](https://img.shields.io/badge/WPF-Windows%2011-0C54C2)
![gRPC](https://img.shields.io/badge/gRPC-named%20pipes-244C5A?logo=grpc&logoColor=white)
![FDC3](https://img.shields.io/badge/FDC3-declared%20subset-1F4E79)
[![License: MIT](https://img.shields.io/badge/license-MIT-3DA639)](LICENSE)

AetherWorkspace is an independent, vendor-neutral **.NET 10 reference implementation** for
safe, local-first AI desktop automation. It demonstrates how an LLM can coordinate several
desktop applications **without giving the model direct system access**: the planner only
proposes a typed plan, a deterministic policy engine decides what may run, and execution happens
through an authenticated local broker. All fixtures are synthetic.

**Propose → govern → execute:**
1. A policy-aware **AI planner** proposes a strict, versioned `ActionPlan` (Track B), or the
   deterministic grammar produces one (Track A).
2. A **deterministic executor** validates the schema, allowlists and capabilities, then checks
   consent, budgets and data-egress boundaries before any side effect.
3. A **local broker** drives independent desktop apps over authenticated local IPC (gRPC over
   Windows named pipes; Unix sockets on Linux dev/CI) with typed FDC3-aligned context.

> **Scope:** this is not production software, a compliance certification, a FDC3 conformance
> claim or a PII guarantee. Detailed status, evidence and technical reference live in the
> **[Wiki](https://github.com/SergeyGer/AetherWorkspace/wiki)**; boundaries are in
> [What this is — and is not](#what-this-is--and-is-not).

## See it working

[![AetherWorkspace Track B demo preview](docs/assets/demo-preview.gif)](https://youtu.be/CFJJZX-QcnU)

Track B (primary, **narrated English video**): an LLM proposes a plan under explicit PUBLIC consent;
the policy-governed executor validates and runs it; typed context synchronizes three independent
Windows apps over authenticated local IPC. The deterministic Track A path remains available with
`usedModel=false`.

▶ **[Watch the narrated demo on YouTube](https://youtu.be/CFJJZX-QcnU)** ·
[MP4 in this repository](docs/assets/demo-hero.mp4) ·
[Capture evidence](docs/testing/evidence/2026-11-18-v1.1-track-b-demo-gate.md) ·
[Narrated-video evidence](docs/testing/evidence/2026-11-18-v1.1-narrated-demo.md).

## Architecture at a glance

**Planner → ActionPlan → deterministic policy/executor → local broker → independent desktop
apps**, with a hash-chained audit and deny-by-default egress.

<details>
<summary><b>Show trust-boundary diagram</b></summary>

```mermaid
%%{init: {'themeVariables': {'fontSize': '20px'}}}%%
flowchart TD
  Request["User request"] --> Planner["Planner<br/>AI or deterministic"]
  Planner -->|"ActionPlan JSON"| Executor["Deterministic Executor"]
  Executor --> Checks{"Policy + capability checks"}
  Checks -->|"authorized"| Broker["Local Broker"]
  Broker --> Apps["Independent desktop apps"]
  Executor -. "allow / deny evidence" .-> Audit[("Hash-chained audit")]
  Checks -. "PUBLIC + consent only" .-> Egress["Egress Gateway"]
  Egress -. "only if allowed" .-> Cloud["Cloud provider"]
```

The model never receives direct OS/desktop access. The deterministic control plane is authoritative;
the broker sees only authorized steps and typed context. Full component map:
[Wiki: Architecture](https://github.com/SergeyGer/AetherWorkspace/wiki/Architecture).

</details>

<details>
<summary><b>Example: a real ActionPlan shape and a denial (click)</b></summary>

A valid proposal uses the repository contract (`schemaVersion`, `planId`, flat typed `steps`):

```json
{
  "schemaVersion": "1.0",
  "planId": "example-workflow-001",
  "description": "Synthetic, non-executable illustration only.",
  "steps": [
    { "action": "LaunchApp", "appId": "sample.watchlist" },
    { "action": "LaunchApp", "appId": "sample.risk" },
    { "action": "PublishContext", "contextRef": "synthetic.instrument.northstar" },
    { "action": "ArrangeWindows", "layout": "side-by-side" },
    { "action": "SummarizeContext", "contextRef": "synthetic.exposure.northstar", "mode": "private" }
  ]
}
```

A malicious/invalid proposal cannot express an executable path, shell command or arbitrary URL in
this schema. An unknown step such as:

```json
{
  "schemaVersion": "1.0",
  "planId": "unsafe-example",
  "steps": [{ "action": "ExecShell", "statusText": "curl https://example.invalid" }]
}
```

is denied by the deterministic validator with `plan.action.unknown` **before any side effect**.
Separately, a cloud planning/summary attempt for `RESTRICTED`/`UNKNOWN` data is denied by the
egress policy (`policy.cloud.denied`) before the provider transport is invoked. These are the real
contract/error shapes from this repository — not illustrative fields from an external design sketch.

</details>

## Start here

Choose the path that matches the time you have.

| I want to… | Time | What you get | Start |
|---|---|---|---|
| Understand the product | 5 min | Purpose, key concepts, safety boundaries | [Wiki: Home](https://github.com/SergeyGer/AetherWorkspace/wiki) |
| Run the Windows desktop app without building | 5 min | Unsigned portable WPF bundle + one-command launcher; verify checksums first | [Latest release](https://github.com/SergeyGer/AetherWorkspace/releases/latest) |
| Run the reference workflow | 10 min | Real trace, plan, denials and private summary | [Quick start](docs/quickstart.md) |
| See status, evidence and roadmap | 5 min | Milestones v0.1–v1.5, CI and desktop evidence | [Wiki: Project status](https://github.com/SergeyGer/AetherWorkspace/wiki/Project-Status) |
| Explore architecture and decisions | 20 min | Component map, invariants, ADRs | [Wiki: Architecture](https://github.com/SergeyGer/AetherWorkspace/wiki/Architecture) |
| Run the Windows 11 test strategy | 30 min | R1 lifecycle, deterministic counters, evidence rules | [Wiki: Testing strategy](https://github.com/SergeyGer/AetherWorkspace/wiki/Testing-Strategy-Windows11) |
| Extend contracts safely | 1 h | Contract studio, schemas, review/validate workflow | [Wiki: Development guide](https://github.com/SergeyGer/AetherWorkspace/wiki/Development-Guide) |
| Verify a release | 5 min | SHA-256 checksums + SPDX SBOM verification | [Reproducibility](docs/distribution/reproducibility.md) |
| Know limitations and residual risks | 3 min | Boundaries, safety notes, non-claims | [Wiki: Known limitations](https://github.com/SergeyGer/AetherWorkspace/wiki/Known-Limitations) |
| Read the end-to-end demo | 5 min | Narrated workflow with expected outputs | [Wiki: Narrated demo](https://github.com/SergeyGer/AetherWorkspace/wiki/Narrated-Demo) |

## What you get

- **Governed automation, not autonomous execution.** AI output is an untrusted proposal:
  strict plans, allowlisted actions, capability checks, bounded budgets and hash-chained,
  metadata-only audit before any side effect — no executable paths or arbitrary URLs.
- **Verifiable evidence.** Deterministic Linux/Windows CI, a headless reference workflow,
  Windows named-pipe and WPF UI lanes, plus a physical Windows 11 reference-desktop run
  recorded in the Wiki. If a claim is not backed by a test or measurement, it is not made.
- **A downloadable Windows desktop bundle.** Each release ships a Windows x64 ZIP with the WPF
  shell, three WPF sample apps, the independent WebView2 chart host, broker and a one-command
  launcher — no source checkout required (unsigned portable; verify the published SHA-256 and SBOM).
- **A runnable desktop reference.** Three independent synthetic WPF apps (Watchlist, Risk,
  Chart), a separate WebView2 chart process with a strict typed-context bridge, durable
  multi-monitor layouts, a Live Inspector with sanitized diagnostics export, and a review-only
  contract studio.
- **A measured hybrid-AI path.** Provider-agnostic planning/summarization configuration, an
  Apache-2.0 local-model profile, and a 120-command evaluation harness with reported plan/denial
  accuracy, latency and TTFT; cloud remains PUBLIC-only with explicit consent.

## Who is this for?

- **AI / agent engineers** who need a working blueprint for constraining LLM planners with
  deterministic validation, capability authorization and bounded execution.
- **Desktop / FinTech architects** exploring local gRPC named-pipe IPC, typed context propagation
  and a heterogeneous WPF + WebView2 desktop surface.
- **AI safety / security researchers** studying propose-and-govern boundaries, hash-chained
  metadata-only audit, fail-closed egress and explicit accepted limitations.
- **QA / automation engineers** interested in deterministic multi-window Windows 11 test lanes,
  TRX evidence and reproducible physical-release gates.

## Quick start

Requires the [.NET 10 SDK](https://dotnet.microsoft.com/download). Windows 11 is needed for
the WPF shell and window placement; Linux supports the headless host and the full portable
test suite.

```bash
./scripts/demo.sh      # build, run the reference workflow, witness the audit/egress proof
./scripts/verify.sh    # full deterministic verification suite
```

On Windows use `pwsh ./scripts/demo.ps1` to exercise the named-pipe transport. The demo starts
a broker, launches three independent synthetic apps, publishes a typed instrument context,
arranges their top-level windows, and produces a private local summary — with observable
denials, zero external calls for restricted data and a verified audit chain. Manual commands
and consent/denial experiments: [docs/quickstart.md](docs/quickstart.md).

## What this is — and is not

**Is:** an honest, testable reference for local-first multi-application automation with
explicit authorization, offline capability and end-to-end traceability. **Is not:** production
software, a compliance certification, perfect PII protection, or a claim that any third-party
runtime is supported. Synthetic data only; no credentials in the repository.

Security policy and vulnerability reporting: [SECURITY.md](SECURITY.md). Blueprint:
[spec v3.0 and release plan](docs/planning/).

## Documentation and development

- **Wiki (main reference):** [Home](https://github.com/SergeyGer/AetherWorkspace/wiki) ·
  [Project status](https://github.com/SergeyGer/AetherWorkspace/wiki/Project-Status) ·
  [Repository layout](https://github.com/SergeyGer/AetherWorkspace/wiki/Repository-Layout) ·
  [Known limitations](https://github.com/SergeyGer/AetherWorkspace/wiki/Known-Limitations)
- **Repository docs:** [quick start](docs/quickstart.md) · [architecture](docs/architecture/overview.md) ·
  [threat model](docs/security/threat-model.md) · [ADRs](docs/adr/) · [contracts](docs/contracts/) ·
  [benchmarks](docs/BENCHMARKS.md) · [releases](docs/releases/)
- **Contributing:** [CONTRIBUTING.md](CONTRIBUTING.md). Please keep the synthetic-data and
  fail-closed boundaries intact.

## License

MIT — see [LICENSE](LICENSE) and [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md).
