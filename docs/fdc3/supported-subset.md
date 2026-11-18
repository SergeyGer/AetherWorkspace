# FDC3 3.0 supported subset (declared, not conformance)

- **Status:** v0.3 implementation for the tested scope (2026-11-15). This is a declared
  bounded subset; it is **not** an FDC3 conformance statement and no independent FDC3
  implementation or official conformance suite has been run.
- The authoritative evidence is the code and tests referenced below; claims without a
  test/measured baseline are not made.

## Supported contexts

| Context type | Status | Supported fields | Notes |
|---|---|---|---|
| `fdc3.instrument` | **Supported (subset)** | `type`, `name`, `id.ticker`, `id.isin` | Model also has optional `cusip`/`figi`/`bbg` properties, but only ticker/isin are exercised by fixtures/tests |
| `fdc3.organization` | **Supported (subset)** | `type`, `name`, `id.lei` | Synthetic issuer context used to demonstrate multi-type propagation |
| `aether.exposure` | **Supported (project-specific)** | workspace context for private summarization | Not claimed as an FDC3 context type |

Contexts are transported as a typed `ContextEnvelope` on the broker's single `default`
app channel. Publishing is **catalog-mediated**: apps/plans reference a trusted
`contextRef`, and the broker resolves/validates the payload from the trusted catalog
(`samples/contexts/contexts.json`) before delivery. Apps never exchange context directly.

## Supported intents (bounded resolution)

| Intent | Required context | Default target (first registry app declaring it) | Status |
|---|---|---|---|
| `ViewInstrument` | `fdc3.instrument` | `sample.watchlist` | **Supported (subset)** |
| `ViewAnalysis` | `fdc3.instrument` | `sample.risk` | **Supported (subset)** |
| `ViewChart` | `fdc3.instrument` | `sample.chart` | **Supported (subset)** |

Resolution rules implemented by the broker (`IntentRouter`):

- the caller session must hold the `intent.resolve` capability;
- only intents in the bounded set above are accepted;
- the referenced context must exist in the trusted catalog and match the intent's required
  context type;
- the target app must be registered, launchable, explicitly declare the intent and accept
  the context's type;
- an explicitly preferred target must itself satisfy those rules; otherwise the request is
  denied (`fdc3.intent.target_not_approved`).

Structured denial codes: `fdc3.intent.unsupported`, `fdc3.intent.context_mismatch`,
`fdc3.intent.target_not_approved`, `broker.context.not_found`, `auth.capability.missing`.

## App directory subset

`GetSnapshot` exposes a bounded app-directory projection: app id, display name,
launchable flag, capabilities, allowed channels/context types/classifications and declared
intents. There is **no** FDC3 directory federation, search API, icon/metadata catalogue or
third-party directory interop.

## Provenance and delivery

Every context event carries a unique `messageId`, `traceId`/`spanId`, authenticated
`sourceAppId`/`sourceInstanceId`, timestamp, schema version, classification and validated
typed payload. Intent resolution replies carry broker provenance (source `aether.broker`,
trace/span id, timestamp).

## Explicitly unsupported

- Full **Desktop Agent API** (`desktopAgent` methods, channel objects, listeners, adding
  context listeners with callbacks, etc.).
- Intents other than the three listed above; FDC3 **intent listeners**; intent resolution
  UI; raising intents from third-party apps.
- **User channels** (colour/global), arbitrary app channels beyond the single `default`
  channel, private channels, channel joining/discovery APIs.
- FDC3 **app directory** federation/interop and application metadata beyond the bounded
  snapshot projection.
- Full FDC3 context-type catalogue; the two supported context types are subsets.
- External FDC3 **desktop-agent interoperability**, Desktop Agent Bridge, conformance
  certification.
- A general-purpose FDC3 runtime or JavaScript adapter.

## WebView2 host bridge (v1.2, bounded)

The v1.2 WebView2 chart sample receives the same catalog-mediated `fdc3.instrument` contexts as the
WPF samples through a **host-side, strict JSON projection**:

- payload fields are `type`, `schemaVersion`, `ticker`, `name`, `classification`, `traceId` and the
  local synthetic series; unknown fields are rejected;
- only the bounded demo tickers `NRST`, `VEGA`, `ORIN` are accepted by the C# host and the local
  JavaScript renderer;
- JavaScript runs only from local `chart.html`/`chart.js` (no remote origin, host objects or network
  fetch), validates the same allowlist and acknowledges the render back to .NET;
- this is a presentation bridge for the demo chart, **not** a general FDC3 JavaScript adapter or a
  conformance claim. See [WebView2 chart sample](../demo/webview2-chart.md).

## Versioning / compatibility

- Envelope and payload schema versions: major version 1 accepted; other majors rejected.
- Broker protocol version is pinned; unsupported registration protocol majors rejected.
- Unknown JSON fields are rejected, so adding FDC3 fields requires explicit schema changes
  and tests.
- Intent names, context types and manifest `intents` declarations are validated against the
  bounded allowlist; unknown values reject the registry/intent request.

## Evidence

- Contracts/fixtures: `tests/fixtures/fdc3/contexts.json`,
  `tests/fixtures/fdc3/intent-scenarios.json`.
- Context contract tests (strict parsing, protobuf round-trip, catalog validation):
  `tests/Aether.Fdc3.Tests/ContextContractTests.cs`.
- Intent resolution and capability/denial scenarios: `tests/Aether.Fdc3.Tests/IntentResolutionTests.cs`.
- Multi-process E2E (organization context delivered to all three apps, intent resolution
  against a real broker process): `tests/Aether.E2E.Tests/Fdc3E2ETests.cs`.
- Broker-side implementation: `src/Aether.Broker/Intents/IntentRouter.cs`,
  `src/Aether.Broker/Hosting/BrokerGrpcService.cs`, `src/Aether.Contracts/Fdc3/*`.
- **External standards interoperability (AW-024 increment):** payloads are validated
  against the vendored official FDC3 3.0 JSON Schemas with an independent Python
  validator and with .NET contract tests; provenance maps to the FDC3 3.0
  `ContextMetadata` shape. Runtime interop: the declared contexts/intents are exercised
  through the independent FINOS **Sail headless** DACP Desktop Agent in
  `tools/fdc3-interop/` (5/5 checks, official DACP messages). See
  [interop-report.md](interop-report.md). Still **no FDC3 conformance claim**.

## Next steps (v0.3 gate closure / later)

- Review this matrix against the official FDC3 3.0 specification and record deviations.
- Add standards fixture suites, version-mismatch tests and, where practicable, an
  independent-implementation interop attempt (AW-024).
- No wording may be upgraded to "conformant" without that evidence.
