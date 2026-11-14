<!-- Stable planning ID: AW-015 -->
# [AW-015] Automate MVP end-to-end workflow and quick-start demo

**Milestone:** `v0.1.0`  
**Labels:** `testing`, `documentation`, `release`  
**Dependencies:** `AW-010`, `AW-011`, `AW-012`, `AW-014`  
**Requirement mapping:** Milestone-specific functional scope

## Outcome
Publish a complete, repeatable vertical slice.

## Implementation checklist
- [ ] Script three-app synthetic workflow including denial test.
- [ ] Add mock-based Windows CI E2E and an optional local-model manual test.
- [ ] Write quick start, screenshots and MVP limitations.

## Acceptance criteria
- [ ] New checkout reproduces the workflow with tests.
- [ ] Acceptance screenshots and trace ID demonstrably correspond to same workflow.

## Verification evidence
Automated tests, review notes and a reproducible local demonstration.

## Scope boundaries
No real customer information, no unapproved cloud transmission, and no side effects outside the registered application contracts. Keep unrelated features in separate issues.
