<!-- Stable planning ID: AW-007 -->
# [AW-007] Implement allowlisted app registry and process launcher

**Milestone:** `v0.1.0`  
**Labels:** `broker`, `security`  
**Dependencies:** `AW-002`, `AW-003`, `AW-005`  
**Requirement mapping:** BR-01

## Outcome
Broker discovers and launches only authorized demo apps.

## Implementation checklist
- [ ] Parse and validate app registry manifest.
- [ ] Enforce canonical executable paths and fixed argument templates.
- [ ] Provide launch timeouts and error results.

## Acceptance criteria
- [ ] Authorized sample app launches; path traversal/unknown app denied.
- [ ] No arbitrary path from model input is used.

## Verification evidence
Automated tests, review notes and a reproducible local demonstration.

## Scope boundaries
No real customer information, no unapproved cloud transmission, and no side effects outside the registered application contracts. Keep unrelated features in separate issues.
