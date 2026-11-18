# Windows 11 WT-04/WT-05 evidence matrix (v1.1 AW-065)

This is the per-ID reference-desktop ledger. Aggregate suite counters (e.g. “511 passed”) are **not**
a substitute: every ID below must be PASS, FAIL, SKIPPED_HARDWARE or a formally recorded exception.
Automated rows come from TRX/UI evidence; manual rows are filled only after a physical Windows 11
run with the documented procedure.

## Ledger

| ID | Scenario | Automation | Status / evidence |
|---|---|---|---|
| WT-04-01 | Named-pipe GUI reference workflow (real broker + WPF shell) | `GuiWorkflowRunsOverWindowsNamedPipes` | PASS (prior CI/R1 evidence; re-record per release) |
| WT-04-02 | Restricted-data workflow, no cloud egress | `RestrictedClassificationNeverShowsExternalProvider` | PASS (reference-full ledger; mapping corrected) |
| WT-04-03 | Shell connects and exposes stable AutomationIds | `ShellStartsConnectsToBrokerAndExposesCoreControls` | PASS |
| WT-04-04 | Real WPF sample processes/windows stay open | `GuiWorkflowWithRealWpfSampleAppsKeepsWindowsOpen` | PASS |
| WT-04-05 | Real WPF samples receive typed context | `WpfSampleAppsStayOpenAndReceiveContext` | PASS |
| WT-04-06 | Watchlist→Risk→Chart synchronization, no echo loop | `WatchlistSelectionSynchronizesRiskAndChartWithoutEchoPublish` | PASS |
| WT-04-07 | Repeated workflow stability (telemetry cycles) | `RepeatedWorkflowReportsUiTelemetry` | PASS (3-cycle CI smoke; 100-cycle physical run pending) |
| WT-04-08 | UI responsiveness telemetry emitted | `RepeatedWorkflowReportsUiTelemetry` | PASS (CI smoke; physical report pending) |
| WT-05-01 | Single-monitor save/restore within visible screens | `SaveRestoreKeepsAllWindowsOnVisibleScreens` | PASS |
| WT-05-02 | Save → arrange → restore without offscreen windows | `SaveRestoreKeepsAllWindowsOnVisibleScreens` | PASS |
| WT-05-03 | Display-configuration change / recovery | manual | **FORMAL_EXCEPTION** (2026-11-18; tracked in #120) |
| WT-05-04 | Dual-monitor restore to secondary display | `DualMonitorLayoutRestoresSecondaryMonitorPlacements` | PASS on physical dual-monitor hardware (CI logs SKIPPED_HARDWARE) |
| WT-05-05 | DPI 100/125/150% | manual | **FORMAL_EXCEPTION** (2026-11-18; tracked in #120) |
| WT-05-06 | Focus / keyboard / modal interaction | manual | **FORMAL_EXCEPTION** (2026-11-18; tracked in #120) |
| WT-05-07 | Missing monitor / topology fallback | `MissingMonitorFallsBackToAPresentScreen` | PASS |
| WT-05-08 | Inspector/export redaction privacy assertions | automated audit/redaction tests | **FORMAL_EXCEPTION** for the manual confirmation part (2026-11-18; #120) |
| WT-05-09 | Child crash/hang recovery on the desktop | fault-injection suites (Linux) + manual | **FORMAL_EXCEPTION** for the physical run (2026-11-18; #120) |
| WT-05-10 | UI frame/command latency profile | `RepeatedWorkflowReportsUiTelemetry` + `Measure-UiResponsiveness.ps1` | PASS — 100-cycle physical report 2026-11-18 |

## Physical procedure (manual rows only)

1. Preconditions: unlocked interactive session, AC power, 100% DPI baseline, no unrelated load;
   record OS build, CPU/GPU/RAM and monitor topology (sanitized).
2. **DPI 125% and 150%:** only with an approved rollback plan. Change the reference display scale,
   run the automated layout/UI suites, capture the per-WT result, then **restore 100%** and verify
   the baseline before leaving. Never change the primary display or scaling without this plan.
3. **Display topology / WT-05-03/07:** disconnect the second display, run dual-monitor restore
   (expected primary fallback, no offscreen windows), reconnect and restore the saved layout.
4. **Focus/keyboard/modal (WT-05-06):** keyboard-only run of the reference workflow, tab-order and
   control reachability, modal/dialog behaviour, then restore normal focus.
5. **Crash/hang (WT-05-09):** kill/stop an owned sample process during the workflow; broker health
   must recover deterministically without shell/broker crash.
6. Fill `wt-manual-ledger.example.json` with `status` (`PASS|FAIL|SKIPPED_HARDWARE|EXCLUDED`),
   evidence and notes, then run the ledger exporter below.

The physical gate runner (`tools/windows-test/Run-PhysicalReleaseGate.ps1`) invokes this exporter
automatically after the approved-SHA release suite. The standalone command below is for partial
re-runs or manual-ledger updates.

## Generate the ledger

```powershell
pwsh tools/windows-test/Export-WtLedger.ps1 `
  -ResultsDirectory C:\AetherWorkspace\results\<run-id> `
  -ManualLedgerPath .\wt-manual-ledger.json `
  -OutputDirectory ..\..\docs\testing\evidence\wt-ledger
```

Outputs `wt-ledger-<stamp>.json` and `.md`. See the v1.1 formal-exception register at
`docs/testing/evidence/2026-11-18-v1.1-wt-formal-exceptions.md`. Commit only sanitized ledgers; raw logs/screenshots stay
local. Formal exceptions require a release-scope decision recorded in the release notes, not a
silent PASS.
