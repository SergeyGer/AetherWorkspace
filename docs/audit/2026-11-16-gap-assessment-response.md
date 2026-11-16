# Response to the v1.0.0 specification-conformance gap assessment (2026-11-16)

Input: *AetherWorkspace_v1.0.0_Specification_Conformance_Gap_Assessment_EN.docx* — independent,
source-and-evidence inspection of `main` @ `c67c3eb` / release v1.0.0 (13 findings: 4 P0, 7 P1,
2 accepted). This page is the authoritative disposition for every finding. Corrective release:
**v1.0.1**.

## Disposition summary

| Finding | Priority | Disposition | Closure evidence |
|---|---|---|---|
| G-01 Release binaries lack the Windows WPF desktop application | P0 | **Fixed in v1.0.1 packaging** | release workflow `windows-desktop` job publishes `AetherWorkspace-<tag>-windows-x64-desktop.zip` (WPF Shell + 3 WPF sample apps + broker + registry/contexts/fixtures + launcher + README) and the `publish` job requires it; headless archive renamed `*-headless-binaries.zip` |
| G-02 Model evaluation: 6-case smoke vs ≥100-command / ≥95% accuracy, p50/p95/TTFT/RAM | P0 | **Accepted gap; planned v1.1** | scope decision: live-provider accuracy measurement is environment-dependent; v1.0.x claims only JSON-shape smoke on a small live sample (documented). Plan: versioned 100+ gold corpus, semantic plan-accuracy metric, repeats, p50/p95/TTFT/tokens/RAM/VRAM, actual-vs-estimated cost labels |
| G-03 Token vault not integrated into all provider paths (spec §7.2 INTERNAL cloud) | P0 | **Spec formally narrowed** by [ADR-009](../adr/ADR-009-public-only-cloud-policy.md) | v1.x cloud is PUBLIC-only; INTERNAL/RESTRICTED/UNKNOWN never reach a provider; vault retained as tested local primitive; conditional INTERNAL-cloud path excluded, not claimed |
| G-04 WT-04/WT-05 matrix incomplete (DPI, focus/modal, topology changes, UI responsiveness) | P0 | **Partially closed; explicit manual matrix** | dual-monitor WT-05-01/02/04 and fallback PASS on physical hardware; remaining WT IDs are marked manual/approval-only in [windows11-reference](../testing/windows11-reference.md) — not implied PASS. v1.1 plan: controlled DPI/focus/topology/responsiveness ledger |
| G-05 Contract studio is template-based, not AI-assisted/compiled | P1 | **Spec formally narrowed** by [ADR-010](../adr/ADR-010-template-based-contract-studio.md) | docs/Wiki/release notes call it a review-only, template-based studio; no AI-generation or compiled-output claim; apply requires explicit confirmation and validation gate |
| G-06 UI responsiveness/frame-time NFR lacks measurements | P1 | **Accepted gap; planned v1.1** | existing evidence remains IPC p50/p95/p99 + functional desktop runs; no frame-time claim. Plan: dispatcher heartbeat/frame-time telemetry, 100-cycle cancel/restart stress, pinned profile, percentiles/GC trends |
| G-07 FDC3 bounded subset, not full interop | P1 | **Accepted deviation** | supported-subset matrix + deviation register + official schema validation + FINOS Sail DACP checks remain published; no conformance claim |
| G-08 Stale status/limitations documents | P1 | **Fixed in v1.0.1** | `docs/IMPLEMENTATION_STATUS.md` and `docs/limitations.md` reconciled to released tags/evidence; new CI doc-consistency tests guard known stale assertions |
| G-09 R1 evidence predates v1.0.0 tag target | P1 | **Fixed in v1.0.1** | provenance record in the acceptance evidence: R1 `6ec0b9e`, tag target `c67c3eb`, docs-only diff (3 files), no code/test changes, CI on tag target green, no rerun required |
| G-10 Local model / credential-store operational lifecycle narrow | P1 | **Accepted gap; planned v1.1** | .env-based development secrets are documented; no Credential Manager integration and no local-model install/checksum/licence profile yet. Plan: one supported local profile + fresh-machine E2E + optional Windows Credential Manager adapter |
| G-11 Release verification lacked Windows packaged-usability gate | P1 | **Improved in v1.0.1; clean-machine operator check pending** | release pipeline now builds WPF artifacts and runs Windows UI/desktop smoke tests **before** publishing; checksums/SBOM verifier unchanged. Remaining: clean-Windows machine launch confirmation of the published bundle (operator step) |
| G-12 R1 artifacts curated, not independently replayable | P2 | **Fixed in v1.0.1 evidence transparency** | machine-readable acceptance manifest with run/suite/counter/environment/provenance and evidence-file hashes (raw TRX/logs remain local by privacy policy) |
| G-13 GitHub Project board omitted | P2 | **Accepted process deviation** | Issues are the work-unit source of truth; all completed milestone issues were closed with evidence; board remains optional |

## Clean-machine verification update (v1.0.1 bundle, 2026-11-16)

Operator verification on the physical Windows 11 reference machine:

- the published **Windows x64 desktop ZIP** downloaded with a matching SHA-256;
- **full-asset verification** (`Verify-Release.ps1` over desktop, headless, source and SPDX SBOM)
  returned **VERIFY PASS**;
- `Start-AetherWorkspace.ps1` started the packaged broker and WPF Shell successfully;
- the reference command opened the three real WPF sample windows, which then closed. Because the
  packaged workflow output was not captured, this is recorded as an **open defect**, not a fix:
  v1.0.2 adds `Test-AetherWorkflow.ps1` (packaged headless workflow with structured result),
  `Collect-Diagnostics.ps1`, WPF sample heartbeat/diagnostics, and real-WPF-sample CI regression
  tests (launch/context persistence and full GUI workflow), closing the headless-only coverage gap.

## Corrective-release acceptance evidence (v1.0.1)

- Tagged source SHA with the release pipeline producing **both** desktop and headless assets.
- `windows-desktop` release job: WPF publish + Windows desktop/UI smoke before publication.
- `Verify-Release.ps1` covers all v1.0.1 assets (desktop ZIP included) — VERIFY PASS recorded in release verification.
- Repository docs reconciled; doc-consistency test included in the portable suite.
- Provenance traceability record and machine-readable R1 manifest added.
- All remaining findings have an explicit disposition above; none is silently re-labeled as PASS.

## Known limitations after v1.0.1

The v1.0.1 corrective release fixes packaging and evidence-governance defects. It does **not**
claim to fix G-02/G-04/G-06/G-10 measurement/coverage gaps or to implement the original INTERNAL
cloud and AI/compiler contract-studio designs; those are explicitly de-scoped (ADR-009/ADR-010) or
planned for a future release. FDC3 remains a declared bounded subset and docking remains a non-goal.
