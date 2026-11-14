<!-- Stable planning ID: AW-017 -->
# [AW-017] Add heartbeat, fault isolation and controlled restart

**Milestone:** `v0.2.0`  
**Labels:** `reliability`, `ui`  
**Dependencies:** `AW-015`, `AW-016`  
**Requirement mapping:** BR-05, NFR-08

## Outcome
Recover from expected child app failures.

## Implementation checklist
- [ ] Implement health states and missed-heartbeat rules.
- [ ] Provide restart action with bounded attempts.
- [ ] Inject app crashes, hangs and broker disconnects.

## Acceptance criteria
- [ ] Shell and Broker survive child crash tests.
- [ ] UI reports deterministic failure state and safe restart path.

## Verification evidence
Automated tests, review notes and a reproducible local demonstration.

## Scope boundaries
No real customer information, no unapproved cloud transmission, and no side effects outside the registered application contracts. Keep unrelated features in separate issues.
