# GitHub issues closure before the v1.0.0 release gate (2026-11-16)

All open issues for completed milestones **M0 through v0.6.0** (AW-001…AW-035, AW-043…AW-046,
AW-030) were reviewed and closed with an evidence comment. Only the three **v1.0.0** issues remain
open: [#36 AW-036], [#37 AW-037], [#38 AW-038].

## Closure groups and evidence

| Issues | Scope | Evidence |
|---|---|---|
| AW-001…AW-006 | M0 foundations, contracts, transport spike, threat model, benchmark harness, docking feasibility | Solution/CI, versioned contracts, `docs/architecture/`, `docs/security/threat-model.md`, `docs/adr/`, benchmark harness |
| AW-007…AW-015 | v0.1.0 MVP vertical slice | v0.1.0 release notes, `docs/IMPLEMENTATION_STATUS.md`, `tests/`, `scripts/demo.sh` |
| AW-016…AW-020 | v0.2.0 reliability/observability | v0.2.0 release notes, benchmark records, fault-injection/audit tests |
| AW-021…AW-023 | v0.3.0 bounded FDC3 | v0.3.x notes, FDC3 schema validator + deviation register, `tests/Aether.Fdc3.Tests`, `docs/fdc3/supported-subset.md` |
| AW-025…AW-029 | v0.4.0 privacy/model routing/evidence | v0.4.0–v0.4.4 releases, `docs/evaluation/`, `docs/testing/evidence/`, security/AI suites |
| AW-030 | Owned-window docking | **Closed as not planned** (no go decision after AW-004); top-level arrangement retained |
| AW-031…AW-033 | v0.5.0 layouts, Inspector, diagnostics | v0.5.0 release; `docs/testing/evidence/2026-11-16-windows11-wt05-layout.md`; physical dual-monitor R1 PASS (491/491, layout05=4) |
| AW-034…AW-035 | v0.6.0 contract studio + validation gate | v0.6.0 release; `docs/testing/evidence/2026-11-16-v0.6.0-contract-studio.md`; contracts suite 63/63 |
| AW-043…AW-046 | v0.5.0 reference-desktop addenda (WT-05 suite, Inspector/export validation, scheduled-run hardening, acceptance matrix/gate docs) | WT-05 evidence page, `docs/testing/windows11-reference.md`, R1 environment manifests/run records, v0.5.0 release |

## Still open (v1.0.0 only)

- **#36 AW-036** — reproducible Windows distribution and supply-chain evidence (implemented;
  verification script + reproducibility guide, to be re-verified at final release).
- **#37 AW-037** — finalize public docs, Wiki and narrated demonstration (README/Wiki split,
  narrated demo and status pages published; acceptance pending).
- **#38 AW-038** — final release gate and acceptance report (final R1 run + v1.0.0 tag).

## Process notes

- Comments on each issue reference the corresponding release, evidence doc and test suite.
- No issue was closed without repository-visible evidence for its scope; scope boundaries and
  non-goals are recorded in the release notes / known limitations.
- The GitHub Project (Kanban) board remains deferred; issue state is the source of truth.
