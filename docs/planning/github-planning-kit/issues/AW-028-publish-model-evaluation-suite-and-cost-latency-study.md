<!-- Stable planning ID: AW-028 -->
# [AW-028] Publish model evaluation suite and cost/latency study

**Milestone:** `v0.4.0`  
**Labels:** `ai`, `benchmark`  
**Dependencies:** `AW-027`, `AW-006`  
**Requirement mapping:** Milestone-specific functional scope

## Outcome
Provide reproducible model-selection evidence.

## Implementation checklist
- [ ] Build ≥100 command labeled corpus and score exact plan correctness.
- [ ] Measure TTFT, end-to-end latency, tokens/sec and model footprint.
- [ ] Compare deterministic, small local and cloud strategies with equivalent tasks.

## Acceptance criteria
- [ ] Report includes hardware, model versions, uncertainty and raw outputs scrubbed.
- [ ] Selected model policy justified by evidence rather than brand.

## Verification evidence
Automated tests, review notes and a reproducible local demonstration.

## Scope boundaries
No real customer information, no unapproved cloud transmission, and no side effects outside the registered application contracts. Keep unrelated features in separate issues.
