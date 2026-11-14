<!-- Stable planning ID: AW-005 -->
# [AW-005] Publish threat model, default policy and constraints

**Milestone:** `M0`  
**Labels:** `security`, `documentation`  
**Dependencies:** `AW-002`  
**Requirement mapping:** SE-03, NFR-04

## Outcome
Capture trust boundaries before AI integrations.

## Implementation checklist
- [ ] Model IPC impersonation, injection, cloud exfiltration and log disclosure.
- [ ] Define deny-by-default PUBLIC/INTERNAL/RESTRICTED/UNKNOWN policy.
- [ ] Set consent and capability rules; document residual risks.

## Acceptance criteria
- [ ] Threat model includes mitigations and residual limitations.
- [ ] UNKNOWN always denies cloud in policy unit tests.

## Verification evidence
Automated tests, review notes and a reproducible local demonstration.

## Scope boundaries
No real customer information, no unapproved cloud transmission, and no side effects outside the registered application contracts. Keep unrelated features in separate issues.
