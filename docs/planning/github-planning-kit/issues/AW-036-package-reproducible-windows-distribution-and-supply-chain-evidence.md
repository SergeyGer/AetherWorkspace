<!-- Stable planning ID: AW-036 -->
# [AW-036] Package reproducible Windows distribution and supply-chain evidence

**Milestone:** `v1.0.0`  
**Labels:** `release`, `security`  
**Dependencies:** `AW-015`, `AW-024`, `AW-029`, `AW-033`  
**Requirement mapping:** Milestone-specific functional scope

## Outcome
Ship a verifiable public build.

## Implementation checklist
- [ ] Choose distribution format and automate release artifact build.
- [ ] Publish hashes, SBOM, dependency/license inventory.
- [ ] Test clean Windows installation and uninstall.

## Acceptance criteria
- [ ] Artifact hashes match documented release.
- [ ] Clean-machine setup does not require private credentials.

## Verification evidence
Automated tests, review notes and a reproducible local demonstration.

## Scope boundaries
No real customer information, no unapproved cloud transmission, and no side effects outside the registered application contracts. Keep unrelated features in separate issues.
