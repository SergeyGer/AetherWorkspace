# Changelog

All notable changes to this reference implementation are recorded here. Version labels follow the
implementation-plan milestones; release assets are published on GitHub Releases.

## [v1.5.0] — Heterogeneous UI, hybrid AI, privacy gate, extension decisions (2026-11-18)

Cumulative v1.2–v1.5 milestone release; runtime evidence boundaries unchanged.

- v1.2: independent WebView2 chart host + strict typed-context bridge, local-only renderer, runtime
  fallback, Windows E2E.
- v1.3: per-capability provider configuration, Apache-2.0 local profile/credential lifecycle,
  120-item gold corpus and live gpt-4o-mini evaluation (98.75% plan / 100% denial; measured TTFT).
- v1.4: ADR-011 rejects sanitized INTERNAL cloud routing; PUBLIC-only default hard-disabled.
- v1.5: ADR-012 Electron no-go; ADR-013 keeps the bounded FDC3 subset with rejected-surface tests.
- Release details: `docs/releases/v1.5.0.md`.

## [v1.1.1] — Narrated hero and final-state social preview (2026-11-18)

Presentation patch on top of v1.1.0 (no runtime/contract changes).

- English narrated Track B hero published on YouTube: https://youtu.be/CFJJZX-QcnU
- Composition: social-preview intro, continuous synthetic professional voiceover,
  1 s crossfades, final spoken GitHub link and QR outro; 93.37 s, AAC 48 kHz.
- Clean capture rebuild from the same raw recording; captions clamped to scene boundaries (no
  overlap); validation PASS.
- Final-state social preview with large mobile-readable type and no fine print.
- Voiceover documented as synthetic TTS; English-only operator decision. Evidence:
  `docs/testing/evidence/2026-11-18-v1.1-narrated-demo.md`.

## [v1.1.0] — Demo-ready (2026-11-18)

Track B hero demo validated on a physical Windows 11 laptop, local capture/media pipeline, demo
layout/process evidence, live LLM provenance, and the v1.0 measurement gaps resolved with honest
evidence.

- **Demo:** clean Track B capture (`cloud-plan`, `usedModel=true`, taskbar hidden, planner readable,
  81.37 s H.264/1080p/yuv420p, 0 black intervals), hero assets under `docs/assets/`; Track A remains
  deterministic.
- **UI responsiveness:** 100-cycle physical report on `8375603` (command 3/8/10 ms p50/p95/p99,
  zero threshold violations) — `docs/testing/evidence/2026-11-18-v1.1-ui-responsiveness.md`.
- **WT matrix:** automated rows PASS; manual DPI/focus/topology/crash/privacy rows are
  **FORMAL_EXCEPTION, not PASS**, tracked by #120 —
  `docs/testing/evidence/2026-11-18-v1.1-wt-formal-exceptions.md`.
- **Release details:** `docs/releases/v1.1.0.md`.

## [Unreleased] — v0.1.0 vertical slice (2026-11-14)

### Changed

- Local demo video acceptance is now **60-95 s** (operator decision 2026-11-17: shorter is fine);
  Build-Video trims leading/trailing black automatically instead of padding the recording.
  Track B remains the primary hero track.

### Added

- **AW-066 UI measurement accepted (2026-11-18):** 100-cycle physical report on `8375603` with pinned
  hardware/display/power profile, command p50/p95/p99 = 3/8/10 ms, frame-probe p95 0.48/0.94/1.78 ms,
  working set 219.9→319.3 MB, managed 12.0→22.5 MB, zero threshold violations; runner records
  thresholds and detects regressions. Evidence: `docs/testing/evidence/2026-11-18-v1.1-ui-responsiveness.md`.
- **AW-065 WT formal exceptions (2026-11-18):** automated WT-04/WT-05 rows PASS; manual DPI 125/150,
  focus/keyboard/modal, display-topology recovery, physical child crash/hang and manual
  Inspector/export privacy are recorded as **FORMAL_EXCEPTION, not PASS**, in
  `docs/testing/evidence/2026-11-18-v1.1-wt-formal-exceptions.md`, tracked by issue #120.


- **v1.1 Track B demo gate PASS (physical Windows 11, 2026-11-18):** clean OBS capture with the
  taskbar auto-hidden/restored and the planner label fully readable; validated live LLM-assisted plan
  (`planner=cloud-plan`, `usedModel=true`, trace `9a681503...`), FFmpeg hero MP4/GIF (81.37 s,
  H.264/1080p/yuv420p, 0 black intervals) and operator review. Evidence:
  `docs/testing/evidence/2026-11-18-v1.1-track-b-demo-gate.md`; hero assets under `docs/assets/`.


