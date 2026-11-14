<!-- Stable planning ID: AW-011 -->
# [AW-011] Implement typed context channels and trace propagation

**Milestone:** `v0.1.0`  
**Labels:** `interop`, `broker`  
**Dependencies:** `AW-008`, `AW-009`  
**Requirement mapping:** BR-03

## Outcome
Synchronize synthetic instrument context across apps.

## Implementation checklist
- [ ] Implement protobuf context broadcasts on a named default channel.
- [ ] Preserve message ID, trace ID, source and classification.
- [ ] Provide cross-process E2E assertion.

## Acceptance criteria
- [ ] Selecting an instrument in Watchlist updates two other apps.
- [ ] Every event has source and correlation metadata.

## Verification evidence
Automated tests, review notes and a reproducible local demonstration.

## Scope boundaries
No real customer information, no unapproved cloud transmission, and no side effects outside the registered application contracts. Keep unrelated features in separate issues.
