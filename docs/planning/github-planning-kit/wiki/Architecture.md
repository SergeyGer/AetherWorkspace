# Architecture

## Main components

| Component | Responsibility |
|---|---|
| Contracts | Versioned Protobuf envelopes, ActionPlan and app manifest schemas |
| Broker | gRPC over Windows Named Pipes, registry, launch, authorized sessions, bounded delivery |
| Execution | Deterministic plan validation, capability authorization and bounded state machine |
| Security | Source-first classification, fail-closed routing and network egress enforcement |
| AI adapters | Provider-agnostic plan generation and local/private summaries; never direct tool execution |
| Desktop Shell | Command bar, window arrangement, status and optional trace Inspector |
| Sample apps | Independent synthetic watchlist, risk and chart processes |

## Trust boundaries

1. Workspace text is untrusted input, not an instruction source.
2. Model output is an untrusted proposal; only authorized typed actions may execute.
3. Cloud egress is opt-in and deny-by-default; RESTRICTED and UNKNOWN never reach cloud models.
4. Same-user Windows identity alone does not establish application authorization.
5. Offline mode and true air-gapped deployment are different claims; the latter needs network-level evidence.

## Desktop integration
v0.1 arranges top-level windows; arbitrary external HWND reparenting is not supported. A separate feasibility spike is mandatory before any owned-app embedding.

## FDC3 compatibility
Target a **declared, tested subset of FDC3 3.0**. Do not claim full conformance until independent verification supports it.

## ADRs
See [[Decision-Log]].
