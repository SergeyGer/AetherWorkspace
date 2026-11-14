# ADR-005: Top-level window arrangement for the MVP; no reparenting or docking

- **Status:** Accepted; docking deferred and not evaluated
- **Date:** 2026-11-14

## Context

Cross-process window embedding (`SetParent`/`HwndHost`) is fragile across DPI awareness,
focus/keyboard handling, modal windows, UAC integrity levels, accessibility and teardown.
The specification gates it behind a separate feasibility spike and explicitly excludes it
from the MVP definition.

## Decision

- MVP arranges supported sample-app **top-level windows** only; it never reparents them.
- Layout is deterministic and computed by the broker (`LayoutPlanner`) within a configured
  work area; Windows applies it with `SetWindowPos` on handles reported by authenticated
  apps (`SWP_NOZORDER|SWP_NOACTIVATE`). Non-Windows uses a virtual arranger for CI.
- Cross-process docking/embedding remains out of scope. No docking spike was executed for
  this baseline and no docking claim is made.

## Consequences

Positive: avoids a high-risk Windows integration area, keeps windows independent and
crash-isolated, works with ordinary OS window management and multi-monitor later.
Negative: the visual experience is not a true embedded workspace; the WPF arrangement
path is only compile-verified in this environment and is runtime-verified by Windows CI.

## Alternatives

Immediate HWND embedding (rejected: unproven, violates MVP gating), WebView2-composed
workspace (rejected: would not interoperate with independent native processes), no window
management (rejected: the reference workflow explicitly includes arrangement).

## Evidence

`src/Aether.Broker/Windows/LayoutPlanner.cs`, `Win32WindowArranger.cs`,
`VirtualWindowArranger.cs`, layout tests, E2E layout assertions; limitations page.
