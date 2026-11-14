<!-- Stable planning ID: AW-012 -->
# [AW-012] Implement guarded ActionPlan validator and executor

**Milestone:** `v0.1.0`  
**Labels:** `execution`, `security`  
**Dependencies:** `AW-005`, `AW-007`, `AW-011`  
**Requirement mapping:** NFR-03

## Outcome
Do not let model output invoke arbitrary commands.

## Implementation checklist
- [ ] Define allowlisted LaunchApp/PublishContext/ArrangeWindows/SummarizeContext actions.
- [ ] Validate schema, authorization, action count and deadlines.
- [ ] Implement explicit states, cancellation and structured error handling.

## Acceptance criteria
- [ ] Valid plans execute deterministically.
- [ ] 100 documented unsafe plans produce no side effects.

## Verification evidence
Automated tests, review notes and a reproducible local demonstration.

## Scope boundaries
No real customer information, no unapproved cloud transmission, and no side effects outside the registered application contracts. Keep unrelated features in separate issues.
