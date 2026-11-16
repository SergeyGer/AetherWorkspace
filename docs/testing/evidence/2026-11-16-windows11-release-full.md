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

| Run | Profile | Status |
|---|---|---|
| `20261116-085503-developer-smoke-565c20` | `developer-smoke` | PASS |
| `20261116-085650-daily-smoke-99a6d4` | `daily-smoke` | PASS |
| `20261116-085803-release-full-ad5ae7` | `release-full` | PASS |

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

## Harness counter caveat and follow-up fix

The first release-full run reported `portable=PASS (16p/0f)` and `totals passed=20`. The run
itself executed and passed the whole portable suite, but the orchestrator read only one TRX
file: `dotnet test` on a solution writes **one TRX per test project**, and the shared
`--results-directory`/fixed filename caused overwrites. The suite status derivation used the
process exit code, so `PASS` is still valid, but the counters under-reported evidence.

Fix in this patch release: solution test runs now use per-project TRX files and the harness
aggregates `Counters` across all of them; the desktop/privacy test coverage and TRX collection
are recorded per suite. Authoritative expected totals on this commit: **445 portable + 2
windows-ui + 2 desktop-e2e = 449 passed**. A control `release-full` rerun after the fix is
requested to record the corrected counters.

## What this evidences (and what it does not)

- **Evidenced:** R1 release-gate PASS on physical Windows 11 hardware for the v0.4.0 approved
  revision; real Named-Pipes GUI workflow through the WPF shell with three sample processes;
  privacy/status assertions (WT-04-02 class-level `ReferenceDesktop` scenario); clean run
  isolation and no inbound listeners.
- **Not evidenced here:** multi-monitor/DPI-change scenarios (WT-05, v0.5), visual pixel
  comparisons, Windows 11 responsiveness profiling (v0.5), and the full WT-04 privacy matrix
  beyond the automated scenarios (portable CI covers the remaining policy assertions).
