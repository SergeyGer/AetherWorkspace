<!-- Stable planning ID: AW-018 -->
# [AW-018] Measure IPC, UI responsiveness and resource limits

**Milestone:** `v0.2.0`  
**Labels:** `benchmark`, `testing`  
**Dependencies:** `AW-006`, `AW-016`  
**Requirement mapping:** NFR-01, NFR-02

## Outcome
Replace aspirational speed claims with evidence.

## Implementation checklist
- [ ] Run warmed 1 KB, three-app p50/p95/p99 benchmark.
- [ ] Record CPU/memory allocations and GC metrics.
- [ ] Document hardware, variance, instrumentation and regressions.

## Acceptance criteria
- [ ] BENCHMARKS.md contains raw reproducible protocol and result.
- [ ] Any target miss has issue/ADR, not a hidden exception.

## Verification evidence
Automated tests, review notes and a reproducible local demonstration.

## Scope boundaries
No real customer information, no unapproved cloud transmission, and no side effects outside the registered application contracts. Keep unrelated features in separate issues.
