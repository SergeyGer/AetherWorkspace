<!-- Stable planning ID: AW-029 -->
# [AW-029] Add egress instrumentation and privacy adversarial regression

**Milestone:** `v0.4.0`  
**Labels:** `security`, `testing`  
**Dependencies:** `AW-025`, `AW-026`, `AW-027`  
**Requirement mapping:** NFR-04

## Outcome
Test real network requests, not only mocked provider functions.

## Implementation checklist
- [ ] Intercept and count external requests in controlled test environment.
- [ ] Exercise errors, retries, unknown classifications and injection fixtures.
- [ ] Publish threat-to-test traceability summary.

## Acceptance criteria
- [ ] Zero external AI calls in every RESTRICTED/UNKNOWN test case.
- [ ] All known egress paths are enumerated and monitored.

## Verification evidence
Automated tests, review notes and a reproducible local demonstration.

## Scope boundaries
No real customer information, no unapproved cloud transmission, and no side effects outside the registered application contracts. Keep unrelated features in separate issues.
