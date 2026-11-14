# Threat model (v0.1 vertical slice)

- **Status:** living document for the implemented baseline; review before every release.
- **Scope:** local broker IPC, app registry/launcher, sessions/capabilities, context
  channels, deterministic plan execution, AI adapters, classification/policy/egress,
  audit, synthetic sample apps. Windows-only runtime paths are compile-verified here and
  runtime-verified in the Windows CI job.
- **Out of scope:** operating-system or hypervisor compromise, physical access, cloud
  provider internals, enterprise identity systems, production deployments.

## 1. Assets

Synthetic workspace context and typed payloads; the integrity of the action-execution
pipeline; policy/decision integrity; local audit evidence; session credentials and
provider API keys; trust in the allowlisted app registry; availability of broker/shell.

## 2. Actors

Local same-user user, operator of the shell, independent sample apps launched by the
broker, untrusted workspace text and app content (possibly prompt-injected), a network
provider used only with explicit consent, a local administrator/attacker with filesystem
access, third-party dependencies.

## 3. Trust boundaries

| ID | Boundary | Implemented controls |
|---|---|---|
| T1 | Untrusted UI/app/workspace content vs. the model | Content is data: classification is source-first; plans are typed and cannot carry instructions as executable actions; prompt-injection tests treat content fields as attacker-controlled |
| T2 | Untrusted AI plan proposal vs. deterministic execution | Strict JSON parse (unknown members rejected), schema/allowlist/action-budget/deadline validation, capability authorization, typed handlers only, structured denials, no fallback path |
| T3 | Provider egress | Single `EgressGateway`; deny-by-default policy; explicit consent; RESTRICTED/UNKNOWN hard deny; retries/errors also gated; metadata-only audit; tests assert zero external calls |
| T4 | Same-user local IPC | Current-user pipe/socket permissions **plus** single-use registration tokens bound to pid/start-time/executable, short-TTL sessions, per-operation capability and resource-grant checks |
| T5 | Local model/plugin processes | Optional adapters behind interfaces, disabled by default; loopback transport is not assumed air-gapped; missing model yields a safe error, never cloud fallback |

## 4. Threats and mitigations

### T-01 Same-user rogue process / IPC impersonation

- **Paths:** connect to the broker socket/pipe, replay a registration token, spoof an app
  id, reuse an expired session.
- **Controls:** broker only accepts sessions from processes it launched; registration
  token is single-use with ~30 s TTL; identity binding checks pid + process start time +
  canonical executable path; session secret stored as a hash and compared in fixed time;
  expired sessions are rejected; every privileged RPC requires a capability and, where
  relevant, channel/context-type/classification grants.
- **Verified by:** `SessionHandshakeTests` (unknown/replayed/expired token, identity
  mismatch, unsupported protocol, operator token mismatch, expiry), `BrokerAuthInterceptor`.
- **Residual:** same-user processes remain a weak OS isolation boundary; a same-user
  process that can read the handshake file/child environment gains operator or app
  credentials for its lifetime. Not a defense against a local administrator.

### T-02 Prompt injection from app text or workspace content

- **Paths:** injected text tries to make the model execute commands, choose a cloud
  provider, or bypass confirmation.
- **Controls:** plan language has no executable paths/URLs/shell; validator rejects such
  content; actions are typed and app/context ids must be in trusted configuration;
  `SummarizeContext` mode is forced to `private` in v0.1 so model text cannot select cloud;
  provider routing is decided by policy, not by plan text; cloud output is untrusted and
  re-validated.
- **Verified by:** validator tests, 100-entry unsafe plan corpus, cloud output rejection
  tests, E2E plan-file boundary tests.
- **Residual:** semantic manipulation of *generated summaries/content* is not fully
  solved; quality/adversarial evaluation is v0.4 work.

### T-03 Sensitive-data cloud egress

- **Paths:** classification bypass, silent downgrade, provider retry after denial,
  telemetry/logs, loopback model with network capability.
- **Controls:** source-first classification, detectors escalate only, UNKNOWN fail-closed;
  deny-by-default policy; explicit consent; single egress chokepoint evaluating policy
  before transport invocation; RESTRICTED/UNKNOWN denied; metadata-only audit; no
  telemetry; local deterministic path requires no provider.
- **Verified by:** policy matrix tests (all classifications × consent × provider),
  egress gateway tests (transport call count zero on denial), AI cloud-provider tests,
  E2E `RESTRICTED` run with `externalAttempts = 0`, cloud-planner denial E2E.
- **Residual:** no zero-leakage guarantee; a third-party library or OS-level network path
  added later must be re-audited; provider retention terms cannot be established by a
  local flag. The test claim is bounded: zero observed external provider calls for the
  synthetic fixtures in the suite.

### T-04 Queue amplification / local IPC denial of service

