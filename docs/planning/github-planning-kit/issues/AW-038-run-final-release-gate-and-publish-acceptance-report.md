<!-- Stable planning ID: AW-038 -->
# [AW-038] Run final release gate and publish acceptance report

**Milestone:** `v1.0.0`  
**Labels:** `release`, `testing`  
**Dependencies:** `AW-036`, `AW-037`  
**Requirement mapping:** Milestone-specific functional scope

## Outcome
Close measurable requirements before version tag.

## Implementation checklist
- [ ] Run full E2E/reliability/egress suite and benchmark on reference setup.
- [ ] Document deviations and remaining non-critical risks.
- [ ] Create version notes and tagged-source acceptance matrix.

## Acceptance criteria
- [ ] All release-blocking requirements pass with evidence.
- [ ] No unverified compliance, performance or interoperability claim.

## Verification evidence
Automated tests, review notes and a reproducible local demonstration.

## Scope boundaries
No real customer information, no unapproved cloud transmission, and no side effects outside the registered application contracts. Keep unrelated features in separate issues.
