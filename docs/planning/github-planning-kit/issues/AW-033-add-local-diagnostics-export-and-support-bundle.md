<!-- Stable planning ID: AW-033 -->
# [AW-033] Add local diagnostics export and support bundle

**Milestone:** `v0.5.0`  
**Labels:** `observability`, `security`  
**Dependencies:** `AW-032`, `AW-029`  
**Requirement mapping:** Milestone-specific functional scope

## Outcome
Make reproducible incident reports possible.

## Implementation checklist
- [ ] Define sanitized JSON export with user-initiated confirmation.
- [ ] Add support bundle metadata and retention controls.
- [ ] Test filename/path escaping and export classification.

## Acceptance criteria
- [ ] Bundle includes reproducer metadata and no confidential values.
- [ ] Export off by default; no telemetry uploads.

## Verification evidence
Automated tests, review notes and a reproducible local demonstration.

## Scope boundaries
No real customer information, no unapproved cloud transmission, and no side effects outside the registered application contracts. Keep unrelated features in separate issues.
