<!-- Stable planning ID: AW-016 -->
# [AW-016] Add bounded queues, ordering, backpressure and deduplication

**Milestone:** `v0.2.0`  
**Labels:** `broker`, `reliability`  
**Dependencies:** `AW-011`, `AW-012`  
**Requirement mapping:** BR-03, BR-04

## Outcome
Handle bursts and partial delivery failures explicitly.

## Implementation checklist
- [ ] Add per-channel queue bounds and overflow result.
- [ ] Define ordering guarantees and retry eligibility.
- [ ] Add cancellation/deadline and deduplication tests.

## Acceptance criteria
- [ ] Overload does not cause unbounded memory growth.
- [ ] Retry does not duplicate documented idempotent actions.

## Verification evidence
Automated tests, review notes and a reproducible local demonstration.

## Scope boundaries
No real customer information, no unapproved cloud transmission, and no side effects outside the registered application contracts. Keep unrelated features in separate issues.