- **Paths:** message floods, oversized payloads, slow subscribers, unbounded retries.
- **Controls:** bounded per-subscriber queues with explicit overflow reporting; payload
  size limit (default 16 KiB); per-plan action budget and per-step/per-plan deadlines;
  bounded dedupe buffer; one writer per subscriber queue; session/channel caps.
- **Verified by:** channel overflow/dedup/isolation tests, validator action-budget tests,
  executor deadline/cancellation tests.
- **Residual:** resource pressure within one user session remains possible; no per-client
  rate limiting beyond queue bounds yet; a hostile same-user process could still open many
  sessions until the operator-token budget is exhausted (bounded restart available).

### T-05 Trace/log disclosure

- **Paths:** raw payloads or identifiers written to logs, trace views, exports.
- **Controls:** audit schema records only timestamp, trace id, event type, outcome, code,
  policy version, classification, provider category, redaction count, payload byte length
  and duration; no raw content; child stdout capture is opt-in diagnostics for synthetic
  demos; redaction counting happens locally.
- **Verified by:** audit tests asserting synthetic identifiers/summary text are absent
  from serialized audit; E2E asserts the broker audit file does not contain synthetic
  account ids.
- **Residual:** local users with filesystem access can read logs; retention/rotation and
  export classification are not fully implemented (v0.5).

### T-06 Registry / artifact tampering

- **Paths:** modified manifest adds an unapproved executable or capability; signature-less
  distribution.
- **Controls:** registry is trusted local configuration validated as a whole (unknown
  capabilities/classifications reject loading); executable templates are expanded only
  from broker-supplied tokens; canonical paths checked against configured executable roots;
  argument templates bounded; no arbitrary path from model input; pinned NuGet versions and
  central package management.
- **Verified by:** registry validation tests, launcher negatives (unallowlisted executable,
  path traversal template), `Directory.Packages.props`.
- **Residual:** development builds are not a trust anchor; signed packages, checksums and
  SBOM are v1.0 work (AW-036). A local admin can edit the registry by design.

### T-07 Model/API supply-chain or output changes

- **Paths:** provider changes response format; local model package replaced; malformed or
  partial output.
- **Controls:** provider adapters behind interfaces; exact model ids in configuration, not
  business logic; strict output parsing; malformed/partial output rejected; provider errors
  surfaced, never bypassing validation; optional cloud disabled by default.
- **Verified by:** cloud/local adapter tests (malformed output, denied paths, zero calls).
- **Residual:** no continuous model evaluation/contract test suite yet (v0.4); provider
  terms and model licences are not validated in this baseline.

### T-08 Audit tampering

- **Paths:** modify/delete historical audit entries.
- **Controls:** hash-chained entries (optional HMAC), sequence + previous-hash verification;
  JSONL persistence with explicit verification; "tamper-evident" documented as not
  immutable; local-admin limitation documented.
- **Verified by:** audit chain tests (modified entry, deleted entry, HMAC key mismatch).
- **Residual:** full filesystem control (truncate whole file, replace chain) is outside the
  guarantee; append-only OS storage/signing is future work.

## 5. Control requirements mapping

| Requirement | Implementation |
|---|---|
| SE-01 credentials via env/credential store, never repo | `EnvironmentSecretProvider`; no secrets in repo; README/CONTRIBUTING rules |
| SE-02 allowlisted canonical executables | `AppRegistry`, `AppLauncher`, allowed roots, negatives tested |
| SE-03 short-lived per-operation capabilities | `SessionManager`, `BrokerGrpcService` capability checks, auth tests |
| SE-04 metadata-only audit of actions/policy/provider | `AuditEvent`, `EgressGateway`, executor audit, tests |
| SE-05 tamper-evident audit with verification | `HashChainAuditLog`, `AuditChainVerifier`, tests |
| SE-06 dependency scanning/SBOM/licence notices | Pinned packages + `THIRD_PARTY_NOTICES.md`; SBOM/scanning is v1.0 |

## 6. Verified vs not verified

**Verified in this environment (Linux):** Unix-socket transport end-to-end, session
binding/handshake negatives, registry/launcher negatives, channel bounds/dedupe/ordering,
capability authorization, plan validation incl. 100-entry corpus, policy matrix, zero
external egress on RESTRICTED/UNKNOWN, private local summary, hash-chain tamper detection,
crash-restart E2E, deterministic layout computation.

**Not verified here:** Windows named-pipe runtime behaviour, Win32 `SetWindowPos`
arrangement, WPF shell/sample rendering and accessibility, OS-level egress-blocked network
tests, real provider interactions, docking (not evaluated), model quality/performance.

## 7. Reporting

See [`../../SECURITY.md`](../../SECURITY.md). Never include real data or secrets in a report;
use synthetic reproductions.
