<!-- Stable planning ID: AW-024 -->
# [AW-024] Validate FDC3 subset with contract and external tests

**Milestone:** `v0.3.0`  
**Labels:** `fdc3`, `testing`  
**Dependencies:** `AW-022`, `AW-023`  
**Requirement mapping:** Milestone-specific functional scope

## Outcome
Back interoperability claims with evidence.

## Implementation checklist
- [ ] Create standards fixture suites with provenance checks.
- [ ] Test version mismatch and unsupported behavior.
- [ ] Attempt independent implementation interop where practicable.

## Acceptance criteria
- [ ] Conformance matrix contains pass/fail/unsupported evidence.
- [ ] No unsupported behavior silently masquerades as supported.

## Verification evidence
Automated tests, review notes and a reproducible local demonstration.

## Scope boundaries
No real customer information, no unapproved cloud transmission, and no side effects outside the registered application contracts. Keep unrelated features in separate issues.
