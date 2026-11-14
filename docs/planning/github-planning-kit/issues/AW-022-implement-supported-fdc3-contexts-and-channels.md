<!-- Stable planning ID: AW-022 -->
# [AW-022] Implement supported FDC3 contexts and channels

**Milestone:** `v0.3.0`  
**Labels:** `fdc3`, `interop`  
**Dependencies:** `AW-021`, `AW-011`  
**Requirement mapping:** Milestone-specific functional scope

## Outcome
Extend context interoperability beyond a custom demo contract.

## Implementation checklist
- [ ] Create context mappings and validator tests.
- [ ] Implement selected user/app channel semantics.
- [ ] Propagate trace ID, timestamp and source metadata.

## Acceptance criteria
- [ ] Selected contexts round-trip without unsupported field loss.
- [ ] Source metadata is preserved across owned apps.

## Verification evidence
Automated tests, review notes and a reproducible local demonstration.

## Scope boundaries
No real customer information, no unapproved cloud transmission, and no side effects outside the registered application contracts. Keep unrelated features in separate issues.
