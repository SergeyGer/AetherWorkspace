# ADR-010 — Template-based (not LLM/compiled) contract studio for the v1.x line

**Status:** Accepted (2026-11-16), corrective release v1.0.1.
**Context:** The v0.6 release-plan step AW-034 describes a bounded, review-only contract generation
assistant; the original technical spec §10 sketches future reviewed/compiled/diffed generation.
The implemented studio generates deterministic bounded templates (registry entry, proto3 snippet,
JSON Schema) into untrusted staging envelopes with CLI `generate`/`review`/`apply --confirm` and a
validation gate (collisions, breaking changes, forbidden paths). It does not use an LLM and does not
run `protoc` or compile generated artifacts. Gap assessment G-05 flagged the wording gap.

**Decision:** v1.x calls this a **template-based contract studio**. AI-assisted plain-text generation
and compiler validation remain future work; the review-only, no-execution posture is the accepted
contract for v1.x.

**Consequences:** README/Wiki/release notes use "review-only, template-based"; no claim of AI
generation or compiled output; `apply --confirm` remains the only persistence path and rejects
breaking changes / forbidden paths.
