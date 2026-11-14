# ADR-002: gRPC over local IPC, no network listener

- **Status:** Accepted
- **Date:** 2026-11-14

## Context

The shell and independent sample apps need a typed, versioned, cancellable and
deadline-aware transport with authenticated sessions. The specification mandates
gRPC/Protobuf over Windows named pipes and prohibits exposing a localhost network
listener in the MVP.

## Decision

- Define the broker surface in Protobuf (`src/Aether.Contracts/Protos/aether/v1/`).
- Host it with Kestrel/HTTP2: `ListenNamedPipe` on Windows, `ListenUnixSocket` on Unix.
- Share one typed client (`Aether.Broker.Client`) and identical service/session semantics
  on both transports. The Kestrel address is a pseudo-authority only; the OS local IPC
  primitive is the actual connection.
- Do not create any TCP listener. Do not expose the broker over a network interface.

## Consequences

Positive: strongly typed contracts, HTTP/2 flow control, deadlines, cancellation and
streaming for channel subscriptions; same code path on both transports.
Negative: named pipes cannot be exercised in this Linux development environment; the
Windows CI job is the runtime evidence. Same-user pipe access is not a security boundary
by itself, so process-bound sessions and capability checks are mandatory (ADR-004 /
threat model).

## Alternatives

Custom length-prefixed framing over pipes (rejected: no typed contracts, more bespoke
security-critical code), local HTTP listener (rejected: network exposure, larger attack
surface), Windows-only named pipes (rejected: would prevent deterministic Linux CI).

## Evidence

`src/Aether.Broker/BrokerHost.cs`, `src/Aether.Broker/Transport`,
`src/Aether.Broker.Client/BrokerChannelFactory.cs`, integration tests in
`tests/Aether.Broker.Tests`, E2E tests in `tests/Aether.E2E.Tests`.
