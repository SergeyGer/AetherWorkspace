<!-- Stable planning ID: AW-002 -->
# [AW-002] Define versioned contracts and synthetic data fixtures

**Milestone:** `M0`  
**Labels:** `contracts`, `security`  
**Dependencies:** `AW-001`  
**Requirement mapping:** BR-06

## Outcome
Specify concrete message and plan contracts before implementation.

## Implementation checklist
- [ ] Write protobuf envelope, app manifest and ActionPlan JSON schemas.
- [ ] Define four classification labels and end-to-end provenance fields.
- [ ] Create three independent synthetic financial data fixtures.

## Acceptance criteria
- [ ] JSON examples validate; protobuf compiles from clean checkout.
- [ ] Fixtures contain no real personal or financial data.

## Verification evidence
Automated tests, review notes and a reproducible local demonstration.

## Scope boundaries
No real customer information, no unapproved cloud transmission, and no side effects outside the registered application contracts. Keep unrelated features in separate issues.
