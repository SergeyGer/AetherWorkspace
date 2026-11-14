# ADR-006: Declared bounded FDC3 3.0 alignment rather than conformance

- **Status:** Accepted; subset implementation in progress (v0.3)
- **Date:** 2026-11-14
- **Deciders:** AetherWorkspace maintainers

## Context

The product needs contextual interoperability with FDC3 without overstating it. FDC3 3.0
is a broad standard covering a Desktop Agent API, app directory, intents, user/app/
private channels, a context-type catalogue and interop between independent agents. This
baseline has no independent FDC3 implementation to test against and no JavaScript
Desktop Agent runtime. The specification asks for a clearly declared, testable subset
and forbids a blanket conformance claim (`_source/Technical_Specification.md` §4.4).

## Decision

- Declare a **bounded FDC3 3.0-aligned subset**, not conformance. The authoritative
  matrix is [`docs/fdc3/supported-subset.md`](../fdc3/supported-subset.md).
- Implement exactly one FDC3 context type: `fdc3.instrument`, with the supported subset
  fields `type`, `name`, `id.ticker` and `id.isin`. The C# model also has optional
  `cusip`/`figi`/`bbg` properties, but those are not part of the declared or tested
  subset.
- Transport it as a typed `ContextEnvelope` on the single app channel named `default`
  through the broker. There are no user channels, no additional app channels and no
  private channels.
- Preserve provenance on every delivery: `sourceAppId`, `sourceInstanceId`, `traceId`,
  `spanId`, `createdAtUnixMs`, plus broker-generated `messageId`, `schemaVersion`,
  `contextType`, `classification`, `channel` and timestamps.
- Publishing is catalog-mediated: a plan references a trusted `contextRef`; the broker
  resolves the payload from the trusted context catalog. This is stricter than the FDC3
  broadcast API and is not presented as a general FDC3 publish/subscribe API.
- Do **not** implement or claim: the FDC3 Desktop Agent API/`window.fdc3`, intents,
  `raiseIntent`/`resolveIntent`, intent listeners, app directory, user/private channels,
  app channels beyond `default`, external desktop-agent bridging or the full context
  catalogue. Version compatibility is project-specific (context schema 1.0, protobuf
  package `aether.v1`).
- Treat external interoperability and standard review as v0.3 work (AW-021/AW-022
  partial; AW-023/AW-024 not started). No conformance, certification or compatibility
  claim is made.

## Consequences

### Positive

- The implemented context transport is meaningful, testable and honest about its limits;
  the matrix lists supported and unsupported surfaces explicitly.
- Provenance and classification travel with every context event, supporting traceability
  and fail-closed policy checks.
- The subset can grow with evidence (additional contexts, channels, intents) without
  rewriting the transport.

### Negative

- This is not a drop-in FDC3 agent: third-party FDC3 apps, intent resolution, user
  channels and Desktop Agent APIs do not work against it.
- Only one FDC3 context type (plus the non-FDC3 `aether.exposure` type) and one channel are supported; most of the catalogue and app-directory semantics are absent.
- No independent FDC3 implementation or official conformance suite has been run, so
  even the declared subset lacks external interoperability evidence.
- Users familiar with FDC3 may over-assume capability; the status and matrix must be
  prominent in the README and docs.

## Alternatives considered

- **Claim full FDC3 conformance** — unsupported and dishonest; rejected.
- **Custom context schema only, no FDC3 alignment** — loses the interoperability
  vocabulary and the design goal; rejected.
- **Adopt an external FDC3 Desktop Agent now** — adds a dependency and runtime that the
  local-only baseline cannot validate; deferred.
- **Implement intents/user channels before documenting the subset** — expands scope and
  delays the honest matrix; rejected by the v0.3 ordering.
- **Silently implement a partial API without a matrix** — invites false compatibility
  assumptions; rejected.

## Evidence

- `src/Aether.Contracts/Models/Contexts/InstrumentContext.cs` (subset fields and
  `fdc3.instrument` type), `src/Aether.Contracts/Models/ChannelNames.cs` (`default`).
- `src/Aether.Contracts/Protos/aether/v1/envelope.proto` and `common.proto` (envelope,
  classification, provenance); `src/Aether.Broker/Catalog/ResolvedContext.cs` (catalog
  validation and type checks).
- `src/Aether.Broker/Hosting/BrokerGrpcService.cs` (`PublishContext`/`Subscribe`,
  provenance and classification enforcement).
- `tests/Aether.Broker.Tests/ChannelRouterTests.cs`,
  `tests/Aether.Contracts.Tests/EnvelopeProtoTests.cs`,
  `tests/Aether.Broker.Tests/RegistryTests.cs` and
  `tests/Aether.E2E.Tests/MvpWorkflowE2ETests.cs` (three subscriber processes receive
  `fdc3.instrument` with the workflow trace id). No external FDC3 fixture or interop
  test exists.
