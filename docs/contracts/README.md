# Contracts and schemas

The broker is the only owner of the wire contracts; all components reference the
generated types and strict JSON models from `src/Aether.Contracts`.

| Contract | Runtime source of truth | Documentation schema / example |
|---|---|---|
| Broker RPC surface (`Register`, `Heartbeat`, `PublishContext`, `Subscribe`, `ResolveContext`, `ReportWindow`, `LaunchApp`, `StopApp`, `ArrangeWindows`, `GetSnapshot`, `RestartApp`, `HealthProbe`) | `src/Aether.Contracts/Protos/aether/v1/broker.proto` | — |
| Context envelope + window layout messages | `src/Aether.Contracts/Protos/aether/v1/envelope.proto` | `context-envelope.schema.json` (JSON projection for docs/tests) |
| Data classification + provenance | `src/Aether.Contracts/Protos/aether/v1/common.proto`, `src/Aether.Contracts/Models/DataClassification.cs` | — |
| ActionPlan | `src/Aether.Contracts/Models/ActionPlan.cs` (strict deserialization) | `action-plan.schema.json`, `action-plan.example.json` |
| App manifest / registry | `src/Aether.Contracts/Models/AppManifest.cs` | `app-manifest.schema.json` |
| Trusted policy document | `src/Aether.Security/Policy/PolicyDocument.cs` | `safe-policy.example.json` |
| Context catalog | `src/Aether.Contracts/Models/ContextCatalog.cs` | validated by `ContextCatalog.Load` (payload typed + classification) |
| FDC3 bounded context subset | `src/Aether.Contracts/Fdc3/*`, `Models/Contexts/*` | `fdc3.instrument`, `fdc3.organization` payloads validated by the catalog |
| FDC3 bounded intent subset | `src/Aether.Broker/Intents/IntentRouter.cs`, resolved via `ResolveIntent` RPC | declared in app manifest `intents`, validated against `Fdc3Intents.Supported` |

## Versioning rules

- Contracts carry a SemVer-compatible `schemaVersion` / `manifestVersion` / catalog
  schema version. The current build accepts **major version 1** and rejects other majors
  (`plan.schema.version_unsupported`).
- RPC protocol version is pinned in `AetherVersions.ProtocolVersion`; registration with
  an unsupported major is rejected.
- Unknown JSON members are rejected (`UnmappedMemberHandling.Disallow`). A contract
  change that adds a field requires a schema version review and tests; never silently
  accept unsupported directives.
- Documents are processed with bounded depth (`AetherJson.MaxDocumentDepth = 32`) and
  bounded payload sizes (`BrokerOptions.MaxPayloadBytes`, default 16 KiB).

## Notes on the JSON schemas

`docs/contracts/*.schema.json` are documentation/contract-test artefacts (validated by
`tests/Aether.Contracts.Tests`), while the runtime enforces the same rules through strict
typed deserialization plus deterministic validation in `Aether.Execution`. When you
change a model, update the schema and examples in the same change. The protobuf
definitions are the wire source of truth for the broker.

## Delivery and retry semantics

- Context delivery is **at-most-once** per subscriber; the broker reports explicit
  `deliveredCount`/`droppedCount`/`overflowed` accounting instead of retrying silently.
- Retries are allowed only for explicitly idempotent operations. The supported pattern is
  a `PublishContext` retry carrying the same client-supplied `clientMessageId` within the
  bounded dedupe window: the broker returns the original `messageId` and does not deliver
  the duplicate again. This is verified at the RPC boundary in
  `tests/Aether.Broker.Tests/FaultInjectionTests.cs`.
- `LaunchApp` is idempotent per app instance: an already-running allowlisted app is reused
  rather than spawned twice. `StopApp`/`RestartApp` are bounded operations with structured
  denials.
- Side effects are never retried automatically by the executor. A failed step stops the
  plan and triggers best-effort compensation for reversible steps; callers must re-run a
  new pipeline instance with a new trace id (the plan id remains deterministic for the same
  command).
