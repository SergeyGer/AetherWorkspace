# FDC3 3.0 supported subset (declared, not conformance)

- **Status:** in-progress v0.3 item (AW-021/AW-022). This is a declared bounded-subset
  matrix for the implemented baseline; it must be reviewed against the official FDC3 3.0
  standard before publication as a v0.3 deliverable.
- **No conformance claim.** The project does not claim FDC3 compliance, Desktop Agent API
  coverage, intents or app-directory support beyond what is listed here.

## 1. What is implemented

| Surface | Status | Notes |
|---|---|---|
| Context data: `fdc3.instrument` (bounded subset) | **Supported** | Payload subset: `type`, `name`, `id.ticker`, `id.isin`. Other ID fields are not accepted in the v0.1 catalog payloads (strict JSON). |
| Additional context type used by the demo (`aether.exposure`) | **Supported (project-specific, not FDC3)** | Private synthetic exposure context used only for local summarization; never sent to providers. |
| App (named) channel semantics — single `default` channel | **Supported** | One allowlisted channel; per-app `allowedChannels` grants; bounded per-subscriber queues; at-most-once delivery. |
| Context broadcast with provenance | **Supported** | Envelope carries message id, trace/span ids, source app id + instance id, timestamp, schema version, classification, deadline. |
| Channel subscribe (server-streaming subscription) | **Supported** | Capability `context.subscribe`; classification/context-type clearance filtering per subscriber. |
| Context read/resolution for local processing | **Supported** | Capability `context.read`; clearance checked against the trusted catalog classification. |
| Typed schema validation for context payloads | **Supported** | Catalog payloads validated against strict typed models before publication. |

## 2. Explicitly unsupported (not implemented)

- Full **Desktop Agent API** (`desktopAgent` methods, `getOrCreateChannel`, `broadcast`,
  `addContextListener`, etc.).
- **Intents**: `raiseIntent`, `resolveIntent`, intent listeners, standard intent names,
  intent resolution UI. (AW-023, not started.)
- **User channels** (`fdc3.channel.*`, colour/global channels) and arbitrary app channels
  beyond the single `default` channel.
- **App Directory** integration or third-party directory interop; the project registry is
  local configuration only.
- **Private channels**, channel joining by third-party apps, external FDC3 desktop-agent
  interoperability, Desktop Agent Bridge.
- Full FDC3 context type catalogue; only the bounded instrument subset is accepted, and
  project-specific non-FDC3 contexts are used only locally.
- Any compatibility guarantee with third-party FDC3 applications; no independent
  implementation interop test has been executed (AW-024, not started).

## 3. Versioning and compatibility expectations

- Context envelope `schemaVersion` and context payload shapes are versioned; the current
  build accepts schema **major version 1** and rejects other majors.
- Broker protocol version is pinned; unsupported registration protocol majors are rejected.
- Unknown JSON fields are rejected rather than silently ignored, so adding FDC3 fields
  requires an explicit schema change and tests.

## 4. Provenance requirements (implemented)

Every context event carries:

- `messageId` — unique event id;
- `traceId` / `spanId` — end-to-end correlation including failures;
- `sourceAppId` / `sourceInstanceId` — authenticated app identity from the broker session;
- `createdAtUnixMs`, optional deadline;
- `classification` — resolved from the trusted catalog, never downgraded by publishers;
- `contextType` and validated typed payload.

## 5. Evidence and next steps

Evidence: `src/Aether.Contracts/Models/Contexts/InstrumentContext.cs`,
`src/Aether.Contracts/Protos/aether/v1/envelope.proto`,
`src/Aether.Broker/Channels/ChannelRouter.cs`, contract/broker/channel tests,
E2E provenance assertions (all three sample apps receive the same trace id).

Planned for v0.3 (not started): publish a reviewed subset matrix (AW-021), add remaining
supported contexts/channel semantics with contract tests (AW-022), limited intent
resolution + app directory mapping with capability checks (AW-023), and standards fixture
suites with independent interop evidence where practicable (AW-024). No claim should be
upgraded to "conformant" without that evidence.
