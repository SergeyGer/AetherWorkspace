<!-- Stable planning ID: AW-030 -->
# [AW-030] Optionally integrate owned-window docking after go decision

**Milestone:** `v0.5.0`  
**Labels:** `windows`, `spike`  
**Dependencies:** `AW-004`, `AW-017`  
**Requirement mapping:** Milestone-specific functional scope

## Outcome
Keep experimental window embedding isolated from core workflow.

## Implementation checklist
- [ ] Read AW-004 ADR and recheck Windows versions.
- [ ] If feasible, implement an opt-in owned-app contract and teardown fallback.
- [ ] Otherwise explicitly close as not planned and retain top-level layout.

## Acceptance criteria
- [ ] No third-party arbitrary HWND support claim.
- [ ] Focus/DPI/modal/crash tests pass or feature stays disabled.

## Verification evidence
Automated tests, review notes and a reproducible local demonstration.

## Scope boundaries
No real customer information, no unapproved cloud transmission, and no side effects outside the registered application contracts. Keep unrelated features in separate issues.
