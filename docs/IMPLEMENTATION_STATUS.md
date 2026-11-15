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
| AW-003 gRPC/Named Pipes transport and authenticated handshake | **Done (Linux UDS executed locally; Windows named pipe executed in CI)** | `src/Aether.Broker/Transport`, `BrokerHost.cs` Kestrel named-pipe/Unix-socket configuration, `Aether.Broker.Client` channel factory, session handshake in `Sessions/SessionManager.cs`; integration tests in `tests/Aether.Broker.Tests` (52 tests) |
| AW-004 Cross-process docking feasibility spike | **Deferred, no dependency** | ADR-005 records that HWND embedding is excluded from the MVP and not evaluated; top-level arrangement implemented instead. No docking claim |
| AW-005 Threat model, default policy and constraints | **Done** | `docs/security/threat-model.md`, default deny-by-default policy `src/Aether.Security/Policy`, UNKNOWN-denies-cloud unit tests, egress/audit controls; `tests/Aether.Security.Tests` (38 tests) |
| AW-006 Benchmark and golden workflow harness | **Done** | `benchmarks/Aether.Benchmarks` (BenchmarkDotNet), `benchmarks/Aether.IpcBench` (real broker + 3 subscribers p50/p95/p99), `docs/BENCHMARKS.md` with measured baseline, `scripts/run-benchmarks.sh` |

## v0.1.0 — MVP vertical slice (AW-007…AW-015)

| Issue | Status | Evidence |
|---|---|---|
| AW-007 Allowlisted app registry and process launcher | **Done** | `Registry/AppRegistry.cs`, `Launching/AppLauncher.cs` (canonical paths, allowed roots, fixed argument templates, no model-supplied paths); tests: unknown app, unallowlisted executable, path traversal, reuse semantics |
| AW-008 Broker registration and capability handshake | **Done** | `Sessions/SessionManager.cs`, `Sessions/BrokerSession.cs`, `Hosting/BrokerAuthInterceptor.cs`; single-use token, process identity binding (pid/start-time/executable), replay/expiry/identity negatives, capability checks on privileged RPCs |
| AW-009 Three independent synthetic sample apps | **Done (headless executed on Linux+Windows CI; WPF build-verified)** | `src/Aether.SampleApps.Headless` (three roles, deterministic virtual windows), `src/Aether.SampleApps` (WPF Watchlist/Risk/Chart), `samples/registry/apps.json` + `apps.windows.json` |
| AW-010 Basic WPF Shell and top-level window arrangement | **Done (WPF build-verified on Windows CI; arrangement logic executed via headless arranger on Linux, Windows build+demo green)** | `src/Aether.Shell` WPF command bar/status/trace, `Windows/LayoutPlanner.cs` deterministic + `Win32WindowArranger.cs`, virtual arranger for CI; layout tests + E2E layout assertions |
| AW-011 Typed context channels and trace propagation | **Done** | `Channels/ChannelRouter.cs` bounded per-subscriber queues, FIFO, overflow accounting, dedupe; envelope carries message/trace/span/source/classification/provenance; tests assert all three app logs carry the workflow trace id |
| AW-012 Guarded ActionPlan validator and executor | **Done** | `Execution/Validation/ActionPlanValidator.cs`, `Execution/Authorization`, `Execution/Executor/PlanExecutor.cs` (states, deadlines, cancellation, compensation); 100-entry unsafe corpus all denied before handler invocation; NFR-03 evidence |
| AW-013 Command bar and pluggable planning adapter | **Done** | `AI/Planner/DeterministicCommandPlanner.cs` (reference command exact plan), `AI/Planner/CloudPlanProvider.cs` behind egress gateway; headless shell + WPF shell command bar; tests: cloud requires consent/permitted classification, denied payloads never reach the transport |
| AW-014 Privacy gateway and private local summary | **Done** | `Security/Classification`, `Policy/PolicyEngine.cs`, `Egress/EgressGateway.cs`, `AI/Summarization/LocalDeterministicSummaryProvider.cs`; E2E: RESTRICTED workflow produces private summary with `externalAttempts = 0`; missing model yields safe error without cloud fallback |
| AW-015 Automated MVP end-to-end workflow and quick start | **Done** | `scripts/demo.sh` (Linux), `scripts/demo.ps1` (Windows), `tests/Aether.E2E.Tests` (9 multi-process tests: reference workflow, repeatability, unsafe-plan denial, cloud/UNKNOWN denial, offline/missing-model, crash+restart), README/quickstart |

## v0.2.0 — reliability and observability (AW-016…AW-020)

