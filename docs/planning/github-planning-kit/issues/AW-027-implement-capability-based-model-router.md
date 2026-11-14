<!-- Stable planning ID: AW-027 -->
# [AW-027] Implement capability-based model router

**Milestone:** `v0.4.0`  
**Labels:** `ai`, `architecture`  
**Dependencies:** `AW-014`, `AW-026`  
**Requirement mapping:** Milestone-specific functional scope

## Outcome
Select models based on task, availability, policy and hardware.

## Implementation checklist
- [ ] Define provider capability registry and local health probes.
- [ ] Implement routing with cloud opt-in, fail-closed restrictions and fallback matrix.
- [ ] Keep exact model IDs in config and document license metadata.

## Acceptance criteria
- [ ] Restricted tasks never route to cloud when local unavailable.
- [ ] Provider swap does not alter executor or broker code.

## Verification evidence
Automated tests, review notes and a reproducible local demonstration.

## Scope boundaries
No real customer information, no unapproved cloud transmission, and no side effects outside the registered application contracts. Keep unrelated features in separate issues.
