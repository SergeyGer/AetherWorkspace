<!-- Stable planning ID: AW-010 -->
# [AW-010] Build basic WPF Shell and top-level window arrangement

**Milestone:** `v0.1.0`  
**Labels:** `ui`, `windows`  
**Dependencies:** `AW-007`, `AW-009`  
**Requirement mapping:** UX-01

## Outcome
Provide a usable desktop without reparenting.

## Implementation checklist
- [ ] Create command/status toolbar and window registry.
- [ ] Implement open/focus/side-by-side arrangement for registered windows.
- [ ] Handle absent/closed window and cancellation.

## Acceptance criteria
- [ ] Three windows can be launched and arranged on reference desktop.
- [ ] Shell remains responsive when child process terminates.

## Verification evidence
Automated tests, review notes and a reproducible local demonstration.

## Scope boundaries
No real customer information, no unapproved cloud transmission, and no side effects outside the registered application contracts. Keep unrelated features in separate issues.
