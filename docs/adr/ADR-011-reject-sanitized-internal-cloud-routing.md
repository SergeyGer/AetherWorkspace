# ADR-011 — Reject sanitized INTERNAL cloud routing for the v1.x line (v1.4 security gate)

**Status:** Accepted (2026-11-18). Supersedes the latent "sanitized INTERNAL" branch of ADR-009 for
the v1.x line.
**Context:** AW-061 required an explicit v1.4 security decision before any sanitized INTERNAL cloud
routing or token-vault integration. The `SessionTokenVault` is a unit-tested local primitive, but the
end-to-end chain (classifier → vault → egress → provider → de-mask) is not implemented, the
rule-based detector has no PII precision/recall or NER guarantee, provider-side logging/retention is
outside our control, and a de-masking bug or placeholder leak would be a direct data-egress incident.
The threat model still records detector evasion and provider data-handling as open risks.

**Decision:** **Reject** sanitized INTERNAL cloud routing for v1.x. Keep cloud **PUBLIC-only**.
- `PolicyEngine` hard-disables the `AllowSanitizedInternal`/`SanitizationReviewed` allow branch, even
  if a custom policy document sets the flag. The public policy flag remains in the document schema for
  forward compatibility but cannot enable a v1.x cloud route.
- The `SessionTokenVault` remains a local redaction/rehydration primitive, **not** wired into any
  provider path. No INTERNAL/RESTRICTED/UNKNOWN payload is sent to a cloud provider.
- No default configuration, CLI flag or UI control enables INTERNAL cloud routing.

**Consequences / required follow-up:**
- Strongest fail-closed posture preserved; no zero-leak or anonymization claim is made.
- Re-enabling this path requires a superseding ADR, an independent security review, an
  approved classifier/DLP with measured false-negative behaviour, provider data-handling assessment,
  end-to-end before/after sanitized-payload tests, expiry/cancel/failure tests, and a demonstrable
  de-mask/placeholder-leak prevention story.
- Tests must prove default denial and the hard-disabled branch; `RESTRICTED`/`UNKNOWN` external
  attempts stay zero.
- Wiki/limitations must state the rejection explicitly; the release notes must not imply INTERNAL
  cloud support or PII guarantees.
