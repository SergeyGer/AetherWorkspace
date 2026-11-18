# ADR-013 — Keep the bounded FDC3 subset; no broader surface for v1.x

**Status:** Accepted (2026-11-18), v1.5 extension evaluation AW-064.
**Context:** The v1.5 candidate evaluated user channels, broader contexts/intents/listener surfaces
and directory federation. The project claims only a declared bounded subset and has no independent
FDC3 conformance evidence; broadening the runtime surface would create new message shapes and
compatibility claims without a conformance suite.

**Decision:** For the v1.x line, keep the declared bounded subset exactly as documented in
`docs/fdc3/supported-subset.md`:
- single `default` app channel; no user/global channels or channel-join/listener APIs;
- three intents (`ViewInstrument`, `ViewAnalysis`, `ViewChart`) only; no intent listeners, no
  arbitrary/third-party intent registration;
- `fdc3.instrument`, `fdc3.organization` and project-specific `aether.exposure` only; full FDC3
  context catalogue, app-directory federation and Desktop Agent Bridge are **rejected**;
- no external conformance claim and no third-party runtime interop claim beyond the recorded Sail
  DACP checks.

**Tests/evidence:** rejection tests cover unknown intents, context-type mismatch, unapproved
preferred targets, unconfigured/non-default channels and bounded snapshot projections. The supported
subset matrix is updated with an explicit rejected-surface table. CI remains the evidence; no
conformance wording is added.

**Consequences:** Any future expansion requires a superseding ADR, standards fixtures/tests,
independent interoperability evidence and explicit non-conformance wording removal. The runtime
surface stays small and auditable.

**Evidence:** issue #92, `docs/fdc3/supported-subset.md`, `tests/Aether.Fdc3.Tests/*`.