- .NET 10 solution with warnings-as-errors, central package management, pinned SDK, and
  Linux + Windows CI workflows.
- Versioned contracts: Protobuf broker service and context envelope; strict JSON
  `ActionPlan`, app manifest and context catalog models; published JSON Schemas and
  human-readable examples under `docs/contracts/`.
- Synthetic fixtures (watchlist, risk, chart), trusted context catalog and allowlisted
  app registry for three independent sample processes.
- Local gRPC broker over named pipes (Windows) / Unix domain sockets (Linux) with
  launch-token registration, process-identity binding, capability checks, bounded
  channels, deterministic layout planning, health state and metadata-only audit chain.
- DTO-inverted security layer: source-first classification, deny-by-default
  classification policy with explicit cloud consent, single egress gateway,
  hash-chained tamper-evident audit.
- Deterministic plan parser/validator, capability authorization and bounded plan
  executor with explicit states, deadlines, cancellation and compensation.
- Deterministic command planner for the reference workflow plus optional cloud planning
  adapter and loopback local model adapter behind policy; private deterministic
  summarizer.
- WPF shell and WPF sample apps (Watchlist/Risk/Chart) for Windows; headless shell and
  headless sample processes for CI/E2E on any platform.
- End-to-end demo scripts, multi-process E2E tests, 100-entry unsafe-plan negative
  corpus, benchmark harnesses with measured IPC baseline, and documentation
  (README, quickstart, architecture, threat model, ADRs, limitations, implementation
  status, benchmark methodology).

## [v0.6.0 — 2026-11-16]

### Added

- **AW-036 release verification and supply-chain evidence:** `tools/release/Verify-Release.ps1`
  validates published SHA-256 checksums, missing assets and SPDX SBOM structure offline; verified
  against v0.6.0 release assets (VERIFY PASS, SPDX-2.2, 64 packages); `docs/distribution/reproducibility.md`
  documents verification, independent rebuild and honest reproducibility boundaries (unsigned portable).
- **AW-034 first slice — review-only contract studio core:** bounded draft generator/validator in
  `Aether.Contracts.Studio` for registry entries, proto3 message snippets and JSON schema
  templates; drafts are explicitly untrusted, deterministic, constrained to documented
  capabilities/context types/classifications, collision-checked against the active registry and
  cannot execute or auto-apply; contracts tests cover valid drafts, collisions, invalid ids and
  schema/proto envelopes.
- **AW-034 staging CLI + explicit review/apply gate:** `tools/Aether.ContractStudio` with
  `generate`, `review` and `apply --confirm` commands; drafts live as untrusted envelopes in a
  staging workspace, review never writes, apply validates first and only writes the confirmed
  target (path-safe, atomic, correct .json/.proto extension); no compilation or execution.
- **AW-035 validation gate:** classified contract-change detection against the active registry
  (new app/capability = non-breaking; removed capability/context-type/classification, reduced
  instances or changed launch definition = breaking and rejected), forbidden output-path checks
  (`.git`, `bin`, `obj`, `.ssh`, `secrets`, `node_modules`, `.env`) and target-extension checks
  with actionable diagnostics, wired into `review`/`apply`; contracts suite 63/63.

## [v0.5.0 — 2026-11-16]

### Added

- **AW-031 core durable workspace layouts:** strict `WorkspaceLayout` model (monitor device
  identity, bounds/work areas, DPI scale hints, per-app bindings), `WorkspaceLayoutValidation`,
  strict `WorkspaceLayoutParser`, and `WorkspaceLayoutPlanner` capture/restore with clamping to
  visible work areas, primary-monitor fallback when a saved monitor disappears, DPI rescaling,
  deterministic ordering and denial when no monitors are available. Published
  `docs/contracts/workspace-layout.schema.json` + example; 18 new broker/contracts tests pass.
- **AW-031 persistence and broker API:** local atomic/versioned `WorkspaceLayoutStore`
  (strict per-layout JSON files, size limits, path-safe ids, corrupt/missing/disabled
  handling), `SaveWorkspaceLayout`/`RestoreWorkspaceLayout` gRPC methods with capability
  checks + audit events, `BrokerClient` methods and `--layout-store` BrokerApp option;
  Windows topology capture via `EnumDisplayMonitors`/DPI with deterministic virtual fallback;
  store and RPC integration tests (broker suite now 85 tests).
