# ADR-009 — PUBLIC-only cloud policy for the v1.x line (formal scope decision)

**Status:** Accepted (2026-11-16), corrective release v1.0.1.
**Context:** Technical specification §7.2 describes optional conditional INTERNAL routing with
session-scoped tokenization and placeholder restoration. The implemented `SessionTokenVault` is
unit-tested but is **not** wired into every provider path; the `ModelRouter` only permits PUBLIC
cloud and keeps INTERNAL/RESTRICTED/UNKNOWN local. The post-v1.0 gap assessment (G-03) flagged the
divergence between written design and runtime behaviour.

**Decision:** For the v1.x line, cloud routing is **PUBLIC-only**. The conditional INTERNAL-cloud
vault path is de-scoped/excluded rather than claimed. The vault remains an available local security
primitive (unit-tested) and is not used to send sanitised INTERNAL payloads to providers.

**Consequences:**
- No INTERNAL data leaves the machine; the strongest fail-closed posture is preserved.
- The spec is narrowed by this ADR and by the gap-assessment response; docs/Wiki must not imply
  INTERNAL cloud support.
- Enabling conditional INTERNAL routing in a future release requires a complete end-to-end
  implementation (classifier → vault → egress → de-mask), failing negative tests, and supersession
  of this ADR.
- Egress deny-by-default tests and zero-external-call assertions remain mandatory.
