# ADR-004: Fail-closed data classification and explicit-consent cloud egress

- **Status:** Accepted (mandatory)
- **Date:** 2026-11-14

## Context

Enrichment with cloud models creates a data-exfiltration risk. The specification requires
source-first classification, deny-by-default cloud policy, explicit user consent, and no
cloud transmission of RESTRICTED/UNKNOWN data, including retries, logs and telemetry.

## Decision

- Four labels: `PUBLIC`, `INTERNAL`, `RESTRICTED`, `UNKNOWN`. A trusted source label sets
  the minimum; deterministic detectors may escalate but never downgrade; missing labels
  fail closed to `UNKNOWN`, treated as restricted.
- Built-in policy: cloud disabled by default, explicit consent required, `PUBLIC` opt-in,
  `INTERNAL` deny-by-default, `RESTRICTED`/`UNKNOWN` deny. Invalid policy documents are
  rejected rather than partially trusted.
- **One** egress gateway wraps every provider attempt; it evaluates policy *before*
  invoking the transport, audits metadata only, and counts external attempts.
- The deterministic planner/local summary paths do not require any provider and keep the
  workflow functional offline.

## Consequences

Positive: testable zero-external-call guarantee for RESTRICTED/UNKNOWN fixtures; no
silent fallback or downgrade; auditable decision codes in the UI.
Negative: cloud features are intentionally limited until a reviewed sanitization/token
vault flow exists (v0.4); the local path offers reduced functionality (deterministic
summary) when no local model is installed.

## Alternatives

Classification detection only (rejected: detectors can fail; source-first is required),
per-call ad-hoc checks (rejected: impossible to prove absence of egress), cloud-on-by-
default with warnings (rejected: violates the baseline).

## Evidence

`src/Aether.Security/Policy`, `src/Aether.Security/Egress/EgressGateway.cs`,
`src/Aether.AI/Planner/CloudPlanProvider.cs`,
`src/Aether.AI/Summarization/CloudSummaryProvider.cs`, `tests/Aether.Security.Tests`,
`tests/Aether.AI.Tests`, E2E denial tests.
