<!-- Stable planning ID: AW-014 -->
# [AW-014] Implement minimal privacy gateway and private local summary

**Milestone:** `v0.1.0`  
**Labels:** `ai`, `privacy`  
**Dependencies:** `AW-005`, `AW-012`, `AW-013`  
**Requirement mapping:** NFR-04

## Outcome
Demonstrate a genuinely private route for restricted synthetic context.

## Implementation checklist
- [ ] Implement source-first classification and UNKNOWN fail-closed.
- [ ] Provide one local inference adapter with configurable model/endpoint.
- [ ] Block external provider calls for RESTRICTED and UNKNOWN, including retries.

## Acceptance criteria
- [ ] Private demo summary can run on documented local-AI hardware.
- [ ] Network test observes zero cloud AI calls for RESTRICTED/UNKNOWN.

## Verification evidence
Automated tests, review notes and a reproducible local demonstration.

## Scope boundaries
No real customer information, no unapproved cloud transmission, and no side effects outside the registered application contracts. Keep unrelated features in separate issues.
