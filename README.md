# AetherWorkspace

[![CI](https://github.com/SergeyGer/AetherWorkspace/actions/workflows/ci.yml/badge.svg)](https://github.com/SergeyGer/AetherWorkspace/actions/workflows/ci.yml)

AetherWorkspace is an independent, vendor-neutral **reference implementation** of a
local-only desktop interoperability workspace: a policy-aware AI planner proposes a
versioned `ActionPlan`, a deterministic executor validates and authorizes every step,
and a local broker drives three independent synthetic desktop applications over
authenticated local IPC. All fixtures are fictitious; nothing in this repository is
production software or a compliance certification.

- **Status:** **v0.4.0** — bounded FDC3 interoperability, privacy controls (detector
  escalation, session token vault), capability-based model routing, real OpenAI/Gemini
  provider smoke evaluation and Windows 11 reference-desktop test infrastructure.
  Windows 11 physical-desktop evidence is produced locally per
  [docs/testing/windows11-reference.md](docs/testing/windows11-reference.md); the first R1
  `release-full` run passed on a physical Windows 11 laptop for v0.4.0 (sanitized summary in
  [docs/testing/evidence/2026-11-16-windows11-release-full.md](docs/testing/evidence/2026-11-16-windows11-release-full.md)).
  GitHub CI remains the mandatory gate.
  Milestones M0/v0.1/v0.2 complete for the tested scope; v0.3 implemented for the declared
  bounded subset. CI is green on Linux and Windows (including named-pipe demo, blocked-egress
  checks, Windows IPC benchmark and WPF UI automation). GitHub releases are published per
  milestone. This remains a reference implementation, not production software.
- **Platform:** .NET 10 / C# 14. Windows 11 x64 is the reference desktop (WPF shell and
  WPF sample apps); cross-platform core libraries and a headless host build and run on
  Linux CI as the deterministic test double. Production IPC on Windows uses gRPC over
  named pipes; Linux development/CI uses the same typed client over Unix domain sockets.
- **Design baseline:** Technical Specification v3.0 and Implementation & Release Plan
  v3.0 (see [`docs/planning/`](docs/planning/)).

## Product hypothesis

For an instrument-centric, multi-application task, a policy-aware planner plus a
deterministic cross-process executor can reduce manual operations and completion time
while preserving explicit authorization, offline capability and end-to-end
traceability — and without ever letting untrusted model output execute processes
directly or transmit restricted data to a cloud provider without explicit consent.

## Status and evidence

| Milestone | Status | Evidence |
|---|---|---|
| M0 feasibility & foundations (AW-001…AW-006) | **Implemented** | Solution/CI skeleton, versioned Protobuf + JSON contracts, local transport prototype, deny-by-default policy, threat model/ADRs, benchmark harness |
| v0.1.0 MVP (AW-007…AW-015) | **Implemented vertical slice** | One automated three-process synthetic workflow (launch/publish/arrange/summarize), authenticated sessions and capability checks, deterministic guarded executor, private local summary, policy denials and cloud-denial path, 100-entry unsafe-plan corpus |
| v0.2 reliability (AW-016…AW-020) | **Implemented for the tested scope** | Bounded queues/FIFO/overflow/dedup + RPC-level idempotent retries, hang/kill/broker-disconnect fault injection, heartbeat + bounded restart, IPC benchmark baseline, offline mode with provider health, rotating/retained hash-chained audit with cross-restart chain continuation. Windows reference benchmarks and OS-level blocked-egress capture remain |
| v0.3 bounded FDC3 (AW-021…AW-024) | **Implemented for the tested bounded subset** | `fdc3.instrument` + `fdc3.organization` contexts on the `default` channel with provenance, bounded intent resolution (`ViewInstrument`/`ViewAnalysis`/`ViewChart`), app-directory subset via broker snapshot, contract fixtures + dedicated test suite + multi-process E2E. Declared subset, no conformance claim; AW-024 external increment: official FDC3 3.0 JSON Schemas validated by an independent Python validator + .NET tests (7/7 payloads) with a deviation register; user channels and third-party Desktop-Agent runtime interop remain unsupported |
| v0.4+ (AW-025…AW-038) | **Not started** | Privacy detectors beyond rules, vault, model router/evaluation, inspector, contract studio, release packaging |

Automated evidence lives in `tests/` (contract, security, execution, AI, broker,
workflow and multi-process E2E suites). See [`docs/IMPLEMENTATION_STATUS.md`](docs/IMPLEMENTATION_STATUS.md)
for the issue-by-issue mapping.

## Quick start

Prerequisites: [.NET 10 SDK](https://dotnet.microsoft.com/download). Windows 11 is
required to see the WPF shell/sample windows; Linux supports the headless host and the
full test suite.

```bash
# Linux / headless (builds the core solution, runs the reference workflow demo)
./scripts/demo.sh

# Run the whole verification suite
./scripts/verify.sh
```

On Windows, use the PowerShell demo which exercises the Windows named-pipe transport
with the headless sample processes (the WPF windows use the same broker protocol):

```powershell
pwsh ./scripts/demo.ps1
```

The demo starts a broker process, runs the reference command below, launches three
independent sample processes, publishes a typed instrument context, arranges their
top-level windows deterministically, and produces a private local summary:

> “Open Northstar bond analytics, show the associated risk view, synchronize this
> instrument across the workspace and summarize the exposure privately.”

A successful demo prints a trace id, the plan, per-step outcomes, the summary, the
computed window layout, egress instrumentation (`external=0` for RESTRICTED data) and a
verified audit chain. See [`docs/quickstart.md`](docs/quickstart.md) for manual
commands, policy configuration, cloud-consent experiments and the denial demos.

## Architecture at a glance

```
┌──────────────┐   trusted local IPC (gRPC over named pipe / Unix socket)   ┌────────────────────┐
│  WPF Shell   │◄────────────── authenticated session, capabilities ──────►│  Aether.Broker     │
│ (or headless)│                                                            │ registry · launch  │
└──────┬───────┘                                                            │ sessions · channels│
       │ classify → plan → validate → authorize → execute                    │ windows · audit    │
       ▼                                                                    └─────────┬──────────┘
┌──────────────┐    untrusted plan proposal (deterministic grammar first,              │ launch / route
│ Aether.AI    │    optional cloud adapter behind the egress gateway)                 ▼
└──────────────┘                                                       ┌──────────────────────────┐
                                                                       │ 3 independent sample apps│
                                                                       │ Watchlist · Risk · Chart │
                                                                       └──────────────────────────┘
```

Key invariants:

- **AI output is an untrusted proposal.** Plans are strict JSON, schema-validated,
  allowlisted, bounded (action count/deadlines) and capability-authorized before any
  side effect. There are no executable paths, shell commands or arbitrary URLs in the
  plan language.
- **Data classification is source-first and fail-closed.** Four labels
  (`PUBLIC`/`INTERNAL`/`RESTRICTED`/`UNKNOWN`); a detector may escalate but never
  downgrade; `UNKNOWN` is treated as restricted. Cloud is disabled by default, requires
  explicit consent, and `RESTRICTED`/`UNKNOWN` never reach a cloud provider.
- **One egress chokepoint.** Every provider attempt (including retries and errors) goes
  through `EgressGateway`, which can be observed in tests to make exactly **zero**
  external calls on denied paths.
- **Session-bound local IPC.** Apps only launch from a canonical allowlist, register
  with a single-use token bound to the launched process identity, and every privileged
  operation re-checks short-lived capabilities.
- **Metadata-only, hash-chained audit.** No raw prompts, payloads or credentials.

Full details: [`docs/architecture/overview.md`](docs/architecture/overview.md),
[`docs/security/threat-model.md`](docs/security/threat-model.md),
[`docs/adr/`](docs/adr/), [`docs/contracts/`](docs/contracts/).

## Repository layout

| Path | Contents |
|---|---|
| `src/Aether.Contracts` | Protobuf envelopes/broker service, strict JSON models and schemas |
| `src/Aether.Security` | classification, deny-by-default policy, egress gateway, audit chain, consent |
| `src/Aether.Execution` | plan parser/validator, capability authorization, deterministic state machine, compensation |
| `src/Aether.AI` | deterministic command planner, optional cloud/local adapters, privacy-routed summarization |
| `src/Aether.Broker` (+ `.Client`) | local gRPC broker: registry, launcher, sessions, channels, windows, health, audit |
| `src/Aether.BrokerApp` | broker process host (local-only, endpoint handshake file) |
| `src/Aether.Workflow` | end-to-end orchestration and typed action handlers |
| `src/Aether.Shell` / `.Headless` | WPF shell (Windows) and CI/E2E headless host |
| `src/Aether.SampleApps` / `.Headless` | three independent synthetic apps (WPF and headless) |
| `samples/` | trusted app registry, context catalog, synthetic fixtures |
| `tests/` | contract, security, execution, AI, broker, workflow and E2E suites plus negative corpora |
| `benchmarks/` | BenchmarkDotNet microbenchmarks and measured IPC latency harness |
| `docs/` | architecture, ADRs, threat model, contracts/schemas, benchmark methodology, planning baseline |
| `scripts/` | demo, verification and benchmark helper scripts |

## Testing

```bash
dotnet build AetherWorkspace.slnx -c Release          # warnings are errors
dotnet test  AetherWorkspace.slnx -c Release         # unit, contract, integration, E2E
```

The suites cover normal, failure and authorization-denied behaviour, including a
committed **100-entry unsafe-plan corpus** (every entry must be denied before any side
effect), zero-external-call regressions for `RESTRICTED`/`UNKNOWN`, audit-tamper and
audit-rotation/continuation tests, hang/kill/broker-disconnect fault injection, and
multi-process E2E workflows with a real broker process. Current measured IPC baseline:
see [`docs/BENCHMARKS.md`](docs/BENCHMARKS.md).

## Security and data policy

- Synthetic data only: fictitious issuers, synthetic identifiers and values. No real
  customer, position, transaction or market data; no credentials in the repository.
- Telemetry egress is disabled by default; the broker exposes no network listener.
- Cloud model use is opt-in, requires explicit consent, is deny-by-default per
  classification and is unreachable for `RESTRICTED`/`UNKNOWN`.
- Known residual risks (same-user process isolation, rule-based PII detection limits,
  local-admin audit tampering, loopback models not being automatically air-gapped) are
  documented in the threat model. There is no claim of perfect PII detection, zero
  leakage, GDPR/DORA compliance or FDC3 conformance.

Report vulnerabilities as described in [`SECURITY.md`](SECURITY.md).

## Known limitations (summary)

- Windows named pipes and the headless reference workflow are **runtime-verified by the
  Windows CI job** (build, full test suite, named-pipe demo). WPF shell/sample windows are
  build-verified; their interactive UI behaviour (and the actual Win32 window placement
  visuals) still needs manual/automated desktop verification. This development
  environment is Linux, where Unix domain sockets + headless samples are the substitute.
- Cross-process window embedding/reparenting (`SetParent`/`HwndHost`) is explicitly
  excluded from the MVP and was **not** evaluated; no docking claims are made.
- v0.2 is implemented for the tested Linux scope; the full Windows reference benchmark,
  OS-level blocked-egress capture and the remaining v0.3–v1.0 scope (FDC3 intents, model
  router/evaluation, Live Inspector, contract studio, reproducible release packaging,
  SBOM) are not implemented yet.
- The chart sample uses WPF vector rendering rather than WebView2 in the MVP.

See [`docs/limitations.md`](docs/limitations.md) for the complete list.

## Documentation index

- [Quick start](docs/quickstart.md) · [Architecture](docs/architecture/overview.md)
- [Threat model](docs/security/threat-model.md) · [ADRs](docs/adr/)
- [Contracts and schemas](docs/contracts/) · [Benchmarks](docs/BENCHMARKS.md)
- [Limitations](docs/limitations.md) · [Implementation status](docs/IMPLEMENTATION_STATUS.md)
- [FDC3 supported subset](docs/fdc3/supported-subset.md) · [.env example](.env.example) · [Release notes](docs/releases/)
- [Planning baseline (spec v3.0 + release plan + issue pack)](docs/planning/)

## Contributing

See [`CONTRIBUTING.md`](CONTRIBUTING.md). The project is a reference implementation for
learning, review and further research; every feature must keep the synthetic-data and
fail-closed boundaries intact.

## License

MIT — see [`LICENSE`](LICENSE). Third-party notices: [`THIRD_PARTY_NOTICES.md`](THIRD_PARTY_NOTICES.md).
Dependency/model licensing must be re-reviewed before any public release tag.
