# Implementation status (mapped to the v3.0 plan)

Status legend: **Done** = implemented with committed automated evidence; **Partial** =
key parts implemented and tested, milestone gate not fully met; **Not started** = planned
only. Every claim below points at code and tests in this repository. No release tag
exists yet.

## M0 — feasibility and engineering foundations (AW-001…AW-006)

| Issue | Status | Evidence |
|---|---|---|
| AW-001 Bootstrap .NET 10 solution and CI skeleton | **Done** | `AetherWorkspace.slnx`, `global.json` (SDK 10.0.100 roll-forward, tested 10.0.404), `Directory.Packages.props`, `Directory.Build.props` (warnings-as-errors, nullable), `.editorconfig`, Linux+Windows CI workflows, unit-test discovery |
| AW-002 Versioned contracts and synthetic fixtures | **Done** | `src/Aether.Contracts/Protos/aether/v1/*.proto`, strict JSON models, `docs/contracts/*.schema.json`, 3 synthetic fixtures in `samples/fixtures/`, context catalog + registry; `tests/Aether.Contracts.Tests` (41 tests incl. schema validation, fixture purity, proto round-trip) |
| AW-003 gRPC/Named Pipes transport and authenticated handshake | **Done (Linux UDS executed; Windows named pipe compile-verified)** | `src/Aether.Broker/Transport`, `BrokerHost.cs` Kestrel named-pipe/Unix-socket configuration, `Aether.Broker.Client` channel factory, session handshake in `Sessions/SessionManager.cs`; integration tests in `tests/Aether.Broker.Tests` (52 tests) |
| AW-004 Cross-process docking feasibility spike | **Deferred, no dependency** | ADR-005 records that HWND embedding is excluded from the MVP and not evaluated; top-level arrangement implemented instead. No docking claim |
| AW-005 Threat model, default policy and constraints | **Done** | `docs/security/threat-model.md`, default deny-by-default policy `src/Aether.Security/Policy`, UNKNOWN-denies-cloud unit tests, egress/audit controls; `tests/Aether.Security.Tests` (38 tests) |
| AW-006 Benchmark and golden workflow harness | **Done** | `benchmarks/Aether.Benchmarks` (BenchmarkDotNet), `benchmarks/Aether.IpcBench` (real broker + 3 subscribers p50/p95/p99), `docs/BENCHMARKS.md` with measured baseline, `scripts/run-benchmarks.sh` |

## v0.1.0 — MVP vertical slice (AW-007…AW-015)

| Issue | Status | Evidence |
|---|---|---|
| AW-007 Allowlisted app registry and process launcher | **Done** | `Registry/AppRegistry.cs`, `Launching/AppLauncher.cs` (canonical paths, allowed roots, fixed argument templates, no model-supplied paths); tests: unknown app, unallowlisted executable, path traversal, reuse semantics |
| AW-008 Broker registration and capability handshake | **Done** | `Sessions/SessionManager.cs`, `Sessions/BrokerSession.cs`, `Hosting/BrokerAuthInterceptor.cs`; single-use token, process identity binding (pid/start-time/executable), replay/expiry/identity negatives, capability checks on privileged RPCs |
| AW-009 Three independent synthetic sample apps | **Done (headless executed; WPF compile-verified)** | `src/Aether.SampleApps.Headless` (three roles, deterministic virtual windows), `src/Aether.SampleApps` (WPF Watchlist/Risk/Chart), `samples/registry/apps.json` + `apps.windows.json` |
| AW-010 Basic WPF Shell and top-level window arrangement | **Done (WPF compile-verified; arrangement executed via headless arranger)** | `src/Aether.Shell` WPF command bar/status/trace, `Windows/LayoutPlanner.cs` deterministic + `Win32WindowArranger.cs`, virtual arranger for CI; layout tests + E2E layout assertions |
| AW-011 Typed context channels and trace propagation | **Done** | `Channels/ChannelRouter.cs` bounded per-subscriber queues, FIFO, overflow accounting, dedupe; envelope carries message/trace/span/source/classification/provenance; tests assert all three app logs carry the workflow trace id |
| AW-012 Guarded ActionPlan validator and executor | **Done** | `Execution/Validation/ActionPlanValidator.cs`, `Execution/Authorization`, `Execution/Executor/PlanExecutor.cs` (states, deadlines, cancellation, compensation); 100-entry unsafe corpus all denied before handler invocation; NFR-03 evidence |
| AW-013 Command bar and pluggable planning adapter | **Done** | `AI/Planner/DeterministicCommandPlanner.cs` (reference command exact plan), `AI/Planner/CloudPlanProvider.cs` behind egress gateway; headless shell + WPF shell command bar; tests: cloud requires consent/permitted classification, denied payloads never reach the transport |
| AW-014 Privacy gateway and private local summary | **Done** | `Security/Classification`, `Policy/PolicyEngine.cs`, `Egress/EgressGateway.cs`, `AI/Summarization/LocalDeterministicSummaryProvider.cs`; E2E: RESTRICTED workflow produces private summary with `externalAttempts = 0`; missing model yields safe error without cloud fallback |
| AW-015 Automated MVP end-to-end workflow and quick start | **Done** | `scripts/demo.sh` (Linux), `scripts/demo.ps1` (Windows), `tests/Aether.E2E.Tests` (9 multi-process tests: reference workflow, repeatability, unsafe-plan denial, cloud/UNKNOWN denial, offline/missing-model, crash+restart), README/quickstart |