| Issue | Status | Evidence / remaining work |
|---|---|---|
| AW-016 Bounded queues, ordering, backpressure, deduplication | **Done** | Per-channel bounded queues with explicit overflow reporting, FIFO per subscriber, clearance filtering, bounded dedupe window and cancellation (`ChannelRouterTests`); RPC-level idempotent retry evidence: same `clientMessageId` is delivered once and returns the original `messageId` (`FaultInjectionTests.DuplicateClientMessageIdIsDeliveredOnlyOnceAtTheRpcBoundary`). Retry eligibility is documented in [`docs/contracts/README.md`](contracts/README.md) |
| AW-017 Heartbeat, fault isolation, controlled restart | **Done (Linux fault injection; Windows runtime tests + demo green in CI)** | Heartbeat RPC, missed-heartbeat states, process-exit detection and bounded `RestartApp`; fault injection: `SIGSTOP`-hung app becomes `UNRESPONSIVE` and bounded restart recovers to `HEALTHY`; `SIGKILL`ed app is reported `EXITED` and restarts; broker/shell survive sample-app crash (`FaultInjectionTests`, `FaultInjectionE2ETests`); shell fails fast with a structured error when the broker process dies. Hang injection is Unix-only (documented) |
| AW-018 Measure IPC, UI responsiveness, resources | **Done for the tested scope** | IPC latency harness + measured Linux p50/p95/p99 and microbenchmark allocations in [`BENCHMARKS.md`](BENCHMARKS.md); Windows named-pipe IPC benchmark runs in Windows CI (results in the CI artifact `windows-evidence/.aether/windows-ipc-benchmark.txt`). WPF UI responsiveness under load is not separately profiled yet |
| AW-019 Offline mode and failure behaviour | **Done for the tested scope** | `--offline` mode + provider health (no network I/O); missing local model safe error with zero cloud fallback; OS-level blocked-egress checks: Linux reference workflow inside an isolated network namespace with no external connectivity, Windows named-pipe demo under a Windows Firewall outbound block for the .NET host (`scripts/egress-blocked-check.*`, run in CI) |
| AW-020 Tamper-evident local audit chain | **Done** | Metadata-only hash chain (+ optional HMAC), JSONL persistence, size-bounded rotation with retention, verification across retained/rotated files, chain continuation across process restarts, tamper/gap detection (`AuditChainTests`, `AuditRotationTests`, E2E). Signed/anchor-based storage remains future work |
| AW-021 Supported-subset matrix | **Done for the declared subset** | [`docs/fdc3/supported-subset.md`](fdc3/supported-subset.md) documents contexts, bounded intents, resolution rules, app-directory projection, unsupported surfaces, versioning and evidence. Official-standard review remains a review checklist item |
| AW-022 Supported FDC3 contexts and channels | **Done for the declared subset** | `fdc3.instrument` and `fdc3.organization` typed payloads, strict validation, single `default` app channel, provenance preserved; contract fixtures + broker channel tests + multi-process E2E (`Fdc3E2ETests`). User channels/other contexts not implemented |
| AW-023 Limited intent resolution and app directory | **Done for the declared subset** | `ResolveIntent` RPC + `intent.resolve` capability, `ViewInstrument`/`ViewAnalysis`/`ViewChart` mapping to apps declaring the intent and required context type, preferred-target approval, structured denials; app-directory subset via `GetSnapshot` intents; capability/negative tests |
| AW-024 Contract and external interop evidence | **Partial** | Fixtures, version/denial contract tests and multi-process E2E are implemented; **no independent FDC3 implementation or official conformance suite has been run**. External interop remains required before any "conformant" wording |

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

CI evidence (v0.2): commit `674e2c0` run
[43641011037](https://github.com/SergeyGer/AetherWorkspace/actions/runs/43641011037) is green
on **both** Linux (build + 333 tests + demo + IPC benchmark smoke) and Windows (build
including WPF, full test suite, named-pipe demo); GitHub's default CodeQL setup is also
green. The named-pipe demo initially exposed a real client bug (`NamedPipeClientStream`
properties set before `ConnectAsync`), which is fixed and regression-covered by the CI
demo.

Recorded baseline totals after the v0.3 step: **354 automated tests** across 8 test
projects (contract 41, security 50, execution 140 incl. 100 unsafe-plan cases, AI 27,
broker 56, workflow 9, FDC3 15, E2E 16) plus **2 Windows-only WPF UI automation tests**
(FlaUI, separate project not run on Linux) and the measured IPC baselines. Counts grow as
work continues; run `dotnet test` for the authoritative current number.

## Deliberate deviations and decisions

- Reference workflow action budget: configuration uses 6 actions while the library
  default remains 5 (ADR-008).
- Chart sample uses WPF vector rendering instead of WebView2 (no WebView2 dependency);
  documented in limitations.
- Linux UDS + headless samples are used for deterministic CI/E2E while Windows paths are
  compile-verified; the Windows CI job provides runtime evidence for named pipes/WPF
  build. This is a documented environment constraint, not a product claim.
