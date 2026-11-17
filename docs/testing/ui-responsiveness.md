# UI responsiveness measurement (v1.1 AW-066)

The WPF shell samples WPF UI-thread scheduling intervals (16 ms dispatcher heartbeat) and command duration while a workflow runs and
exposes sanitized telemetry (`UiTelemetryText` AutomationId and an optional JSONL file). This is a
**measurement**, not a latency claim.

## Telemetry fields

- `frames` — sampled dispatcher heartbeat ticks during the run (UI-thread responsiveness proxy; not a rendering/CompositionTarget claim);
- `p50/p95/p99/max` heartbeat interval in ms; `long` = intervals over 50 ms;
- `command` — workflow duration in ms (from the shell result);
- `ws` — shell process working set MB; `gc` — managed heap MB at completion;
- trace id and planner/usedModel provenance are recorded by the optional JSONL writer
  (`AETHER_UI_TELEMETRY_FILE`).

## Reproduce on the physical Windows 11 reference laptop

```powershell
# 100-cycle reference measurement; writes results/ui-responsiveness/<stamp> files
pwsh tools/windows-test/Measure-UiResponsiveness.ps1 -Cycles 100
```

The runner checks interactive session, Explorer, disk space and records power state; failures return
`SKIPPED_ENVIRONMENT`/`FAIL_HARNESS`/`FAIL_PRODUCT` instead of a fake number. It builds the Windows
UI test project, runs `RepeatedWorkflowReportsUiTelemetry`, aggregates per-cycle p50/p95/p99 values
and writes `ui-responsiveness-<stamp>.json` plus the raw sanitized JSONL.

CI runs a 3-cycle smoke only. The authoritative AW-066 report requires the pinned physical profile:
AC power, unlocked session, 100% DPI baseline, no unrelated workload, same monitor topology.

## Reporting rules

- Publish p50/p95/p99 and memory trends with the device/profile fingerprint; never convert an IPC
  benchmark into a UI latency claim.
- Report FAIL/SKIP honestly if the environment is unsuitable; no “successful” measurement without a
  completed 100-cycle run.
- Raw telemetry stays local; only sanitized summaries/ledgers are committed.
