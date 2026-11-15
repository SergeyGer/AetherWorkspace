# AW-024 — external FDC3 interoperability increment (2026-11-15)

Status: **partial completion of AW-024**. This record covers the first practicable
external-interoperability increment: official-schema validation with an independent
validator plus a deviation register. It is **not** an FDC3 conformance or certification
claim, and direct third-party Desktop-Agent runtime interop is still pending.

## FDC3 conformance landscape (researched 2026-11-15)

- FDC3 3.0 shipped with a conformance program: three independent implementations achieved
  conformance — FINOS **FDC3 Sail** (open source), **interop.io io.connect Browser 5.0**
  (commercial) and **Morgan Stanley fdc3-web** (open-source library). Sources:
  [FINOS blog](https://www.finos.org/blog/fdc3-3.0-three-conforming-desktop-agents-and-a-new-chapter-for-interoperability),
  [FDC3 Sail](https://github.com/finos/FDC3-Sail),
  [fdc3-web](https://github.com/morganstanley/fdc3-web).
- The [FDC3 Conformance Framework](https://github.com/finos/FDC3-conformance-framework)
  tests implementations of the **JavaScript Desktop Agent API**: it is launched as a web
  app inside a desktop agent by loading the conformance App Directory
  ([conformance overview](https://fdc3.finos.org/docs/api/conformance/Conformance-Overview)).
- FDC3 3.0 also defines an **experimental Desktop Agent Communication Protocol (DACP)**
  — a JSON wire protocol between applications and a Desktop Agent
  ([DACP spec](https://fdc3.finos.org/docs/api/specs/desktopAgentCommunicationProtocol)).
  `@finos/sail-headless-agent` (3.0.0-beta.1) is a headless 2.2/3.0 DACP engine that can be
  used as an independent counterpart in a future adapter test.

**Why the conformance suite is not directly applicable here:** AetherWorkspace is not a
JavaScript/web Desktop Agent API implementation. Its bounded FDC3 surface is a policy-aware
.NET broker exposing context delivery on one channel and intent resolution via its own
authenticated gRPC API. Running the JS Desktop Agent conformance runner against this
architecture would require a DACP/Web-Connection-Protocol adapter that does not exist yet.
The honest claim remains "declared bounded subset", not "conformant".

## Increment 1 — official schema interoperability (done)

| Item | Implementation |
|---|---|
| Official artifacts | Vendored FDC3 3.0 JSON Schemas `context`, `instrument`, `organization` from `finos/FDC3` at pinned commit `c6adda7a1f66d97d03809343852a4bfc0806f859`; Apache-2.0 `LICENSE` and `NOTICE.md` with provenance (`tests/fixtures/fdc3/official/`) |
| Independent validator | `tools/fdc3-interop/validate_fdc3.py` uses Python `jsonschema` (different language/runtime/library than the product's .NET + JsonSchema.Net) to validate every supported fixture and trusted-catalog payload; emits a machine-readable report |
| Product-side tests | `tests/Aether.Fdc3.Tests/OfficialSchemaInteropTests.cs` validates the same payloads with JsonSchema.Net and asserts schema provenance/licence pinning |
| Metadata mapping | `Aether.Contracts.Fdc3.Fdc3MetadataProjection` maps broker-authenticated provenance to the FDC3 3.0 `ContextMetadata` shape: `source {appId, instanceId}`, authoritative ISO-8601 `timestamp`, `traceId` |
| Evidence report | `artifacts`/`.aether/fdc3-interop-report.json` produced by the validator in CI (schema commit, payload list, pass/fail) |

Measured result (independent validator): **7/7 supported payloads valid** against the
official schemas — 2 fixtures, 4 trusted-catalog payloads (`synthetic.instrument.northstar`,
`synthetic.instrument.vega`, `synthetic.organization.northstar`, `synthetic.organization.vega`)
and the metadata projection. No deviations in validated payload schemas were found.

Reproduce:

```bash
pip install 'jsonschema>=4.18,<5'
python3 tools/fdc3-interop/validate_fdc3.py --report .aether/fdc3-interop-report.json
dotnet test tests/Aether.Fdc3.Tests/Aether.Fdc3.Tests.csproj
```

## Increment 2 — deviation register (done for the declared surface)

| FDC3 3.0 surface | AetherWorkspace status | Deviation / rationale |
|---|---|---|
| Context data (`fdc3.instrument`, `fdc3.organization`) | Supported subset; payloads validated against official schemas | Models expose a tested subset of fields; extra standard fields are rejected by strict product models rather than passed through |
| App channels | Single `default` app channel, broker-mediated | No arbitrary/get-or-create channels; context is catalog-mediated and never peer-to-peer |
| User channels | Not implemented | No channel UI/join semantics; deliberate scope limit |
| Intents (`ViewInstrument`, `ViewAnalysis`, `ViewChart`) | Bounded resolution to declaring apps via `ResolveIntent` | Intent names and resolution rules are a declared allowlist; no `raiseIntent` API, resolver UI, intent listeners or result objects |
| App Directory | Snapshot projection (id/name/capabilities/intents) | No directory federation, no `getAppMetadata`/find APIs, no icons/schemas |
| Desktop Agent API | Not implemented (not a JS agent) | Conformance runner targets this API; requires a DACP/WCP adapter before any conformance attempt |
| DACP / Web Connection Protocol | Not implemented | Planned next increment (adapter + Sail headless agent interop) |
| Context metadata | Broker-authenticated source/timestamp/traceId mapped to the FDC3 `ContextMetadata` shape | We keep authoritative broker-authored metadata; app-provided metadata is not accepted from untrusted plans |
| Security & Identity (experimental) | Not implemented | Future work; explicitly experimental in the standard |

## Next increment to fully close AW-024

1. Implement a test-only **DACP adapter** (JSON messages; experimental protocol) exposing
   the bounded surface, or a Desktop Agent Bridge adapter.
2. Run `@finos/sail-headless-agent` (or another independent FDC3 implementation) as the
   counterpart and record an interaction: external app broadcasts context / raises a
   bounded intent that reaches our synthetic apps and back.
3. Re-run the deviation register; only then consider closing AW-024. Any "conformant"
   wording remains prohibited without the official conformance program.
