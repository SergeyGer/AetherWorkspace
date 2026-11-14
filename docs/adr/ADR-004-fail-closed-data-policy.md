# ADR-004: Fail-closed classification and policy, denied-by-default cloud, explicit consent and one egress gateway

- **Status:** Accepted (mandatory control)
- **Date:** 2026-11-14
- **Deciders:** AetherWorkspace maintainers

## Context

Even synthetic financial-like workspace data must not leak to an external model by
default. The specification requires source-first classification, a deny-by-default
cloud policy, explicit user consent, `RESTRICTED`/`UNKNOWN` hard denial (including
retries and errors) and a single observable egress point (`_source/Technical_Specification.md` §7).
Detection is inherently imperfect, so absence of a positive classification must not mean
`PUBLIC`.

## Decision

- Use four labels: `PUBLIC`, `INTERNAL`, `RESTRICTED` and `UNKNOWN`. `UNKNOWN` has the
  highest sensitivity rank and is treated as restricted everywhere.
- `SourceFirstClassifier` starts from the trusted source label. Missing or malformed
  labels resolve to `UNKNOWN`; detectors may escalate to at least `RESTRICTED` but can
  never downgrade; a detector failure returns `UNKNOWN`.
- `PolicyDocument` must declare `failureMode: FAIL_CLOSED` and rules for all four labels;
  malformed rules reject the whole document instead of being partially trusted.
- Built-in policy: cloud disabled by default, explicit consent required, `PUBLIC`
  opt-in, `INTERNAL` deny-by-default, `RESTRICTED`/`UNKNOWN` deny. `UNKNOWN` is denied
  before rule evaluation; sanitized `INTERNAL` requires both policy opt-in and a review
  flag and is not implemented end-to-end.
- `EgressGateway` is the **single** wrapper around every provider attempt, including
  cloud, local and failure paths; a retry must be a new gateway call. It evaluates policy
  before invoking the transport, records metadata-only audit and exposes counters.
- `BrokerGrpcService` independently rejects `UNKNOWN` context publication and refuses to
  let a declared source classification downgrade a trusted catalog classification.
- No silent fallback: a missing local model returns a clear error; it does not trigger a
  cloud call. `SummarizeContext` mode is restricted to `private` in this baseline.

## Consequences

### Positive

- Denied `RESTRICTED`/`UNKNOWN` requests can be proven to make zero transport calls;
  the gateway's `externalAttempts` counter and audit events are the observable evidence.
- Policy is deterministic, versioned (`3.0`), locally inspectable and testable without a
  live provider.
- Classification cannot be downgraded by a detector or plan text; derived outputs
  inherit or increase sensitivity, and the fail-closed document check rejects malformed
  policy.

### Negative

- Detection is rule-based only (synthetic account IDs, email-like and IBAN-like
  patterns); there is no PII guarantee or precision/recall claim, and the optional NER
  escalation and token vault are v0.4 backlog.
- Cloud functionality is intentionally limited; sanitized `INTERNAL`, tokenization and
  model routing are not implemented. Live cloud/local model behavior is not validated
  here (test doubles only).
- A loopback local-model endpoint is not automatically air-gapped; network behaviour
  must be verified. Consent UI is a checkbox/CLI flag, the policy file is user-editable,
  and audit is tamper-evident, not immutable.

## Alternatives considered

- **Cloud enabled by default with warnings** — violates the baseline and makes leakage
  the default; rejected.
- **Classify only at the egress gateway** — too late to prevent payload construction or
  logging; source-first classification is required; rejected.
- **Rely on provider-side retention controls or trust the source label alone** — neither
  is observable/enforceable here, and detectors must escalate contamination; rejected.
- **Ad-hoc per-call HTTP checks instead of one gateway** — impossible to prove absence of
  egress; rejected.

## Evidence

- `src/Aether.Contracts/Models/DataClassification.cs` (rank/`UNKNOWN` fail-closed),
  `src/Aether.Security/Classification/SourceFirstClassifier.cs` and
  `SensitivePatterns.cs`.
- `src/Aether.Security/Policy/PolicyDocument.cs`, `PolicyEngine.cs`,
  `src/Aether.Security/Egress/EgressGateway.cs` and
  `docs/contracts/safe-policy.example.json`.
- `src/Aether.AI/Planner/CloudPlanProvider.cs`,
  `src/Aether.AI/Summarization/CloudSummaryProvider.cs`,
  `LocalModelSummaryProvider.cs` and `SummaryService.cs` (private routing).
- `src/Aether.Broker/Hosting/BrokerGrpcService.cs` (`PublishContext` deny/downgrade
  checks) and `src/Aether.Broker/Catalog/ResolvedContext.cs` (trusted catalog).
- `tests/Aether.Security.Tests/PolicyEngineTests.cs`,
  `tests/Aether.Security.Tests/EgressGatewayTests.cs`,
  `tests/Aether.AI.Tests/PrivacyRoutingTests.cs`,
  `tests/Aether.AI.Tests/CloudPlanProviderTests.cs`,
  `tests/Aether.E2E.Tests/OfflineModeE2ETests.cs` and
  `tests/Aether.E2E.Tests/DenialE2ETests.cs`.
