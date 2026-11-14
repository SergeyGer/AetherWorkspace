<!-- Stable planning ID: AW-031 -->
# [AW-031] Implement durable multi-monitor workspace layouts

**Milestone:** `v0.5.0`  
**Labels:** `ui`, `windows`  
**Dependencies:** `AW-010`, `AW-017`  
**Requirement mapping:** Milestone-specific functional scope

## Outcome
Improve repeatability and desktop recovery.

## Implementation checklist
- [ ] Persist layout with monitor identity and scale hints.
- [ ] Clamp restored windows to visible work areas.
- [ ] Test monitor removal, DPI changes and corrupted JSON.

## Acceptance criteria
- [ ] Layout recovers across two tested display topologies.
- [ ] No invisible/offscreen sample window after restore.

## Verification evidence
Automated tests, review notes and a reproducible local demonstration.

## Scope boundaries
No real customer information, no unapproved cloud transmission, and no side effects outside the registered application contracts. Keep unrelated features in separate issues.
