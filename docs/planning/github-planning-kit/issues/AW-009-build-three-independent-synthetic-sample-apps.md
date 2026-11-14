<!-- Stable planning ID: AW-009 -->
# [AW-009] Build three independent synthetic sample apps

**Milestone:** `v0.1.0`  
**Labels:** `samples`, `ui`  
**Dependencies:** `AW-002`, `AW-008`  
**Requirement mapping:** Milestone-specific functional scope

## Outcome
Create visible cross-process context propagation targets.

## Implementation checklist
- [ ] Build Watchlist WPF process, Risk WPF process and chart module.
- [ ] Include typed context handlers with source metadata.
- [ ] Generate deterministic synthetic holdings and summaries.

## Acceptance criteria
- [ ] Each sample app starts independently and renders fixture data.
- [ ] No actual market data integrations.

## Verification evidence
Automated tests, review notes and a reproducible local demonstration.

## Scope boundaries
No real customer information, no unapproved cloud transmission, and no side effects outside the registered application contracts. Keep unrelated features in separate issues.
