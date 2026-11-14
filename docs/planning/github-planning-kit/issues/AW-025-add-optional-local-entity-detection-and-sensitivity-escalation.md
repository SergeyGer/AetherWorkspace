<!-- Stable planning ID: AW-025 -->
# [AW-025] Add optional local entity detection and sensitivity escalation

**Milestone:** `v0.4.0`  
**Labels:** `privacy`, `ai`, `testing`  
**Dependencies:** `AW-014`, `AW-020`  
**Requirement mapping:** Milestone-specific functional scope

## Outcome
Improve classification while keeping deterministic rules authoritative.

## Implementation checklist
- [ ] Integrate optional local NER/PII detector behind interface.
- [ ] Create synthetic adversarial entities and false-negative review.
- [ ] Prove detector can escalate but never downgrade trusted classification.

## Acceptance criteria
- [ ] Failure leads to UNKNOWN/RESTRICTED cloud denial.
- [ ] Publish precision/recall only with labeled dataset and setup.

## Verification evidence
Automated tests, review notes and a reproducible local demonstration.

## Scope boundaries
No real customer information, no unapproved cloud transmission, and no side effects outside the registered application contracts. Keep unrelated features in separate issues.
