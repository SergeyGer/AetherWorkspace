# Changelog

All notable changes to this reference implementation are recorded here. The project is
pre-release; version labels follow the implementation plan milestones. There is no
release tag yet.

## [Unreleased] — v0.1.0 vertical slice (2026-11-14)

### Added

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
