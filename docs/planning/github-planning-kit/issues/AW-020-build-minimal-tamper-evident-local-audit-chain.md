<!-- Stable planning ID: AW-020 -->
# [AW-020] Build minimal tamper-evident local audit chain

**Milestone:** `v0.2.0`  
**Labels:** `security`, `observability`  
**Dependencies:** `AW-012`, `AW-014`  
**Requirement mapping:** SE-04, SE-05

## Outcome
Expose accountable, redacted execution outcomes.

## Implementation checklist
- [ ] Define structured audit schema and HMAC/hash-chain verification.
- [ ] Exclude prompt and raw PII, add retention settings.
- [ ] Document local-admin limitations and rotation semantics.

## Acceptance criteria
- [ ] Audit verifier detects a modified historical test entry.
- [ ] Secret and PII fixtures do not appear in logs.

## Verification evidence
Automated tests, review notes and a reproducible local demonstration.

## Scope boundaries
No real customer information, no unapproved cloud transmission, and no side effects outside the registered application contracts. Keep unrelated features in separate issues.
