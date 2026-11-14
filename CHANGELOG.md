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

### Known limitations

See [`docs/limitations.md`](docs/limitations.md). No release artifacts, SBOM, model
evaluation or docking support are included.
