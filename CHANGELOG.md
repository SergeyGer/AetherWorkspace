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
