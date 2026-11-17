# Implementation status (mapped to the v3.0 plan)

Status legend: **Done** = implemented with committed automated evidence; **Partial** =
key parts implemented and tested, milestone gate not fully met; **Not started** = planned
only. Every claim below points at code and tests in this repository.

**Snapshot:** 16 November 2026 — latest released tags **v1.0.1** (corrective) and **v1.0.0**
(reproducible reference release). This page is the implementation mapping; the authoritative
versioned summary is the [Wiki Project status](https://github.com/SergeyGer/AetherWorkspace/wiki/Project-Status).
Open post-v1.0 findings and their disposition are tracked in
[`docs/audit/2026-11-16-gap-assessment-response.md`](audit/2026-11-16-gap-assessment-response.md).

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
| AW-024 Contract and external interop evidence | **Done for the practicable scope** | Fixtures, version/denial contract tests and multi-process E2E. External interop: (1) official FDC3 3.0 JSON Schemas validated by an independent Python validator + .NET tests (7/7 payloads) with deviation register; (2) **runtime DACP interop with the independent FINOS Sail headless Desktop Agent** — contexts/intents exchanged in official DACP messages, 5/5 checks. The official JS Desktop Agent conformance runner is not applicable to this broker architecture, and **no conformance/certification claim** is made |

## v0.4.0 — privacy, model routing/evaluation and Windows 11 test infrastructure

| Item | Status | Evidence |
|---|---|---|
| AW-025 Local entity detection and escalation | **Implemented for the tested scope** | `SensitivePatterns` now detects account/IBAN/email/phone/LEI/ISIN shapes; `ClassificationEscalator` never downgrades, strong categories ⇒ RESTRICTED, unknown categories ⇒ INTERNAL, detector failure/malformed findings ⇒ UNKNOWN (fail-closed); supervised by `SourceFirstClassifier`. 47 escalation tests. Shape detectors are not validators; no precision/recall claim without a labelled corpus |
| AW-026 Session token vault | **Implemented for the tested scope** | `SessionTokenVault` (session-scoped, capacity/TTL/eviction/cancel, thread-safe, reversible masking `[TOKEN:N]`, content masking/unmasking, audit snapshot without raw tokens). 29 vault tests. Not wired into every AI call path yet; adapter integration is the remaining step for full prompt masking |
| AW-027 Capability-based model router | **Implemented for the tested scope** | `ModelRouter` + `ModelProviderSettings`: cloud only for PUBLIC + enabled + consent + configured provider; INTERNAL/RESTRICTED/UNKNOWN never attempt cloud and fall back to local model/deterministic. New native Gemini transport + OpenAI-compatible adapter. Shell JSON exposes route/provider/model/reason. 10 router/transport tests; existing egress regression tests remain green |
| AW-028 Model evaluation | **Done for the smoke scope** | `tools/Aether.ModelEval` + synthetic corpus; first real-provider smoke on 2026-11-15: `gpt-4o-mini` 6/6 responses and 6/6 syntactically valid bounded plans, `gemini-3.1-flash-lite` 6/6 and 6/6, avg latency ~1.3–1.4 s, estimated cost < $0.01; sanitized evidence in `docs/evaluation/results-2026-11-15.md`. No quality ranking or statistical claim; larger corpus/TTFT scoring remains |
| AW-029 Adversarial egress regression | **Implemented for the tested scope** | Router + egress tests prove non-PUBLIC data never reaches a cloud transport; CI OS-level blocked-egress checks (Linux network namespace, Windows firewall) remain; evaluation harness never executes model output |
| AW-039 Windows 11 reference baseline | **Done** | Published reference-desktop diagram, testing strategy, lifecycle, hardware/preflight/run-result contracts, and first sanitized R1 evidence record (`docs/testing/evidence/2026-11-16-windows11-release-full.md`); reference device: Windows 11 Pro build 26300, Core Ultra 7 255H, 63.3 GB RAM, Arc 140T, 1 monitor/100% DPI |
| AW-040 Local PowerShell orchestrator | **Done** | `tools/windows-test/Run-AetherTests.ps1` executed on the physical Windows 11 laptop in all three profiles (`developer-smoke`, `daily-smoke`, `release-full`) — PASS, approved-SHA trust, preflight gates, detached worktree, manifest/run-result, cleanup. TRX evidence discovery hardened in v0.4.2 (project-scoped TestResults, freshness filter, SHA-256 de-duplication) after control-run duplicate over-counting; PASS statuses are exit-code/per-project-log authoritative |
| AW-041 WPF GUI E2E over Windows Named Pipes | **Done** | `tests/Aether.Desktop.E2E.Tests` runs on GitHub Windows CI and passed on the physical Windows 11 reference desktop (R1 `release-full`): real broker over named pipes, WPF shell, three sample processes, trace/privacy assertions, 2/2 tests PASS |
| AW-042 WT-04 regression suite on reference desktop | **Done for the v0.4 scope** | WT-04-01/02/07 execute on R1 (2 desktop E2E tests + WPF UI automation); WT-04-03/05/06/08 policy assertions covered by portable CI suites and executed locally via `release-full`. Residual: deeper physical-desktop WT-04 privacy matrix and evidence-scrubbing drill tracked as follow-up |

## v1.0.0 — released 2026-11-16 (reproducible public release)

| Item | Status | Evidence |
|---|---|---|
| AW-036 Reproducible Windows distribution and supply-chain evidence | **Done** | automated tag-driven artifact build (binaries/source/SPDX SBOM/SHA-256); `tools/release/Verify-Release.ps1` verified v0.6.0 assets (VERIFY PASS, SPDX-2.2, 64 packages); `docs/distribution/reproducibility.md` describes verification, rebuild and unsigned-portable boundaries |
| AW-037 Finalize public docs/Wiki/narrated demo | **Done** | concise README with Start-here paths; Wiki project status/layout/limitations/narrated demo; detailed version data moved out of README |
| AW-038 Final release gate + acceptance report | **Done** | final R1 run `20261116-153831-release-full-b3e064`: PASS 511/511, 2 monitors; `docs/testing/evidence/2026-11-16-windows11-v1.0.0-acceptance.md`; v1.0.0 release |

## v0.6.0 — released 2026-11-16 (developer contract studio)

| Item | Status | Evidence |
|---|---|---|
| AW-034 Review-only contract generation assistant | **Done (release v0.6.0)** | `Aether.Contracts.Studio` bounded generators and validator; `tools/Aether.ContractStudio` staging CLI (generate/review/apply --confirm); untrusted status, deterministic output, capability/context/classification constraints, active-registry collision detection, path-safe staging/atomic apply, no compilation/execution; contracts 57/57 |
| AW-035 Validate generated schemas before acceptance | **Done (release v0.6.0)** | `ContractValidationGate`: active-registry add/remove comparison with breaking classification, forbidden path segments, extension enforcement; wired into CLI review/apply; contracts 63/63 |

## v0.5.0 — released 2026-11-16

| Item | Status | Evidence |
|---|---|---|
| AW-030 Owned-window docking | **Not planned (no go decision)** | AW-004 feasibility did not produce a go decision; top-level arrangement remains the supported path. To be recorded as not planned in the v0.5 release |
| AW-031 Durable multi-monitor workspace layouts | **Done for the v0.5 scope** | strict `WorkspaceLayout` model/parser/validation; capture/restore planner with work-area clamping, primary fallback, DPI rescaling; atomic versioned store; `SaveWorkspaceLayout`/`RestoreWorkspaceLayout` RPC with capabilities/audit; `BrokerClient` methods; broker 85 tests. WT-05 `Category=Layout05` desktop tests run in the Windows UI CI lane and in the reference orchestrator. **Physical dual-monitor evidence (2026-11-16):** `release-full` PASS — 484/484 tests, `layout05=4`, monitors DISPLAY1 1920×1200 + DISPLAY2 3440×1440; see [`docs/testing/evidence/2026-11-16-windows11-wt05-layout.md`](testing/evidence/2026-11-16-windows11-wt05-layout.md). WT-05-03/05/06/10 remain manual/approval-only for v0.5 |
| AW-032 Live Inspector with redacted trace timeline | **Done (release v0.5.0)** | shell `timeline` output; WPF Inspector tab with step timeline and local diagnostics export; broker tests/UI compile; WT-05-08 R1 evidence pending |
| AW-033 Local diagnostics export and support bundle | **Done (release v0.5.0)** | `--export-diagnostics`, redaction/atomic-write/size/path tests (Security 133/133), explicit no-raw-payload/no-telemetry flags; WPF export button; WT-05-08 R1 evidence pending |

Windows 11 physical-desktop evidence follows
[`docs/testing/windows11-reference.md`](testing/windows11-reference.md) and the published
[`reference-desktop run lifecycle`](testing/reference-desktop.md); AW-033/AW-037 gaps after the
v1.0.0 gap assessment are answered in
[`docs/audit/2026-11-16-gap-assessment-response.md`](audit/2026-11-16-gap-assessment-response.md).

## v1.0.2 — diagnostics release (2026-11-16)

| Item | Status | Evidence |
|---|---|---|
| Packaged desktop self-test/diagnostics | **Done** | `Test-AetherWorkflow.ps1` (packaged broker + headless reference workflow + structured result/diagnostics), `Collect-Diagnostics.ps1` (privacy-safe ZIP, endpoint token excluded), headless shell included in the desktop bundle |
| WPF sample-app diagnostics | **Done** | heartbeat parity, unhandled-exception/connect logging to broker child log and `%TEMP%\aether-sample-logs` |
| Real-WPF-sample regression coverage | **Done** | CI tests run the real WPF sample apps through launch/context persistence and the full WPF-shell workflow (`WT-04-09`) |
| Clean-machine desktop workflow verification | **Done** | v1.0.2 bundle: assets `VERIFY PASS`; packaged headless self-test `status=succeeded` 6/6 steps; GUI run 6/6 steps, three real WPF sample windows open, summary + Inspector. Evidence: [`docs/testing/evidence/2026-11-16-windows11-v1.0.2-desktop-bundle.md`](testing/evidence/2026-11-16-windows11-v1.0.2-desktop-bundle.md) |

## v1.0.1 — corrective release (2026-11-16)

| Item | Status | Evidence |
|---|---|---|
| G-01 Windows x64 WPF desktop bundle in releases | **Done** | release workflow now has a `windows-desktop` job (WPF Shell + WPF sample apps + broker + registry/contexts/fixtures + launcher/README) and a `publish` job that requires it; artifact `AetherWorkspace-<tag>-windows-x64-desktop.zip` |
| G-08 stale status/limitations documents | **Done** | this page and [`docs/limitations.md`](limitations.md) reconciled to v1.0.0/v1.0.1; CI doc-consistency test added |
| G-09 tag/R1 provenance | **Done** | provenance record appended to the v1.0.0 acceptance evidence (docs-only diff between R1 SHA and tag target) |
| G-12 machine-readable R1 evidence | **Done** | `docs/testing/evidence/2026-11-16-windows11-v1.0.0-acceptance.manifest.json` |
| G-02/03/04/05/06/10/11 remaining findings | **Accepted deviations / planned v1.1** | dispositions and explicit ADRs in [`docs/audit/2026-11-16-gap-assessment-response.md`](audit/2026-11-16-gap-assessment-response.md) |



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

Recorded baseline totals after the v0.3.1 interop increment: **359 automated tests** across
8 test projects (contract 41, security 50, execution 140 incl. 100 unsafe-plan cases, AI 27,
broker 56, workflow 9, FDC3 20 incl. official-schema interop, E2E 16) plus **2 Windows-only
WPF UI automation tests**
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

## v1.1–v1.5 — demo & development execution (started 2026-11-17)

The execution brief `AW-DP-001` (demo-ready hero, WebView2, hybrid AI, privacy gateway,
extensions) is tracked as GitHub milestones **v1.1 … v1.5** with issues **AW-047…AW-067**
(#75–#95), including the four v1.0 measurement gaps (G-02/G-04/G-06/G-10).

**v1.1 increment 1 (2026-11-17):** AW-047 planning/storyboard/acceptance/runbook; AW-048
three-instrument catalog (ORIN context + VEGA/ORIN chart series + parity tests); AW-049 Watchlist
selection publishes approved typed context with echo suppression; AW-050 Risk real metrics and
Chart actual-series switching. Windows real-WPF GUI test + portable 520/520; evidence
`docs/testing/evidence/2026-11-17-v1.1-interactive-ticker-sync.md`. Remaining v1.1: AW-051/052/054,
AW-053 optional, AW-065/066 measurement, AW-055/056 capture/media. Plan index:
[`docs/planning/v1.1-v1.5-execution-plan.md`](planning/v1.1-v1.5-execution-plan.md). This page
remains the implementation mapping; the Wiki [Roadmap](https://github.com/SergeyGer/AetherWorkspace/wiki/Roadmap)
is the short status view.
