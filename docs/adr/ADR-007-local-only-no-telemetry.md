# ADR-007: Local-only by default; no network listener and no telemetry egress

- **Status:** Accepted (mandatory)
- **Date:** 2026-11-14

## Context

The workspace handles sensitive context and must not leak it through implicit channels
(network listeners, metrics, crash reporting, telemetry, background provider calls). The
specification requires no network listener in the MVP and telemetry egress disabled by
default.

## Decision

- The broker creates no TCP listener; it binds a named pipe (Windows) or Unix domain socket
  (Unix) and relies on current-user permissions plus session authentication/capabilities.
- No telemetry, analytics or remote logging is implemented or enabled by default.
- Audit is local, metadata-only and hash-chained; exports (planned v0.5) must be explicit
  user actions and pass the same data-classification policy as provider egress.
- Cloud provider use is an explicit, consented, policy-gated feature.

## Consequences

Positive: minimal implicit attack surface and data-flow paths; easy to reason about
privacy and to instrument in tests.
Negative: no built-in remote support/diagnostics; incident analysis relies on local
artefacts; operators must plan their own backup/retention (rotation is not implemented).

## Alternatives

Localhost HTTPS with auth (rejected: violates the no-listener constraint and broadens the
surface), anonymous telemetry with opt-out (rejected: opt-out is not a safe default for
this data), remote crash reporting (rejected: potential payload leakage).

## Evidence

`src/Aether.Broker/BrokerHost.cs` (Kestrel local-only configuration), README security
section, threat model, E2E tests (no network listener; egress counters at zero on denied
paths).
