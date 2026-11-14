<!-- Stable planning ID: AW-006 -->
# [AW-006] Create benchmark and golden workflow harness

**Milestone:** `M0`  
**Labels:** `testing`, `benchmark`  
**Dependencies:** `AW-003`  
**Requirement mapping:** NFR-01

## Outcome
Establish reproducible baselines before performance claims.

## Implementation checklist
- [ ] Create stopwatch trace harness, fixture corpus and manual task script.
- [ ] Specify reference PC, p50/p95/p99 measurement protocol.
- [ ] Add smoke launch scenario to Windows CI.

## Acceptance criteria
- [ ] Benchmark methodology and raw schema are documented.
- [ ] Initial baseline recorded without unsupported performance claims.

## Verification evidence
Automated tests, review notes and a reproducible local demonstration.

## Scope boundaries
No real customer information, no unapproved cloud transmission, and no side effects outside the registered application contracts. Keep unrelated features in separate issues.
