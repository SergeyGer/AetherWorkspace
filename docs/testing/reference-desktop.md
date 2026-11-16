# Reference Desktop: automated test run lifecycle

This is the published v0.4–v0.5 Windows 11 reference-desktop run process (Addendum A01 §7).
See the diagram [`reference-desktop-automated-test-run.png`](reference-desktop-automated-test-run.png).

| Stage | What happens | Safety rule |
|---|---|---|
| 01 Trigger | Manual launch or per-user Task Scheduler (“run only when user is logged on”, no SYSTEM, no wake/unlock) | local only; no inbound network |
| 02 Trust | Read a reviewed **full 40-hex SHA** from a local approved-revision file; verify it exists in the trusted checkout | unapproved SHA ⇒ `SKIPPED_UNAPPROVED_SHA`, never executed |
| 03 Preflight | single-instance mutex, unlocked interactive session, power/disk/RAM checks | locked/asleep ⇒ `SKIPPED_LOCKED`/`SKIPPED_ASLEEP`; never bypass Windows security |
| 04 Prepare | detached worktree from the approved SHA, pinned restore, Release build, isolated synthetic-data directory | no app state from previous runs |
| 05 Execute | start real Broker (Windows Named Pipes) + WPF Shell + three sample apps; run selected suite | only owned PIDs are tracked/closed |
| 06 Assert/Capture | semantic UIA assertions, geometry snapshot, trace correlation; redacted TRX/logs and app-only failure screenshots | no full-desktop capture; synthetic data only |
| 07 Cleanup | terminate only tracked PIDs, restore display state if changed, release mutex, remove worktree | cleanup runs even on failure |
| 08 Result | write `run-result.json` with PASS/FAIL/SKIPPED, manifest, SHA, counts and artifact references | artifacts stay local (ACL-restricted); curated summary may be published manually |

## What is implemented vs proposed

- **Implemented / in CI:** Windows-hosted FlaUI UI automation (`tests/Aether.Shell.UiTests`), new
  Windows Named Pipes GUI E2E project (`tests/Aether.Desktop.E2E.Tests`), portable security and
  privacy tests, OS-level blocked-egress checks, Git SHA validation in the local orchestrator
  script and its environment-manifest/run-result contracts.
- **Implemented, not yet executed on the physical laptop:** `tools/windows-test/Run-AetherTests.ps1`.
- **Proposed / pending:** full WT-04 physical run evidence, Task Scheduler registration,
  WT-05 layout/DPI/focus/Inspector suite, hardware/driver fingerprint publication.

## Evidence policy

Physical-device evidence is distinguished from GitHub Windows Server CI evidence in docs and
release notes. Raw logs/screenshots remain local; only curated synthetic summaries (pass/fail
counts, SHA, display profile, sanitized observations) may be added to a release report. Test
results that cannot run (locked session, missing hardware, no local model) are recorded as
skips with reasons — never as passes.

## Recorded reference-desktop runs

| Date | Commit | Device | Profile | Result |
|---|---|---|---|---|
| 2026-11-16 | `e7007af` (v0.4.0) | Windows 11 Pro build 26300, Intel Core Ultra 7 255H, 63.3 GB RAM, Intel Arc 140T, 1 monitor, 100% DPI | `developer-smoke`, `daily-smoke`, `release-full` | **PASS** (exit 0) |
| 2026-11-16 | `4a2796e` (v0.4.1) | same reference laptop | `release-full` control run | **PASS** (exit 0; per-project suite log confirms 445 portable + 2 + 2 = 449 passed; counter aggregation hardened in v0.4.2) |

Sanitized summary: [`evidence/2026-11-16-windows11-release-full.md`](evidence/2026-11-16-windows11-release-full.md).
The reference laptop is not a GitHub runner, runs only approved 40-hex SHAs, and all raw
artifacts stay local.
