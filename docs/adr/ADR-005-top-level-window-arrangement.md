# ADR-005: Deterministic top-level window arrangement for the MVP; cross-process docking deferred and not evaluated

- **Status:** Accepted for the MVP; docking deferred, not evaluated
- **Date:** 2026-11-14
- **Deciders:** AetherWorkspace maintainers
- **Related:** [Architecture overview](../architecture/overview.md)

## Context

The reference workflow includes arranging the three sample windows side by side. True
cross-process docking (`SetParent`/`HwndHost` embedding) is fragile across DPI
awareness, keyboard focus, modal windows, UAC integrity levels, accessibility and
teardown, and the specification gates it behind a separate feasibility spike while
excluding it from the MVP (`_source/Technical_Specification.md` §9.2). This development
environment is Linux and has no Windows desktop test environment; no docking spike was
run for this baseline.

## Decision

- The only implemented window operation is **deterministic top-level arrangement**. No
  window is reparented, embedded or owned by another process.
- Registered apps report their own top-level window (`ReportWindow`) through their
  authenticated session. `WindowRegistry` accepts a window only when the app id and
  instance id match that session; bounds are sanity-checked, titles are bounded and the
  registry has a maximum size. Arbitrary third-party HWNDs are never accepted.
- `LayoutPlanner` computes a fixed `side-by-side` layout in a configured work area,
  ordered by the trusted manifest then app/instance/window id. The same inputs always
  produce the same rectangles.
- On Windows, `Win32WindowArranger` applies the layout with `SetWindowPos` using
  `SWP_NOZORDER | SWP_NOACTIVATE` and only for handles reported by authenticated
  registered apps. Other platforms use `VirtualWindowArranger`, which records the
  computed layout for deterministic CI/E2E assertions.
- Cross-process docking/reparenting is **explicitly deferred** and was **not
  evaluated**. There is no go/no-go spike report, no DPI/focus/accessibility evidence
  and no claim that owned-app or third-party docking works. Revisiting it requires a
  separate spike, ADR update and tests on real Windows hardware.

## Consequences

### Positive

- Avoids the highest-risk Windows integration area while still delivering the reference
  workflow's visible side-by-side layout.
- Independent windows remain crash-isolated; a failing sample cannot corrupt the shell
  or broker window tree.
- Layout computation is pure and unit-testable; the virtual arranger gives the
  Linux/headless suite deterministic geometry assertions.
- No third-party window injection surface: only authenticated registered apps can report
  handles or geometry.

### Negative

- The user experience is ordinary OS window management, not an embedded workspace; there
  is no shared title bar, tab or in-window hosting.
- Win32 `SetWindowPos` and WPF window reporting are **compile-verified only** here; real
  monitor/DPI/focus behavior is unverified, and the Windows CI job is the only configured
  runtime path.
- Only one layout (`side-by-side`) exists; multi-monitor persistence, monitor removal and
  DPI-change recovery are not implemented (v0.5 scope).
- A window that ignores or rejects placement may remain where it was; the code reports
  applied/placement counts, not an OS-level guarantee.

## Alternatives considered

- **`SetParent`/`HwndHost` embedding** — deferred; the specification requires a
  completed feasibility spike first, and none was executed here; rejected for the MVP.
- **Third-party window docking** — unsafe ownership/accessibility/UAC implications and
  no consent contract; rejected.
- **No window management** — the reference workflow explicitly includes arrangement;
  rejected.
- **Manual user arrangement** — non-deterministic and not automatable; rejected.
- **WebView2-composed workspace** — would not interoperate with independent native
  sample processes; rejected.

## Evidence

- `src/Aether.Broker/Windows/IWindowArranger.cs` (`SetWindowPos`, no reparenting),
  `LayoutPlanner.cs` and `WindowRegistry.cs` (authenticated ownership checks).
- `src/Aether.Broker/Hosting/BrokerGrpcService.cs` (`ArrangeWindows`),
  `src/Aether.Contracts/Models/WindowModels.cs` and
  `samples/registry/apps.json` (registered sample apps only).
- `tests/Aether.Broker.Tests/LayoutPlannerTests.cs` and
  `WindowRegistryTests.cs`; `tests/Aether.E2E.Tests/MvpWorkflowE2ETests.cs` asserts a
  three-placement `side-by-side` layout.
- No `SetParent`/`HwndHost` implementation or docking spike exists in `src/` or `tests/`;
  this absence is intentional and documented in `docs/limitations.md`.
