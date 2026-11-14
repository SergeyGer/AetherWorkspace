# ADR-006: Declare a bounded FDC3 3.0 alignment instead of conformance

- **Status:** Accepted
- **Date:** 2026-11-14

## Context

The project must demonstrate FDC3-aligned interoperability without claiming full
conformance. Full FDC3 requires a Desktop Agent API, app directory, intents, user/app
channels and interop testing against an independent implementation.

## Decision

- Align the typed context layer with FDC3 where cheap and honest: the implemented context
  type is a bounded `fdc3.instrument` subset (type/name/id.ticker/id.isin) carried on one
  `default` app channel with provenance metadata (source app/instance, trace id, span id,
  timestamp, classification).
- Publish an explicit supported/unsupported matrix (`docs/fdc3/supported-subset.md`).
- Do **not** claim FDC3 conformance; independently verified interoperability and
  additional surfaces are v0.3 backlog items (AW-021…AW-024).

## Consequences

Positive: meaningful alignment for the reference workflow without overstating
interoperability; the matrix can grow with evidence.
Negative: no intents, user channels or third-party desktop-agent interop in this baseline;
the matrix requires review against the official standard before publication as a v0.3
deliverable.

## Alternatives

Claim full FDC3 for the demo context (rejected: dishonest), custom context schema only
(rejected: loses alignment value), delay all FDC3 work (rejected: context transport is
needed for the MVP and aligns naturally).

## Evidence

`src/Aether.Contracts/Models/Contexts/InstrumentContext.cs`, envelope provenance,
`docs/fdc3/supported-subset.md`, contract tests.
