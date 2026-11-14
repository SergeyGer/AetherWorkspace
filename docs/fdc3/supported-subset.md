# FDC3 3.0 supported subset

**Status:** v0.3 work in progress (2026-11-14). This document states exactly what
AetherWorkspace implements against the FDC3 3.0 specification. It is a **declared
subset**, not a conformance statement: no blanket FDC3 compliance claim is made, and no
independent FDC3 implementation or official conformance suite has been run. AW-021 is
partial, AW-022 is partial, and AW-023/AW-024 are not started.

## Supported subset

| Surface | Status | What is implemented |
|---|---|---|
| Context type `fdc3.instrument` | **Supported (subset)** | Fields `type`, `name`, `id.ticker` and `id.isin`; class `src/Aether.Contracts/Models/Contexts/InstrumentContext.cs` |
| Context transport | **Supported** | Typed `ContextEnvelope` published and subscribed through the broker's `default` app channel; no direct app-to-app socket |
| Provenance | **Supported** | `sourceAppId`, `sourceInstanceId`, `traceId`, `spanId`, `createdAtUnixMs`, plus broker `messageId`, `schemaVersion`, `contextType`, `classification`, `channel`, timestamps |
| App channel | **Supported (one only)** | The configured workspace channel is `default` (`ChannelNames.Default`); no channel discovery/creation API |
| Context catalog | **Supported (workspace mechanism)** | Plans reference a trusted `contextRef`; the broker resolves and validates the payload from `samples/contexts/contexts.json` |
| Classification | **Supported (workspace mechanism)** | `PUBLIC`/`INTERNAL`/`RESTRICTED`/`UNKNOWN` on the envelope; `UNKNOWN` publication is denied |

The supported context is transported on the single named `default` app channel through
`Aether.Broker`; apps never exchange context directly. This is deliberately narrower than
the FDC3 broadcast model: publishing is **catalog-mediated** (an app/plan references a
trusted `contextRef`, and the broker supplies the payload), so arbitrary FDC3 context
objects cannot be injected through the plan or an app call.

```json
{
  "type": "fdc3.instrument",
  "name": "Northstar 2032 Bond",
  "id": { "ticker": "NRST", "isin": "SYN00000NRST2" }
}
```

The C# `InstrumentId` type also has optional `cusip`, `figi` and `bbg` properties. Those
are **not** part of the declared or tested subset; only `ticker` and `isin` are exercised
by the trusted catalog and tests. The `aether.exposure` context is a workspace-specific
type used for private summarization; it is not claimed as an FDC3 context.

## Unsupported surfaces (explicit)

- **Full FDC3 Desktop Agent API** (`window.fdc3`, the `@finos/fdc3` JavaScript API,
  app/user channel methods and listener registration).
- **Intents** — `raiseIntent`, `resolveIntent`, `addIntentListener`, intent metadata or
  intent-based application resolution.
- **User channels** — no user-channel join/leave, no user-channel context routing.
- **App channels beyond `default`** — no creation, discovery, joining or per-app channel
  selection. `default` is the only configured channel.
- **Private channels** — no private-channel creation, broadcast or listener flow.
- **Intent listeners / context listeners** in the FDC3 API sense. The project has a
  broker `Subscribe` stream, not an FDC3 listener object.
- **External FDC3 desktop-agent interoperability** — no bridging to any other desktop
  agent; no cross-agent context or intent exchange.
- **Full context type catalogue** — only the bounded `fdc3.instrument` subset above (plus
  the non-FDC3 `aether.exposure` workspace type).
- **FDC3 App Directory integration** — the trusted `samples/registry/apps.json` allowlist
  is not an FDC3 App Directory record and does not declare FDC3 app metadata.
- **FDC3 conformance/certification** — not tested, not claimed, not implied.

## Schema and version compatibility

- `InstrumentContext.ContextType` is `"fdc3.instrument"` and its declared schema version
  constant is `"1.0"`. The context payload itself is strict JSON with **unknown fields
  rejected** (`AetherJson.Strict`, `UnmappedMemberHandling.Disallow`, max depth 32).
- The protobuf envelope is package `aether.v1`
  (`src/Aether.Contracts/Protos/aether/v1/envelope.proto`); the broker sets
  `schema_version` to `"1.0"` (`AetherVersions.ContractSchemaVersion`).
- The broker protocol version is `"1"`; registration accepts only the integer protocol
  version 1 (`AetherVersions.ProtocolVersionMajor`). Any other value is rejected.
- The trusted context catalog declares `schemaVersion: "1.0"`; the loader accepts major
  version 1 and rejects unsupported majors, unknown context types, type/payload
  mismatches and `UNKNOWN` classifications.
- There is **no minor-version negotiation** and no FDC3 version negotiation. Unknown or
  unsupported fields/types are rejected rather than silently ignored. A breaking context
  or envelope change requires a new major version and a documentation/ADR update.
- Compatibility is within this repository's broker/client and its declared subset. It
  does not imply compatibility with other FDC3 3.0 contexts, agents or application
  directories.

## Delivery semantics and provenance

- `PublishContext` requires the session capability `context.publish`; `Subscribe`
  requires `context.subscribe`; the broker checks the session's allowed channels,
  context types and classifications.
- The broker constructs the envelope (message id, trace/span, timestamps, provenance)
  from the authenticated session; a publisher cannot forge `sourceAppId` or
  `sourceInstanceId`.
- Delivery is at-most-once per subscriber, FIFO per subscriber and bounded per channel
  (default capacity 64). Overflow is reported as `droppedCount`/`overflowed`; retries are
  idempotent only through the bounded client-message deduplication window.
- `RESTRICTED`/`UNKNOWN` handling is fail-closed: `UNKNOWN` cannot be published, a
  source classification cannot downgrade a trusted catalog classification, and
  subscribers without matching clearance do not receive the event.

## Evidence

| Test / path | What it demonstrates |
|---|---|
| `tests/Aether.Contracts.Tests/EnvelopeProtoTests.cs` | `ContextEnvelope` round-trips with channel, classification and provenance; `CLASSIFICATION_UNSPECIFIED` maps to `UNKNOWN` |
| `tests/Aether.Broker.Tests/RegistryTests.cs` | The trusted catalog loads `fdc3.instrument` and `aether.exposure`; unknown classifications, `UNKNOWN`, type mismatches and oversized payloads are rejected |
| `tests/Aether.Broker.Tests/ChannelRouterTests.cs` | Delivery preserves message id, trace id, source app id and classification; FIFO, clearance filtering, overflow and dedupe are covered |
| `tests/Aether.E2E.Tests/MvpWorkflowE2ETests.cs` | Three independent sample processes receive `fdc3.instrument` with the workflow trace id from one publish |
| `tests/Aether.Contracts.Tests/FixturePurityTests.cs` | Fixtures are marked synthetic and free of realistic PII; context classifications are explicit |
| `src/Aether.Broker/Hosting/BrokerGrpcService.cs` | Publish/subscribe enforcement of classification, channel, context type and provenance |

No external FDC3 fixtures, official schema conformance run or independent-agent interop
test exists. AW-024 remains open.

## What would change this claim

Adding intents, user channels, extra app channels, the Desktop Agent API, app-directory
mapping or external-agent interop would require new ADR/design review, dedicated tests
and an updated matrix. Independent interoperability evidence and review against the
official FDC3 3.0 specification are prerequisites for any broader compatibility claim.
Until then, this document and the code are the complete statement of FDC3 support: a
bounded, FDC3-aligned instrument context on one `default` channel through the broker,
with provenance — and no blanket conformance claim.
