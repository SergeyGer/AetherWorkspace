<!-- Stable planning ID: AW-008 -->
# [AW-008] Implement broker registration and capability handshake

**Milestone:** `v0.1.0`  
**Labels:** `broker`, `security`  
**Dependencies:** `AW-007`  
**Requirement mapping:** BR-02, SE-03

## Outcome
Bind caller sessions to trusted sample app instances.

## Implementation checklist
- [ ] Design registration handshake and bounded session TTL.
- [ ] Verify process identity and map to approved capabilities.
- [ ] Deny spoofed identity and replayed session tokens.

## Acceptance criteria
- [ ] Authenticated sample apps register; negative tests denied.
- [ ] Every privileged call includes capability authorization.

## Verification evidence
Automated tests, review notes and a reproducible local demonstration.

## Scope boundaries
No real customer information, no unapproved cloud transmission, and no side effects outside the registered application contracts. Keep unrelated features in separate issues.
