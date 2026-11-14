<!-- Stable planning ID: AW-013 -->
# [AW-013] Add command bar and pluggable planning adapter

**Milestone:** `v0.1.0`  
**Labels:** `ai`, `ui`, `security`  
**Dependencies:** `AW-010`, `AW-012`  
**Requirement mapping:** Milestone-specific functional scope

## Outcome
Connect user command to typed plan without direct tool execution.

## Implementation checklist
- [ ] Support deterministic command parser for reference workflow.
- [ ] Add one optional configured cloud planning adapter for PUBLIC synthetic input.
- [ ] Make cloud opt-in and validate model outputs before executor.

## Acceptance criteria
- [ ] Reference command yields correct ActionPlan via deterministic path.
- [ ] Cloud mode requires explicit consent and denied payloads never reach provider.

## Verification evidence
Automated tests, review notes and a reproducible local demonstration.

## Scope boundaries
No real customer information, no unapproved cloud transmission, and no side effects outside the registered application contracts. Keep unrelated features in separate issues.
