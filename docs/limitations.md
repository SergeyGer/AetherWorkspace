# Honest limitations

AetherWorkspace is a **reference implementation with test evidence**, not a certified
product. This page lists what is *not* claimed and what remains unfinished. If a claim
is not backed by a test or a measured baseline in this repository, it is not made.

## Platform and runtime

- **Windows split:** the Windows named-pipe transport, full test suite and the headless
  reference-workflow demo run in the Windows CI job and are green as of commit
  `674e2c0` (run
  [43641011037](https://github.com/SergeyGer/AetherWorkspace/actions/runs/43641011037)).
  The WPF shell/sample windows build on Windows CI but their interactive UI behaviour and
  the visual result of Win32 top-level placement are not automatically verified yet and
  need manual desktop verification (or UI automation) before a v0.5 UX claim. The Linux
  development environment executes everything else via Unix domain sockets + headless
  samples.
- **Linux/headless is the CI substitute.** Unix domain sockets and headless sample
  processes exercise the same typed protocol, policy, executor, broker sessions/channels
  and audit logic; they are not the reference desktop experience.
- **WPF UI automation (FlaUI) covers the shell smoke + restricted reference workflow in
  Windows CI**; visual correctness of Win32 top-level arrangement (multi-monitor, DPI,
  focus, accessibility) still needs manual desktop verification or a broader UI suite.
- **No cross-process window embedding.** `SetParent`/`HwndHost` docking is excluded from
  the MVP; no feasibility spike was executed and no claim is made. Only ordinary
  top-level placement (never reparenting) is implemented.
- **No network listener and no telemetry egress by default.**

## AI and models

- The **deterministic command grammar** is the primary planning path for the reference
  workflow; it recognises a bounded set of workspace commands and rejects unknown
  commands rather than guessing.
- Optional cloud and loopback local model adapters exist behind the egress gateway and
  are **disabled by default**. They are exercised with test doubles; no live provider or
  local model installation is validated here. Model identifiers, hardware profiles,
  licence metadata and evaluation numbers are v0.4 deliverables (AW-027/AW-028).
- The private local summary is a **deterministic reduced-function path**, not a
  language model. It computes aggregate synthetic figures locally and never performs
  network I/O.
- **No model performance claims:** no TTFT, token throughput, RAM/VRAM, cost, plan
  accuracy percentage, or "AI quality" metric is claimed. The ≥95% plan-accuracy target
  belongs to the v0.4 evaluation suite.
- Prompt-injection resistance is structural (untrusted plans cannot carry executables,
  URLs, shell commands; only allowlisted typed actions pass validation). Semantic
  manipulation of generated summaries is not fully solved and requires quality testing.

## Privacy and security

- **Rule-based detectors only** in the current baseline (synthetic account IDs, email-
  like and IBAN-like patterns). There is **no guarantee of PII detection** and no
  precision/recall claim. Optional NER escalation and the session-scoped token vault are
  v0.4 backlog items.
- **No zero-leakage claim.** The bounded test claim is narrower: for every synthetic
  RESTRICTED/UNKNOWN fixture and failed-classifier fixture in the suite, the observed
  number of external provider calls is zero (egress instrumentation + tests).
- The optional cloud path for `INTERNAL` data (sanitized, explicitly reviewed) is
  **formally de-scoped for the v1.x line** (ADR-009, `docs/adr/ADR-009-public-only-cloud-policy.md`):
  cloud attempts are restricted to PUBLIC data; the session token vault remains local-only and is
  not an INTERNAL-cloud sanitisation path. The deny-by-default behaviour is implemented and tested.
- **Same-user processes remain a weak OS isolation boundary.** Process-bound launch
  tokens and capability checks raise the cost of impersonation; they do not create a
  security boundary stronger than the OS user. Residual risk is documented in the
  threat model.
- The **audit chain is tamper-evident, not immutable.** A local administrator can delete
  or truncate log files; verification detects modification/deletion of entries within a
  retained chain (including across rotated files), but not an attacker with full control
  of the machine. Rotation and bounded retention are implemented; signed append-only
  storage, an external anchor and an operator runbook are not.
- Operator handshake files use current-user permissions on Unix; on Windows the file is
  written with default user ACLs. Secrets for cloud providers come from environment
  variables (Credential Manager integration is a production item, not implemented).
- **Cloud data-retention terms cannot be established by a local JSON flag.** Any real
  provider use requires separate legal/security review; the repository does not do that.

## Correctness and reliability

- **v0.4 model/privacy boundaries:** cloud attempts are limited to PUBLIC data with explicit
  cloud enablement + consent + configured provider; INTERNAL/RESTRICTED/UNKNOWN never attempt
  cloud. The evaluation smoke covers 6 synthetic commands only (no quality ranking). The
  session token vault is volatile/in-memory and not yet applied to every prompt path. No local
  model was installed in this environment, so local-inference behaviour is reported
  unavailable rather than validated.
- **Windows 11 physical-desktop evidence is recorded for the tested scope:** the reference
  orchestrator, named-pipe GUI E2E and layout suites passed on the physical Windows 11 laptop
  (final release gate `20261116-153831-release-full-b3e064`: 511/511, dual monitors; sanitized
  evidence in `docs/testing/evidence/`). GitHub-hosted Windows CI remains a non-authoritative
  supporting gate for desktop support claims. Remaining gaps — DPI 125/150%, focus/keyboard/modal,
  display-topology changes, crash/restart matrix and UI frame-time profiling — are explicit in
  the post-v1.0 gap assessment rather than implied PASS.
- **FDC3 scope is a declared bounded subset:** `fdc3.instrument`/`fdc3.organization`
  contexts and three intents only; no user channels, intent listeners, app-directory
  federation or external FDC3 desktop-agent interop; no conformance claim.
- **No unbounded-queue or crash-safety guarantee under adversarial budgets.** Queues are
  bounded with explicit overflow reporting; message size limits, action budgets and
  deadlines exist. Hang/kill/broker-disconnect fault injection is covered on Linux; a
  complete matrix (disk-full, clock changes, Windows runtime hangs) and OS-level
  network-egress-blocked capture are not complete.
- Retry semantics are at-most-once with deduplication keys for explicitly idempotent
  publish retries; there is no general transaction or exactly-once delivery.
- Window arrangement is deterministic for configured monitor work areas. Dual-monitor restore
  and missing-monitor fallback are verified on real hardware; per-monitor DPI 125%/150%,
  live display-configuration changes and automated visual assertions remain manual/approval-only.
- The chart sample uses WPF vector rendering instead of WebView2; the specification
  allowed WebView2 "where required" but it is not required for the deterministic
  demonstration.
- **Reference workflow action budget:** the reference plan uses 6 actions (three app
  launches + publish + arrange + summarize). The library default remains 5 per the
  specification; the demo configuration sets 6 (see ADR-008).

## Accepted manual-verification limitation (v1.x)

Five reference-desktop manual rows are **accepted known limitations (won't test)**, not PASS:
DPI 125%/150% with rollback (WT-05-05), focus/keyboard/modal (WT-05-06), display-topology recovery
(WT-05-03), physical child crash/hang recovery (WT-05-09) and the manual Inspector/export privacy
confirmation (WT-05-08). Issue #120 is closed with this explicit risk-acceptance decision. Automated
rows, dual-monitor restore/fallback, the 100-cycle UI measurement and sanitized audit/redaction
tests remain PASS. Evidence:
`docs/testing/evidence/2026-11-18-v1.1-wt-formal-exceptions.md`; matrix:
`docs/testing/wt-matrix.md`. Re-executing the manual rows requires a new scoped issue and operator run.

## Documentation and process

- GitHub Releases are produced per milestone: since v1.0.1 they include a Windows x64 WPF desktop
  bundle (Shell + three WPF sample apps + broker + launcher), a headless runtime archive, a source
  snapshot, SHA-256 checksums and an SPDX SBOM. Verification: `tools/release/Verify-Release.ps1`.
  There is **no signed installer, no
  reproducible-build guarantee and no Windows code signing** yet (v1.0 scope). These
  releases are development baselines, not production distributions.
- No calendar commitments, compliance certifications, DORA/GDPR statements, or claims
  of universal third-party application compatibility.
- The planning issue pack under `docs/planning/` is a planning baseline; not every issue
  is implemented and stable `AW-` ids are planning identifiers, not GitHub issue numbers.
- English is used for code, tests and documentation; the reference demo data is
  synthetic and fictitious.

## v1.3 Hybrid AI limits (2026-11-18)

- The supported local profile is `qwen2.5:7b` via Ollama (Apache-2.0; pinned layer digest). The
  weights are not bundled and local inference was not measured on the CI host; physical
  memory/latency values are only claimed from the operator measurement script/report.
- The evaluation harness reports **TTFT as unavailable** for the current non-streaming adapters; this
  is an explicit deviation, not a fabricated latency. Latency/token/cost values are labelled
  estimates unless produced by a provider-billing export.
- Cloud remains PUBLIC-only with consent; INTERNAL sanitized cloud routing is the v1.4 decision,
  not part of v1.3. The local endpoint is validated loopback-only; a loopback service is not
  automatically air-gapped.

## Sanitized INTERNAL cloud routing (v1.4 rejection)

Sanitized INTERNAL cloud routing is **rejected** for the v1.x line (ADR-011). Cloud remains
PUBLIC-only; the token vault is a local primitive, not a provider-path sanitizer. No INTERNAL payload
is sent to a cloud provider, and no zero-leak/PII-guarantee or anonymization claim is made. The
`AllowSanitizedInternal` policy flag is hard-disabled in the engine; re-enabling requires a new ADR
and independent security evidence.

## v1.5 extension scope

- No Electron adapter is included (ADR-012: measured runtime footprint and duplicate typed-context
  evidence make the PoC a no-go for v1.x). Revisit only with a specific unmet requirement.
- The FDC3 runtime surface remains the declared bounded subset (ADR-013); user/global channels,
  broader contexts/intents/listeners, directory federation and Desktop Agent Bridge are rejected.
  No FDC3 conformance or external interoperability claim is made.
