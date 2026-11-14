<!-- Stable planning ID: AW-032 -->
# [AW-032] Build Live Inspector with redacted trace timeline

**Milestone:** `v0.5.0`  
**Labels:** `observability`, `ui`  
**Dependencies:** `AW-020`, `AW-018`, `AW-031`  
**Requirement mapping:** Milestone-specific functional scope

## Outcome
Expose debugging without leaking content.

## Implementation checklist
- [ ] Create WPF drawer for action timeline, IPC timings and heartbeats.
- [ ] Provide drill-down by trace ID and compact histogram.
- [ ] Prohibit raw sensitive payload preview by policy.

## Acceptance criteria
- [ ] Reference workflow is inspectable by trace ID.
- [ ] Synthetic PII does not appear in export or UI detail.

## Verification evidence
Automated tests, review notes and a reproducible local demonstration.

## Scope boundaries
No real customer information, no unapproved cloud transmission, and no side effects outside the registered application contracts. Keep unrelated features in separate issues.
