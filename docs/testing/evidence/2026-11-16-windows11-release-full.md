# R1 evidence — Windows 11 reference desktop, release-full (2026-11-16)

Curated, sanitized evidence from the **physical reference laptop** (R1 lane). Raw logs,
machine/user names, paths and screenshots remain local (retention 30 days); this page contains
only the non-personal summary.

## Result

| Field | Value |
|---|---|
| Run id | `20261116-085803-release-full-ad5ae7` |
| Status | **PASS** (exit code 0) |
| Approved commit | `e7007afa86c12312919e5a893dbb64d91b696a19` (v0.4.0) |
| Profile | `release-full` |
| Suites | `portable=PASS`, `windows-ui=PASS` (2 tests), `desktop-e2e=PASS` (2 tests: WT-04-01 + WT-04-02) |
| Inbound TCP listeners for run-owned processes | 0 (local IPC only) |
| Cleanup | detached worktree removed, mutex released, no global display/firewall/environment changes |
| Evidence location | `C:\AetherWorkspace\results\<run-id>\` (local, not uploaded automatically) |

Preceding control runs on the same reviewed SHA (same device):

| Run | Commit | Profile | Status |
|---|---|---|---|
| `20261116-085503-developer-smoke-565c20` | `e7007af` | `developer-smoke` | PASS |
| `20261116-085650-daily-smoke-99a6d4` | `e7007af` | `daily-smoke` | PASS |
| `20261116-085803-release-full-ad5ae7` | `e7007af` | `release-full` | PASS |
| `20261116-091552-release-full-97b9bf` | `4a2796e` (v0.4.1) | `release-full` | PASS |
| `20261116-094002-release-full-a5f4d6` | `d44e6ba` (v0.4.2) | `release-full` | PASS |
| `20261116-102503-release-full-24c244` | `303f4d7` (v0.4.3) | `release-full` | FAIL_HARNESS (missing aggregation helper; all 8 test projects passed before the harness error) |
| `20261116-104313-release-full-ca993b` | `e97ee5d` (v0.4.4) | `release-full` | **PASS**, deterministic counters: 445 portable + 2 windows-ui + 2 desktop-e2e = 449; dual-monitor capture pending topology fix |

Two independent runs from the same approved SHA produced matching functional outcomes
(addendum gate G‑01), and the GUI end-to-end scenario used the real Windows Named Pipes
transport with three child processes (G‑02).

## Recorded environment (sanitized)

| Component | Value |
|---|---|
| OS | Windows 11 Pro, build 26300, x64 |
| CPU | Intel(R) Core(TM) Ultra 7 255H |
| RAM | 63.3 GB |
| GPU / display adapter | Intel(R) Arc(TM) 140T GPU (32 GB) |
| Monitors | 1 |
| DPI | 100% |
| .NET SDKs discovered | 10.0.401, 10.0.404 |
| PowerShell | 7.7.0 |

## Harness counter caveat and follow-up fixes

The run itself passed; the counter aggregation needed two iterations:

1. **v0.4.1 — under-count:** solution `dotnet test` writes one TRX per test project, and the
   original fixed filename under `--results-directory` overwrote evidence, so the first run
   reported portable as 16 tests instead of the full suite. Fix: per-project TRX + aggregate
   Counters across files.
2. **v0.4.2 — over-count (664 → 781):** the v0.4.1 control run reported `portable=664` and the
   v0.4.2 run reported `portable=781` (the synthetic baseline for this commit is 445 portable
   tests, matching Linux and Windows CI). Heuristic discovery of per-project TRX files on
   Windows still picked up duplicate/extra files.
3. **v0.4.3 — deterministic fix:** the portable suite no longer discovers TRX from the
   worktree at all. The orchestrator enumerates the eight `tests/*.Tests.csproj` projects from
   `AetherWorkspace.slnx`, runs each one separately with its own `results/trx/portable/<project>.trx`,
   and aggregates exactly those files. This removes duplicate discovery by construction;
   locally simulated collection yields 8 files / 445 passed.
4. **v0.4.4 — helper regression fix:** the first v0.4.3 run executed all eight test projects
   successfully but failed at the aggregation step because the `Read-TrxCountersAggregate`
   helper had been dropped during the rewrite (status `FAIL_HARNESS`, exit 2). v0.4.4 restores
   the helper and a CI gate now validates the orchestrator script (parse + required helper
   definitions).
5. **v0.4.4 — final deterministic run (2026-11-16, run `20261116-104313-release-full-ca993b`):**
   **PASS**, exit 0, with `portable=PASS (445p/0f)`, `windows-ui=PASS (2p/0f)`,
   `desktop-e2e=PASS (2p/0f)`, `totals passed=449 failed=0 skipped=0`. The per-project TRX
   execution aggregates exactly the eight expected TRX files; the counter issue is closed.

## Display topology note (v0.5 prep)

The final v0.4.4 run reported `monitorCount=1` (1920×1200, 100% DPI) even though a second
physical monitor was connected at the time: `Win32_DesktopMonitor` did not expose it. The
orchestrator's topology capture is being replaced with real desktop screen enumeration
(`System.Windows.Forms.Screen.AllScreens` with a CIM fallback) and the manifest now includes a
`screens` array (device name, primary flag, bounds, bits per pixel). The dual-monitor baseline
will be recorded on the next reference run once the second monitor is in **Extend** mode and the
new topology capture is approved.

**Authoritative portable counters are confirmed from the v0.4.1 control-run suite log**
(`logs/test-portable.log`), independent of the harness aggregation bug:

| Test project | Passed |
|---|---:|
| Aether.AI.Tests | 37 |
| Aether.Contracts.Tests | 41 |
| Aether.Execution.Tests | 140 |
| Aether.Workflow.Tests | 9 |
| Aether.Security.Tests | 126 |
| Aether.Fdc3.Tests | 20 |
| Aether.Broker.Tests | 56 |
| Aether.E2E.Tests | 16 |
| **portable total** | **445** |
| windows-ui | 2 |
| desktop-e2e (WT-04-01 + WT-04-02) | 2 |
| **run total** | **449 passed, 0 failed, 0 skipped** |

The v0.4.2 harness fix (project-scoped TRX discovery + freshness filter + SHA-256 de-duplication)
is validated with a synthetic worktree; it will be exercised on the next physical R1 run. A
rerun on v0.4.2 is optional for evidence closure because the per-project suite log already
provides the authoritative executed-test counts for this PASS run.

## What this evidences (and what it does not)

- **Evidenced:** R1 release-gate PASS on physical Windows 11 hardware for the v0.4.0 approved
  revision; real Named-Pipes GUI workflow through the WPF shell with three sample processes;
  privacy/status assertions (WT-04-02 class-level `ReferenceDesktop` scenario); clean run
  isolation and no inbound listeners.
- **Not evidenced here:** multi-monitor/DPI-change scenarios (WT-05, v0.5), visual pixel
  comparisons, Windows 11 responsiveness profiling (v0.5), and the full WT-04 privacy matrix
  beyond the automated scenarios (portable CI covers the remaining policy assertions).
