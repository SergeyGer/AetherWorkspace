# ADR-002: gRPC/HTTP2 over named pipes in production and Unix domain sockets in development/CI

- **Status:** Accepted
- **Date:** 2026-11-14

## Context

The broker and clients are independent processes in one user session. They need typed
request/reply, server streaming, cancellation/deadlines, trace metadata and
authenticated sessions without a TCP port. Windows 11 is the reference desktop, where
named pipes are the intended local transport; development and CI run on Linux, where
the same protocol must still be exercised. The specification forbids a network listener
in the MVP (`_source/Technical_Specification.md` §4.1).

## Decision

- Define `aether.v1.BrokerService` in Protobuf over gRPC/HTTP2: register, heartbeat,
  publish/subscribe, resolve context, report window, launch/stop/restart, arrange,
  snapshot and health.
- On Windows, use a **named pipe** as the production/reference transport via Kestrel
  `ListenNamedPipe` (HTTP/2 only); the default pipe name is `aether-broker-<16 hex>` per broker start.
- On Linux, use a **Unix domain socket** via Kestrel `ListenUnixSocket` (HTTP/2 only),
  with a generated short path under the system temp directory.
- Select the platform default automatically (`named-pipe` on Windows,
  `unix-domain-socket` elsewhere), with explicit `--transport`/endpoint override.
- Keep one typed client (`Aether.Broker.Client/BrokerChannelFactory.cs`) and the same
  protobuf messages on both transports. The client uses `http://localhost` only as a
  gRPC pseudo-authority and installs a custom `ConnectCallback` for a
  `NamedPipeClientStream` or Unix `Socket`; no TCP connection is made.
- Publish endpoint, operator identity and short-lived operator token through a local
  handshake file (`BrokerEndpointInfo`), written atomically and user-only on Unix.
- Authenticate every RPC except `Register` and `HealthProbe` through session metadata in
  `BrokerAuthInterceptor`; privileged operations still re-check capability grants.

## Why no network listener

A localhost TCP listener is still a network listener: other local processes can reach
it, and proxy/firewall/port-forwarding mistakes can expose it. It needs port management
and weakens the local-only claim. Named pipes and Unix sockets are OS-local IPC
primitives, require no port and are permissioned by the OS. Keeping the endpoint off TCP
makes "no network listener" structural rather than a configuration promise. A remote or
multi-machine mode needs a separate design with TLS, authentication and deployment
hardening.

## Consequences

### Positive

- No TCP port, firewall rule, TLS certificate or listening URL is required; the broker
  never calls `ListenAnyIP`, `ListenLocalhost` or `UseUrls`.
- One contract and client cover Windows and Linux; only the connection factory and
  Kestrel listener differ.
- Streaming, deadlines, cancellation, metadata and gRPC status codes support
  subscriptions, heartbeats and structured denials.

### Negative

- Windows named-pipe execution is **compile-verified only** here; the Windows CI job is
  the configured runtime path, not evidence produced in this workspace.
- No explicit `PipeSecurity` ACL is configured; Kestrel and the OS user boundary apply.
  Same-user processes remain a weak isolation boundary, and the operator handshake
  token is reusable within a bounded budget.
- Unix socket paths are length-limited and need cleanup on abnormal exit; named-pipe
  names are guessable by same-user code (authentication still applies).

## Alternatives considered

- **localhost TCP gRPC** — introduces a network listener, port conflicts and external
  reachability; rejected.
- **WCF `NetNamedPipeBinding`** — Windows-only legacy stack with weaker
  protobuf/streaming fit; rejected.
- **Custom pipe framing / shared memory** — hand-written streaming and lifetime
  management, no gRPC semantics; rejected.
- **stdin/stdout per child or named pipes only** — neither supports a multi-client
  broker with a Linux CI substitute; rejected.

## Evidence

- `src/Aether.Contracts/Protos/aether/v1/broker.proto` (12 RPCs, streaming `Subscribe`).
- `src/Aether.Broker/BrokerHost.cs` (`ListenNamedPipe`/`ListenUnixSocket`, HTTP/2) and
  `src/Aether.Broker/Transport/BrokerTransportEndpoint.cs` (defaults/endpoints).
- `src/Aether.Broker.Client/BrokerChannelFactory.cs` (custom `ConnectCallback`, no TCP),
  `src/Aether.Contracts/Broker/BrokerEndpointInfo.cs` (handshake file permissions) and
  `src/Aether.Broker/Hosting/BrokerAuthInterceptor.cs`.
- `tests/Aether.Broker.Tests/SessionHandshakeTests.cs`,
  `tests/Aether.Broker.Tests/ChannelRouterTests.cs` and
  `tests/Aether.E2E.Tests/MvpWorkflowE2ETests.cs` exercise Unix sockets;
  `docs/BENCHMARKS.md` records that baseline.
- Windows named pipes are compile-verified through the solution build and configured in
  `.github/workflows/ci.yml`; they were not executed in this workspace.
