<!-- Stable planning ID: AW-019 -->
# [AW-019] Verify offline-mode network isolation and failure behavior

**Milestone:** `v0.2.0`  
**Labels:** `privacy`, `testing`  
**Dependencies:** `AW-014`  
**Requirement mapping:** Milestone-specific functional scope

## Outcome
Distinguish private routing from genuine offline operation.

## Implementation checklist
- [ ] Add private mode UI and provider health status.
- [ ] Run with network egress blocked; test missing local runtime.
- [ ] Check no unauthorized fallback to cloud.

## Acceptance criteria
- [ ] Offline-supported commands run with egress disabled.
- [ ] Uninstalled model yields safe error without unexpected cloud call.

## Verification evidence
Automated tests, review notes and a reproducible local demonstration.

## Scope boundaries
No real customer information, no unapproved cloud transmission, and no side effects outside the registered application contracts. Keep unrelated features in separate issues.
