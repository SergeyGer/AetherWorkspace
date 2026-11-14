<!-- Stable planning ID: AW-034 -->
# [AW-034] Build review-only contract generation assistant

**Milestone:** `v0.6.0`  
**Labels:** `ai`, `developer-tools`  
**Dependencies:** `AW-024`, `AW-029`, `AW-032`  
**Requirement mapping:** Milestone-specific functional scope

## Outcome
Generate bounded drafts, never executable artifacts directly.

## Implementation checklist
- [ ] Create templates for proto, registry and typed client boilerplate.
- [ ] Constrain prompt to documented public/synthetic schemas.
- [ ] Require explicit diff review and disk write approval.

## Acceptance criteria
- [ ] Draft is marked untrusted and cannot change active registry.
- [ ] No auto-compilation or execution before explicit review.

## Verification evidence
Automated tests, review notes and a reproducible local demonstration.

## Scope boundaries
No real customer information, no unapproved cloud transmission, and no side effects outside the registered application contracts. Keep unrelated features in separate issues.
