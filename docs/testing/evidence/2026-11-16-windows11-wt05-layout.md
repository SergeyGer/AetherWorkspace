# R1 evidence — WT-05 durable multi-monitor layouts (Windows 11 reference desktop, 2026-11-16)

Curated, sanitized evidence from the **physical Windows 11 reference laptop** (R1 lane) with the
second display connected in Extend mode. Raw logs/TRX and the full environment manifest remain
local (30-day retention); this page contains only non-personal data.

## Result

| Field | Value |
|---|---|
| Run id | `20261116-132414-release-full-0c78f7` |
| Status | **PASS** (exit code 0) |
| Approved commit | `491d4c210d0a113621111135800e904b4842f815` (v0.5 development) |
| Profile | `release-full` (portable + windows-ui + desktop-e2e + layout05) |
| Suites | `portable=PASS (476p/0f)`, `windows-ui=PASS (2p/0f)`, `desktop-e2e=PASS (2p/0f)`, **`layout05=PASS (4p/0f)`** |
| Totals | **484 passed, 0 failed, 0 skipped** |
| Monitors | 2 (Extend mode) |
| Inbound TCP listeners | 0 (local IPC only) |
| Cleanup | detached worktree removed, mutex released, no global display/firewall/environment changes |

## Reference display topology

| Screen | Primary | Position | Resolution | Bits |
|---|---|---:|---|---:|
| `\\\\.\\DISPLAY1` | yes | (0, 0) | 1920 × 1200 | 32 |
| `\\\\.\\DISPLAY2` | no | (1920, 0) | 3440 × 1440 | 32 |

## WT-05 scenarios executed by the `layout05` suite

| Test ID | Scenario | Result |
|---|---|---|
| WT-05-01 | Single-monitor arrangement / save-restore bounds inside visible screens | PASS |
| WT-05-02 | Save → arrange → restore without offscreen windows | PASS |
| WT-05-04 | Two-monitor restore to the saved secondary monitor (`DISPLAY2` 3440×1440) | PASS (real dual-monitor hardware) |
| WT-05-04 fallback | Persisted monitor disappeared → primary fallback on a present screen | PASS |
| — | Corrupt persisted layout → controlled `workspace.layout.invalid`, no window changes | PASS |

The dual-monitor scenario runs only on hardware with a second display; on the GitHub-hosted
single-screen Windows runner it logs SKIPPED_HARDWARE while the other layout scenarios still
assert (CI Windows UI lane green).

## Scoped boundaries

- WT-05-03 (resolution/topology change via display APIs) remains manual/approval-only in v0.5.
- WT-05-05 (DPI 100/125/150%), WT-05-06 (focus/keyboard/modal) and WT-05-10 (responsiveness
  profiling) still require device-side manual runs or the v0.5 Inspector/export work.
- WT-05-08 (Inspector/redacted export) is implemented with AW-032/AW-033.
- No window content or customer data is stored by the layout store; layout files are synthetic
  local configuration (monitor identity/geometry plus app/geometry bindings only).
