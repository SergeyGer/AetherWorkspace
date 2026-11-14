<!-- Stable planning ID: AW-035 -->
# [AW-035] Validate generated schemas before acceptance

**Milestone:** `v0.6.0`  
**Labels:** `developer-tools`, `testing`  
**Dependencies:** `AW-034`  
**Requirement mapping:** Milestone-specific functional scope

## Outcome
Ensure generated artifacts are reproducible and syntactically safe.

## Implementation checklist
- [ ] Run protoc/JSON schema checks in isolated build step.
- [ ] Detect name collisions, breaking contract changes and forbidden paths.
- [ ] Store tests and explanation with generated diff.

## Acceptance criteria
- [ ] Invalid or breaking output is rejected with actionable diagnostics.
- [ ] Approved generated code passes contract tests.

## Verification evidence
Automated tests, review notes and a reproducible local demonstration.

## Scope boundaries
No real customer information, no unapproved cloud transmission, and no side effects outside the registered application contracts. Keep unrelated features in separate issues.