## v0.2.0 — reliability and observability (AW-016…AW-020)

| Issue | Status | Evidence / remaining work |
|---|---|---|
| AW-016 Bounded queues, ordering, backpressure, deduplication | **Partial** | Implemented: per-channel capacity, explicit overflow result, FIFO, bounded dedupe window, cancellation. Verified by `ChannelRouterTests`. Remaining: full retry-eligibility policy documentation/tests and overload benchmarks |
| AW-017 Heartbeat, fault isolation, controlled restart | **Partial** | Implemented: heartbeat RPC, missed-heartbeat health states, bounded restart RPC, crash recovery E2E (broker/shell survive a sample-app crash). Remaining: hang/disconnect injection matrix, UI restart affordance execution on Windows |
| AW-018 Measure IPC, UI responsiveness, resources | **Partial** | IPC latency harness + measured p50/p95/p99 and microbenchmark allocations in `docs/BENCHMARKS.md`. Remaining: Windows reference-machine run, UI thread responsiveness measurement under WPF, GC/CPU resource profile |
| AW-019 Offline mode and failure behaviour | **Partial** | Deterministic private path tested without model/cloud; missing-model failure has no cloud fallback; cloud disabled by default. Remaining: explicit network-egress-blocked environment test at OS level, provider-health UI status |
| AW-020 Tamper-evident local audit chain | **Partial** | Implemented: metadata-only hash-chained audit (+ optional HMAC), JSONL persistence, tamper/gap detection tests, no raw payload assertions. Remaining: rotation/retention configuration, documented operational runbook, audit verification CLI |

## v0.3.0 — bounded FDC3 interoperability (AW-021…AW-024)

| Issue | Status | Evidence / remaining work |
|---|---|---|
| AW-021 Supported-subset matrix | **Partial** | `docs/fdc3/supported-subset.md` documents the implemented subset and unsupported surfaces honestly; review against the official standard is pending |
| AW-022 Supported FDC3 contexts and channels | **Partial** | Implemented: `fdc3.instrument` context subset on the single `default` app channel with provenance. Remaining: additional contexts and user/app channel semantics |
| AW-023 Limited intent resolution and app directory | **Not started** | No raise/resolve intent flow yet |
| AW-024 Contract and external interop evidence | **Not started** | No independent FDC3 implementation interop test yet |

## v0.4.0…v1.0.0 — not started

AW-025…AW-038 (local entity detection, token vault, model router, model evaluation,
egress adversarial regression, docking decision, multi-monitor layouts, Live Inspector,
diagnostics export, contract studio, reproducible Windows distribution/SBOM, public docs
and final release gate) are planned only. See `docs/planning/implementation-release-plan-v3.0.md`.

## Verification commands

```bash
dotnet build AetherWorkspace.slnx -c Release
dotnet test  AetherWorkspace.slnx -c Release
./scripts/demo.sh
dotnet run -c Release --project benchmarks/Aether.IpcBench -- --samples 1000 --warmup 100
```

Recorded baseline totals for this tree: 316+ automated tests across 7 test projects
(contract 41, security 38, execution 140 incl. 100 unsafe-plan cases, AI 27, broker 52,
workflow 9, E2E 9) plus the measured IPC baseline. Counts grow as work continues; run
`dotnet test` for the authoritative current number.

## Deliberate deviations and decisions

- Reference workflow action budget: configuration uses 6 actions while the library
  default remains 5 (ADR-008).
- Chart sample uses WPF vector rendering instead of WebView2 (no WebView2 dependency);
  documented in limitations.
- Linux UDS + headless samples are used for deterministic CI/E2E while Windows paths are
  compile-verified; the Windows CI job provides runtime evidence for named pipes/WPF
  build. This is a documented environment constraint, not a product claim.
