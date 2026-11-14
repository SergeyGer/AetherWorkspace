# ADR-007: No network listener and no telemetry egress by default; local metadata-only audit

- **Status:** Accepted (mandatory defaults)
- **Date:** 2026-11-14
- **Deciders:** AetherWorkspace maintainers
- **Related:** [Architecture overview](../architecture/overview.md), [ADR-002](ADR-002-grpc-over-local-ipc.md)

## Context

The workspace handles classified context and must not leak it through implicit channels:
a listening port, analytics, crash reporting, remote logging or background provider
calls. The specification requires no network listener in the MVP and telemetry egress
disabled by default (`_source/Technical_Specification.md` §10.3). The project still needs
enough local evidence to debug a workflow without storing raw payloads.

## Decision

- The broker creates **no TCP listener**. `BrokerHost` configures Kestrel only with
  `ListenNamedPipe` (Windows) or `ListenUnixSocket` (Unix); there is no `ListenAnyIP`,
  `ListenLocalhost`, `UseUrls` or other network binding.
- The client never dials TCP: `BrokerChannelFactory` uses a custom `ConnectCallback` for
  the named pipe or Unix socket; `http://localhost` is a gRPC pseudo-authority only.
- No telemetry, analytics, crash-reporting SDK, remote logging or automatic update
  service is referenced or configured. The repository has no telemetry package and no
  telemetry endpoint setting.
- Provider calls are **not telemetry**: optional cloud and loopback local-model adapters
  are explicit, disabled by default, policy-gated and routed through `EgressGateway`.
  They are the only network egress in the codebase, and they are never triggered by the
  deterministic path.
- All diagnostic output is local: console output, structured result JSON, broker/shell
  audit JSONL and optional child-log files. Nothing is uploaded automatically.
- Audit is local and **metadata-only**: `AuditEvent` carries trace id, event type,
  outcome, error code, classification, provider category, byte counts, duration and
  app id — never raw prompts, payloads, account identifiers or credentials. The chain is
  SHA-256 (or optional HMAC-SHA256) hash-linked and can be verified with
  `AuditChainVerifier`; persistence is opt-in via `--audit-file`.
- Exports and support bundles are future work (v0.5); any export must be explicitly
  initiated and pass the same classification policy as provider egress.

## Consequences

### Positive

- Minimal implicit attack surface and data-flow paths: the only listening endpoint is an
  OS-local pipe/socket, and the only outbound path is explicit provider egress.
- Privacy properties are structurally reviewable and testable; tests can assert zero
  external attempts on denied paths.
- Metadata-only audit supports incident triage without creating a second sensitive-data
  store; tamper/gap detection is automated.
- The app works fully offline on the deterministic path.

### Negative

- No built-in remote support or diagnostics; users must share local artefacts manually.
- Audit is tamper-evident, not immutable: a local administrator can delete or truncate
  the JSONL file, and rotation/retention configuration is not implemented.
- There is no OS-level egress firewall; "no telemetry" is true by construction and
  package absence, not enforced by a network sandbox. A misconfigured loopback model or
  an operator-enabled cloud adapter can still make network calls.
- The Windows named-pipe path is compile-verified only here; the Windows CI job is the
  configured runtime evidence source.
- No performance/telemetry metrics are collected for product improvement, so prioritising
  work relies on tests and user reports.

## Alternatives considered

- **Opt-in telemetry or anonymous usage metrics** — deferred and not implemented; for this
  data, any accidental default would be a leak. Rejected for the baseline.
- **Remote crash reporting** — payload/stack-trace leakage risk; rejected.
- **Localhost HTTPS with authentication** — still a network listener and a larger surface;
  rejected.
- **Windows Event Log / ETW** — platform-specific and not needed for the reference
  workflow; would add a local sink without solving remote diagnostics; deferred.
- **No audit at all** — loses accountable execution evidence; rejected.

## Evidence

- `src/Aether.Broker/BrokerHost.cs` (Kestrel named-pipe/Unix-socket only) and
  `src/Aether.Broker.Client/BrokerChannelFactory.cs` (no TCP dial).
- `Directory.Packages.props` contains no telemetry/analytics package; no telemetry
  endpoint or SDK initialization exists in `src/`.
- `src/Aether.Security/Audit/AuditEvent.cs` and `AuditChain.cs` (metadata fields,
  hash chain, optional HMAC, JSONL store, verifier).
- `tests/Aether.Security.Tests/AuditChainTests.cs` (tamper/gap detection,
  metadata-only assertion), `tests/Aether.E2E.Tests/MvpWorkflowE2ETests.cs` (audit chain
  valid and no raw account identifier in broker audit),
  `tests/Aether.E2E.Tests/OfflineModeE2ETests.cs` (zero external attempts).
- `docs/limitations.md` documents the residual local-admin and loopback-model caveats.
