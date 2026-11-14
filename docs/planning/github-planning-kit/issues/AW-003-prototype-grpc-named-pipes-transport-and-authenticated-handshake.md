<!-- Stable planning ID: AW-003 -->
# [AW-003] Prototype gRPC/Named Pipes transport and authenticated handshake

**Milestone:** `M0`  
**Labels:** `spike`, `ipc`  
**Dependencies:** `AW-001`, `AW-002`  
**Requirement mapping:** BR-02, NFR-01

## Outcome
Test feasibility of the local broker transport and session binding.

## Implementation checklist
- [ ] Implement minimal gRPC/HTTP2 Named Pipe server and client.
- [ ] Evaluate current-user pipe ACLs and process identity binding.
- [ ] Benchmark 1 KB request/reply with trace IDs.

## Acceptance criteria
- [ ] Transport works across two independent processes.
- [ ] Document threat boundary and supported Windows build.

## Verification evidence
Automated tests, review notes and a reproducible local demonstration.

## Scope boundaries
No real customer information, no unapproved cloud transmission, and no side effects outside the registered application contracts. Keep unrelated features in separate issues.
