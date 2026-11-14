# Roadmap

**Status:** proposed implementation plan, 14 November 2026. No milestone listed below should be interpreted as already shipped. Releases are outcome-gated, not date-gated.

| Milestone | Planned capability | Main Issues | Gate |
|---|---|---|---|
| **M0** | Feasibility & engineering foundation | AW-001–AW-006 | Clean build, IPC spike, HWND decision, policy baseline and benchmark method. |
| **v0.1.0** | MVP — three-app policy-aware workflow | AW-007–AW-015 | Demo runs end-to-end and enforces zero external calls on restricted/unknown test fixtures. |
| **v0.2.0** | Reliability & measured performance | AW-016–AW-020 | Backpressure, crash recovery, offline verification and audit evidence. |
| **v0.3.0** | Scoped FDC3 3.0 interoperability | AW-021–AW-024 | Declared subset with provenance and independent contract evidence. |
| **v0.4.0** | Advanced privacy & model evaluation | AW-025–AW-029 | NER/vault/routing guarded by egress regression and model performance report. |
| **v0.5.0** | Desktop UX & inspectability | AW-030–AW-033 | Multi-monitor restore and inspector; owned-app docking only if spike succeeded. |
| **v0.6.0** | Review-only contract generation | AW-034–AW-035 | Generated artifacts validated and reviewed before use. |
| **v1.0.0** | Public, reproducible release | AW-036–AW-038 | Clean install, SBOM/license review, docs, acceptance report and tagged build. |

## The one workflow that defines the MVP

1. A synthetic instrument command is parsed into a typed ActionPlan.
2. Deterministic validation and authorization precede execution.
3. The broker launches three registered independent sample processes.
4. An instrument context is propagated and top-level windows are arranged.
5. Sensitive/unknown content does not reach a cloud model; permitted private inference is local.
6. A trace ID ties the sequence together and failure cases are demonstrated.

## Deferred or optional
- Arbitrary third-party HWND embedding: **not in MVP**. Owned-app embedding is conditional on feasibility tests.
- Broad FDC3 coverage beyond the stated 3.0 subset: deferred.
- Contract/code generation: review-only tool in v0.6, no autonomous execution.
- Any production connectivity or customer data handling: explicitly outside this roadmap.

## How scope changes
Open a new issue with problem statement, acceptance criteria, test impact and security impact. Move work between milestones only with a documented dependency and updated release gate. Milestones are considered Released only when tagged artifacts and test evidence are available.
