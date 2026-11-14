<!-- Stable planning ID: AW-026 -->
# [AW-026] Implement session-scoped reversible token vault

**Milestone:** `v0.4.0`  
**Labels:** `privacy`, `security`  
**Dependencies:** `AW-025`  
**Requirement mapping:** Milestone-specific functional scope

## Outcome
Enable explicitly authorized sanitization without claiming anonymity.

## Implementation checklist
- [ ] Add volatile token mappings scoped to session and TTL.
- [ ] Reject unknown/unissued placeholders on response.
- [ ] Add cancellation cleanup and output rehydration guard.

## Acceptance criteria
- [ ] No raw sensitive values appear in cloud test doubles.
- [ ] Expired or foreign tokens cannot be rehydrated.

## Verification evidence
Automated tests, review notes and a reproducible local demonstration.

## Scope boundaries
No real customer information, no unapproved cloud transmission, and no side effects outside the registered application contracts. Keep unrelated features in separate issues.
