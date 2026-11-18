# UI responsiveness measurement (v1.1 AW-066)

The WPF shell samples WPF UI-thread responsiveness (16 ms background probe of dispatcher queue latency) and command duration while a workflow runs and
exposes sanitized telemetry (`UiTelemetryText` AutomationId and an optional JSONL file). This is a
**measurement**, not a latency claim.

## Telemetry fields

- `frames` — sampled dispatcher queue-latency probes during the run (UI-thread responsiveness proxy; not a rendering/CompositionTarget claim); steady-state cycles that complete faster than a probe may report `sampleUnavailable`, but command/memory values are still recorded for all 100 cycles;
- `p50/p95/p99/max` heartbeat interval in ms; `long` = intervals over 50 ms;
- `command` — workflow duration in ms (from the shell result);
- `ws` — shell process working set MB; `gc` — managed heap MB at completion;
- trace id and planner/usedModel provenance are recorded by the optional JSONL writer
  (`AETHER_UI_TELEMETRY_FILE`).

The complete physical gate can be run in one command with
`tools/windows-test/Run-PhysicalReleaseGate.ps1`; the UI section can also be run standalone as shown
below.

## Reproduce on the physical Windows 11 reference laptop

```powershell
# 100-cycle reference measurement; writes results/ui-responsiveness/<stamp> files
pwsh tools/windows-test/Measure-UiResponsiveness.ps1 -Cycles 100
```

The runner checks interactive session, Explorer, disk space and records power state; failures return
`SKIPPED_ENVIRONMENT`/`FAIL_HARNESS`/`FAIL_PRODUCT` instead of a fake number. It builds the Windows
UI test project, runs `RepeatedWorkflowReportsUiTelemetry`, aggregates per-cycle p50/p95/p99 values
and writes `ui-responsiveness-<stamp>.json` plus the raw sanitized JSONL.

CI runs a 3-cycle smoke only. If the CI desktop cannot schedule dispatcher probes, the smoke records
`samplesUnavailable` and still passes the wiring check; the runner treats zero measured cycles as
`FAIL_HARNESS`, so no physical report can be based on a no-sample run. The standalone runner builds
the solution first (BrokerApp/headless sample apps are required). The authoritative AW-066 report requires the pinned physical profile:
AC power, unlocked session, 100% DPI baseline, no unrelated workload, same monitor topology.

## Reporting rules

- Publish p50/p95/p99 and memory trends with the device/profile fingerprint; never convert an IPC
  benchmark into a UI latency claim.
- Report FAIL/SKIP honestly if the environment is unsuitable; no “successful” measurement without a
  completed 100-cycle run.
- Raw telemetry stays local; only sanitized summaries/ledgers are committed.

## Pinned reference profile (recorded automatically)

Every report records OS caption/version/build, CPU model/cores/logical CPUs, RAM GB, GPU model/driver,
current display mode, session interactivity and power state. This makes a run attributable to the
reference laptop rather than an anonymous aggregate.

## Thresholds and regression policy

| Metric | Threshold | Rationale |
|---|---|---|
| Command p95 | ≤ 250 ms | local reference workflow; 2026-11-17 baseline was 12 ms |
| Command p99 | ≤ 2000 ms | permits a bounded first-run/GC outlier; baseline 962 ms |
| Frame/heartbeat p95 (of p95s) | ≤ 16 ms | dispatcher probe should stay sub-frame at p95 |
| Long intervals (>50 ms) | 0 | no UI-thread stall observed in the baseline |

The runner writes `thresholdViolations` and exits `7` (`FAIL_THRESHOLD`) when a threshold is crossed;
this is a regression signal, not a latency-benchmark claim. compare against the recorded baseline
rather than treating the thresholds as performance marketing. Never present the IPC benchmark as UI
latency.
