# Windows 11 reference-desktop testing strategy (v0.4–v0.5)

Derived from **Technical Specification / Addendum A01 “Windows 11 Reference Desktop Automated
Testing Strategy”** (15 November 2026). This document records how the repository implements
that strategy; the GitHub issues (AW-039–AW-046) are the planning artifacts, executed runs and
stored artifacts are the evidence.

## Design rule

The personal Windows 11 laptop is the authoritative physical desktop for GUI, DPI, focus and
actual Win32 layout evidence. It is **never** registered as a GitHub Actions self-hosted
runner, receives no inbound job commands and never runs unreviewed revisions. GitHub-hosted CI
remains the mandatory always-on regression gate.

## Test lanes

| Lane | Environment | Responsibilities | Evidence |
|---|---|---|---|
| L1 | GitHub Actions · ubuntu-latest | contracts, unit/security, FDC3/DACP, headless E2E, provider mocks, OS-level blocked egress, SBOM | TRX, logs, reports |
| W1 | GitHub Actions · windows-latest | WPF build, real Windows Named Pipes, headless workflow, named-pipe demo/benchmark, firewall blocked-egress, FlaUI UI automation, Named-Pipes GUI E2E | TRX, traces, artifacts |
| R1 | Physical Windows 11 x64 laptop, local interactive session | reference GUI, real win32 bounds, DPI/monitor changes, focus/keyboard, UI latency | local run manifest, redacted TRX/logs, app-only screenshots |
| H1 | existing Hetzner Linux VPS (optional) | long-running/headless exploratory runs | private logs |

GitHub’s `windows-latest` image is a Windows Server VM, not the reference Windows 11 desktop;
its UI evidence is independent but **non-authoritative** for desktop-support claims.

## Published lifecycle

The 8-stage run lifecycle is published as
[`reference-desktop-automated-test-run.png`](reference-desktop-automated-test-run.png) and
described in [`reference-desktop.md`](reference-desktop.md):
**Trigger → Trust → Preflight → Prepare → Execute → Assert/Capture → Cleanup → Result**.
Unapproved SHA, locked session or missing hardware always produces an explicit `SKIPPED_*`
result; Windows security is never bypassed.

## v0.4 scenarios (WT-04)

| ID | Scenario | Assertion |
|---|---|---|
| WT-04-01 | Reference workflow via WPF command bar over Named Pipes | three real sample processes, visible UI updates, allowed plan executes, trace correlation persists |
| WT-04-02 | RESTRICTED/UNKNOWN classifications | no external attempts; private route or safe denial; UI status reflects provider/denial |
| WT-04-03 | Detector failure/ambiguous content | escalate or remain UNKNOWN; fail-closed cloud path |
| WT-04-04 | Session token vault lifecycle | masking stays in session scope; expiry/cancel removes mappings; no raw tokens in logs |
| WT-04-05 | Cloud consent/provider health | cloud off by default; rejected route never falls back to cloud; unavailable local model fails safely |
| WT-04-06 | Adversarial plan injection | unsafe corpus denied before side effects |
| WT-04-07 | UI automation contract | stable AutomationIds, semantic patterns, explicit state waits |
| WT-04-08 | Evidence scrub/artifact minimization | screenshots/TRX exclude secrets and raw restricted payloads |

Automated coverage in this repository: WT-04-01/02/07 are scaffolded in
`tests/Aether.Desktop.E2E.Tests` (Windows-only, named-pipe transport, FlaUI). WT-04-03…06/08
are covered by portable CI tests + local run evidence; the full R1 physical run is pending and
must be recorded before claiming Windows 11 reference validation.

## Hardware profile (to record on the laptop)

CPU/GPU/RAM, Windows build, monitor count, resolution and per-monitor DPI, .NET SDK version,
power state, git SHA, suite version. Multi-monitor/DPI claims require matching available
hardware; otherwise the scenario is `SKIPPED_HARDWARE` ("implemented; not hardware-validated").

## Local run

See [`tools/windows-test/README.md`](../../tools/windows-test/README.md) and
`Run-AetherTests.ps1`. Typical flow: operator approves a reviewed full SHA in a local file,
runs the orchestrator manually or from a per-user Task Scheduler task (“run only when the user
is logged on”), and reviews the local `run-result.json` + redacted artifacts. Raw screenshots
and logs are never auto-uploaded to GitHub; only curated synthetic summaries may be committed.

## Failure taxonomy

`PASS` · `FAIL_PRODUCT` (deterministic assertion failure, blocks the gate) · `FAIL_HARNESS`
(infrastructure defect, never converted to pass) · `SKIPPED_LOCKED` / `SKIPPED_ASLEEP` ·
`SKIPPED_HARDWARE` · `SKIPPED_UNAPPROVED_SHA` · `INCONCLUSIVE`.

## v0.5 scope (next)

WT-05-01…10: single-monitor arrangement, layout save/restore, resolution change, two-monitor
(if hardware), DPI 100/125/150%, focus/keyboard/modal, child crash/hang recovery, Live
Inspector/redacted export, optional docking (only after a go decision) and UI responsiveness
on the reference hardware. Cross-process embedding is not asserted without a go decision.
