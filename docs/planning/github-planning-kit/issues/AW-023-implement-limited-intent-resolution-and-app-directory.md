<!-- Stable planning ID: AW-023 -->
# [AW-023] Implement limited intent resolution and app directory

**Milestone:** `v0.3.0`  
**Labels:** `fdc3`, `interop`, `security`  
**Dependencies:** `AW-021`, `AW-022`, `AW-008`  
**Requirement mapping:** Milestone-specific functional scope

## Outcome
Support bounded standard-intent workflows.

## Implementation checklist
- [ ] Add selected raise-intent and resolution flow.
- [ ] Map app-registry entries to declared App Directory subset.
- [ ] Ensure existing capability checks apply after resolution.

## Acceptance criteria
- [ ] Supported intent resolves to expected registered sample target.
- [ ] Unknown intent and unapproved target are rejected.

## Verification evidence
Automated tests, review notes and a reproducible local demonstration.

## Scope boundaries
No real customer information, no unapproved cloud transmission, and no side effects outside the registered application contracts. Keep unrelated features in separate issues.
