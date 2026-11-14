<!-- Stable planning ID: AW-004 -->
# [AW-004] Run cross-process docking feasibility spike

**Milestone:** `M0`  
**Labels:** `spike`, `windows`  
**Dependencies:** `AW-001`  
**Requirement mapping:** UX-01

## Outcome
Decide if HWND embedding deserves a later optional milestone.

## Implementation checklist
- [ ] Test two owned windows under DPI variations, modal dialogs and keyboard focus.
- [ ] Record crash, teardown and accessibility behavior.
- [ ] Write go/no-go ADR with top-level arrangement fallback.

## Acceptance criteria
- [ ] Spike report and reproducible samples are committed.
- [ ] No dependency on docking remains for MVP.

## Verification evidence
Automated tests, review notes and a reproducible local demonstration.

## Scope boundaries
No real customer information, no unapproved cloud transmission, and no side effects outside the registered application contracts. Keep unrelated features in separate issues.