- **AW-033 sanitized diagnostics export (headless):** `--export-diagnostics <path.json>` writes an
  operator-initiated local bundle (run/timeline metadata, app health, environment, aggregated
  audit-chain facts, provider routing flags) with synthetic account-id/secret/JWT redaction,
  atomic file write, `.json`/size validation and explicit `rawPayloadsIncluded=false` /
  `telemetryUploads=false` flags; no telemetry uploads, off by default.
- **AW-032 Live Inspector (WPF + headless):** shell output includes a redacted per-step
  `timeline`; the WPF shell gained an **Inspector** tab with the timeline list and an explicit
  **Export diagnostics** action that writes the same sanitized bundle locally (no telemetry).
- **WT-05 layout desktop tests:** new `Category=Layout05` scenarios in
  `tests/Aether.Desktop.E2E.Tests` — single-monitor save/restore with bounds assertions,
  two-monitor placement on real dual-monitor hardware, missing-monitor primary fallback and
  corrupt-layout denial; named-pipe broker harness now provisions `--layout-store`; wired into
  the `release-full`/`daily-smoke` reference profiles and the Windows UI CI lane.
- **Physical WT-05 evidence (Windows 11, 2026-11-16):** dual-monitor `release-full` PASS on
  approved commit `491d4c2` — `portable=476`, `windows-ui=2`, `desktop-e2e=2`, `layout05=4`,
  **484/484 passed** with DISPLAY1 1920×1200 + DISPLAY2 3440×1440; sanitized summary in
  `docs/testing/evidence/2026-11-16-windows11-wt05-layout.md`.
- **Reference dual-monitor baseline:** Windows 11 laptop in Extend mode — DISPLAY1 1920×1200
  primary at (0,0) + DISPLAY2 3440×1440 at (1920,0), both 100% DPI, captured in the
  `environment-manifest.json` `displays.screens` array after the topology fix.

### Added

- Windows reference-test orchestrator now captures the real desktop topology via
  `System.Windows.Forms.Screen.AllScreens` (device name, primary flag, bounds, bits per pixel)
  with a CIM fallback; `environment-manifest.json` gains a `displays.screens` array. This fixes
  multi-monitor detection (`Win32_DesktopMonitor` reported one monitor for a two-monitor setup).
- R1 evidence records the final deterministic v0.4.4 run (`20261116-104313...`): PASS, 445
  portable + 2 windows-ui + 2 desktop-e2e = 449 passed, 0 failed/skipped.

## [Unreleased] — v0.4.4 restore TRX aggregation helper (2026-11-16)

### Fixed

