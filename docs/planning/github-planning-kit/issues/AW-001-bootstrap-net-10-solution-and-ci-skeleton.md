<!-- Stable planning ID: AW-001 -->
# [AW-001] Bootstrap .NET 10 solution and CI skeleton

**Milestone:** `M0`  
**Labels:** `foundation`  
**Dependencies:** None  
**Requirement mapping:** NFR-06

## Outcome
Create a supported, reproducible Windows build baseline.

## Implementation checklist
- [ ] Create solution, global.json, Directory.Packages.props and editorconfig.
- [ ] Add src/tests/benchmarks/docs folders and Windows build workflow.
- [ ] Enable warnings-as-errors on owned code and unit-test discovery.

## Acceptance criteria
- [ ] Fresh Windows runner restores, builds and runs placeholder unit suite.
- [ ] No hardcoded secrets; exact SDK and tool versions documented.

## Verification evidence
Automated tests, review notes and a reproducible local demonstration.

## Scope boundaries
No real customer information, no unapproved cloud transmission, and no side effects outside the registered application contracts. Keep unrelated features in separate issues.