- Restored `Read-TrxCountersAggregate` in `tools/windows-test/Run-AetherTests.ps1` (it was
  dropped during the v0.4.3 deterministic-suite rewrite; the v0.4.3 run executed all eight test
  projects successfully but ended `FAIL_HARNESS` with "Read-TrxCountersAggregate is not
  recognized"). Added a static check that every custom helper call has a definition; only
  PowerShell built-in cmdlets and `git` remain external.
- R1 evidence and reference-desktop run tables record the v0.4.3 failed harness attempt
  (8/8 test projects passed; failure was harness-only) and point to the v0.4.4 fix.

## [Unreleased] — v0.4.3 deterministic per-project test evidence (2026-11-16)

### Fixed

- `tools/windows-test/Run-AetherTests.ps1`: the solution/portable suite no longer discovers TRX
  files from the worktree (heuristics still over-counted on Windows: 664 on v0.4.1, 781 on
  v0.4.2 against the 445 baseline). The orchestrator now enumerates the eight
  `tests/*.Tests.csproj` projects from `AetherWorkspace.slnx`, runs each project separately with
  its own TRX under `results/trx/portable/<project>.trx`, and aggregates exactly those files.
  Locally simulated collection: 8 files / 445 passed. SUITE status derives from per-project
  exit codes and TRX presence (missing TRX ⇒ INCONCLUSIVE, failing project ⇒ FAIL_PRODUCT,
  timeout ⇒ FAIL_HARNESS).
- Recorded the v0.4.2 control `release-full` run (`20261116-094002...`, commit `d44e6ba`) as
  PASS in the R1 evidence doc; a final rerun on v0.4.3 records the deterministic counters
  (ideally with the second monitor connected for the dual-monitor baseline).

## [Unreleased] — v0.4.2 TRX evidence de-duplication and control-run record (2026-11-16)

### Fixed

- `tools/windows-test/Run-AetherTests.ps1`: the solution-TRX collector scanned the whole
  worktree and could pick up duplicate/stray TRX copies; the v0.4.1 control run consequently
  over-counted portable tests (664 vs the 445 baseline). Discovery now only scans
  `tests/<project>/TestResults`, requires files newer than the suite start, and de-duplicates
  identical files by SHA-256. Validated with a synthetic worktree containing a duplicate
  `bin/` copy and a stale TRX: only the 2 valid files were counted (total 5).
- Recorded the v0.4.1 control `release-full` run (`20261116-091552...`, commit `4a2796e`) as
  PASS in the R1 evidence doc; authoritative totals confirmed from the per-project suite log:
  445 portable + 2 windows-ui + 2 desktop-e2e = 449 passed. The v0.4.2 collector fix is
  validated synthetically and will be exercised on the next physical R1 run.

## [Unreleased] — v0.4.1 physical Windows 11 R1 evidence and harness counter fix (2026-11-16)

### Added

- Sanitized R1 evidence from the first physical Windows 11 reference-desktop `release-full`
  run: **PASS** for approved commit `e7007af` (v0.4.0); preceding `developer-smoke` and
  `daily-smoke` runs also PASS; real Windows Named Pipes GUI E2E with three child processes,
  zero inbound listeners, clean worktree/cleanup.
- `docs/testing/evidence/2026-11-16-windows11-release-full.md`, recorded-runs table in the
  reference-desktop lifecycle doc and updated R1 status in the Windows 11 testing strategy.
- AW-039/AW-040/AW-041 marked done; AW-042 done for the v0.4 scope with tracked residual.

### Fixed

- `tools/windows-test/Run-AetherTests.ps1`: solution test runs wrote one TRX per project and
  the shared filename overwrote evidence, so `portable` counters under-reported (16 instead of
  445). Solution suites now keep per-project TRX files and the harness aggregates Counters
  across all of them; expected authoritative totals 445 + 2 + 2 = 449.
- Windows testing README: corrected SKIPPED_* taxonomy names, added the trusted-checkout
  `-RepoRoot` example, documented that the reference-desktop suite includes WT-04-01/02 and
  that TRX evidence is aggregated.

## [Unreleased] — v0.4.0 privacy, model routing/evaluation and Windows 11 test infrastructure (2026-11-15)

### Added

- Real provider configuration and adapters: `AETHER_MODEL_PROVIDER` selection, OpenAI-compatible
  and native **Google Gemini** `generateContent` transports; credentials only from the
  git-ignored `.env` (in-memory provider, never logged).
- Explicit **capability-based model router**: PUBLIC-only cloud path requiring enabled cloud +
  recorded consent + configured provider; INTERNAL/RESTRICTED/UNKNOWN never attempt cloud and
  fall back to local model/deterministic. Router/provider/model/reason exposed in shell output.
- **Model evaluation harness** (`tools/Aether.ModelEval`) + synthetic command corpus; first real
  smoke run: OpenAI `gpt-4o-mini` and Gemini `gemini-3.1-flash-lite` — 6/6 responses and 6/6
  syntactically valid plan shapes each; sanitized evidence committed.
- **Detector escalation** (AW-025): account/IBAN/email/phone/LEI/ISIN patterns,
  `ClassificationEscalator` never downgrades, detector failure ⇒ UNKNOWN fail-closed.
- **Session token vault** (AW-026): masking/de-masking, TTL, capacity/eviction, cancellation,
  thread safety, audit snapshots without raw tokens.
- **Windows 11 reference-desktop testing infrastructure** (AW-039–AW-042 scaffolding):
  published Reference Desktop run diagram, testing strategy docs, local PowerShell orchestrator,
  Windows Named Pipes GUI E2E project, CI Windows UI lane integration.
- v1.0 distribution policy recorded: public MIT sources, unsigned portable archives + SPDX SBOM +
  SHA-256 checksums; no installer/code signing planned.

### Fixed

- `.env` parser strips trailing `# comment` from unquoted values while preserving URL fragments.
- Gemini default model updated to an available cost-optimized tier after provider availability check.

## [Unreleased] — v0.3.2 AW-024 DACP runtime interop with FINOS Sail (2026-11-15)

### Added

- Test-only DACP interoperability harness (`tools/fdc3-interop/`) driving the declared
  FDC3 subset through `@finos/sail-headless-agent` 3.0.0-beta.1 (independent implementation,
  Apache-2.0): instrument/organization broadcasts with provenance-preserving metadata,
  ViewInstrument intent resolution and intent-result return — 5/5 checks.
- CI runs the pinned Node interop harness on every Linux build; machine-readable report
  uploaded as an artifact.
- AW-024 marked done for the practicable scope; issue #24 closed with evidence. v1.0
  distribution policy confirmed (public MIT, unsigned portable archives + SBOM + checksums).

## [Unreleased] — v0.3.1 AW-024 external FDC3 interop increment (2026-11-15)

### Added

- Official FDC3 3.0 context JSON Schemas vendored as pinned test fixtures (Apache-2.0,
  upstream commit `c6adda7a1f66d97d03809343852a4bfc0806f859`).
- Independent Python `jsonschema` validator (`tools/fdc3-interop/validate_fdc3.py`) and
  `.NET` contract tests validating all supported context payloads against the official
  schemas (7/7 payloads), plus provenance → FDC3 3.0 `ContextMetadata` projection.
- `docs/fdc3/interop-report.md`: conformance landscape, deviation register and the
  DACP/Sail headless agent plan for the next AW-024 increment.
- Model-selection rationale with cost-optimized defaults (OpenAI `gpt-4o-mini`,
  Gemini `gemini-2.5-flash-lite`, local `qwen2.5:3b`) in `.env.example` and
  `docs/evaluation/model-selection.md`.

## [Unreleased] — v0.3.0 bounded FDC3 interoperability + reliability closure (2026-11-15)

### Added

- FDC3-aligned bounded subset: `fdc3.organization` context type alongside
  `fdc3.instrument` (typed payloads, catalog validation, protobuf envelope, provenance).
- Bounded intent resolution (`ViewInstrument`, `ViewAnalysis`, `ViewChart`) through a new
  broker RPC and capability (`intent.resolve`): target apps must declare the intent and
  accept the required context type; unknown intents, context mismatches and unapproved
  explicit targets produce structured denials. App-directory subset exposed via snapshot.
- Contract fixtures (`tests/fixtures/fdc3/`), dedicated `Aether.Fdc3.Tests` suite and a
  multi-process FDC3 E2E test (organization context to all three apps + intent resolution).
- OS-level blocked-egress evidence: Linux isolated-network-namespace script and Windows
  Firewall-based check wired into CI; both must keep the reference workflow green with zero
  external egress attempts.
- Windows named-pipe IPC benchmark in Windows CI; WPF UI automation tests (FlaUI) in
  Windows CI; SBOM (SPDX) generation in CI and attached to releases.
- GitHub Releases per milestone, release workflow (`v*` tags) with binaries/source/SBOM/
  checksums, branch protection ruleset, issue dependency links/assignees and a Kanban project.
- `.env.example` template for OpenAI/Gemini/local model providers (local `.env` is
  git-ignored and only loaded explicitly with `--env-file`).

### Fixed

- README/limits/status updated for bounded FDC3 subset and reliability-closure evidence.

## [Unreleased] — v0.2 reliability hardening (2026-11-14)

### Added

- Offline headless mode (`--offline`): no cloud adapter is constructed, cloud flags are
  forced off, `--offline` + `--planner cloud` is rejected as a usage error, and provider
  health (`mode`, deterministic/local/cloud adapter availability, cloud flags) is reported
  in the structured result without any network I/O.
- Audit file rotation/retention for broker and shell (`--audit-max-bytes`, `--audit-files`):
  size-bounded JSONL rotation, bounded retention, verification across retained/rotated
  files, and continuation of the same hash chain across process restarts (invalid
  persisted chains are rejected fail-closed).
- Fault-injection coverage: `SIGSTOP`-hung sample app becomes `UNRESPONSIVE` and bounded
  restart recovers to `HEALTHY`; `SIGKILL`ed app is reported `EXITED` and restarts;
  shell fails fast with a structured error when the broker process dies; restart after
  crash keeps the reference workflow green.
- RPC-boundary retry/idempotency evidence: duplicate `clientMessageId` publishes are
  delivered once and return the original message id.

### Fixed

- Windows named-pipe client: `NamedPipeClientStream` transmission-mode properties were set
  before `ConnectAsync`, throwing `InvalidOperationException` (`Pipe handle has not been
  set`) on Windows. Connect first; the Windows CI named-pipe demo now passes. Found by the
  Windows CI job.
- Test portability on Windows: fake process-identity tests used non-canonical `/tmp/...`
  paths; the E2E harness now pins the cross-platform Unix-domain-socket transport, and
  app readiness/launch budgets were raised for cold CI runners.

### Known limitations

See [`docs/limitations.md`](docs/limitations.md). Windows reference-machine benchmarks,
OS-level blocked-egress capture, release artifacts, SBOM, model evaluation and docking
support are not included.
