

# AetherWorkspace
Technical Specification
Version 3.0  |  Proposed design  |  14 November 2026

| Status | Design baseline; implementation remains subject to test evidence |
| Audience | Open-source contributors, maintainers and technical readers |
| Scope | Vendor-neutral desktop interoperability and policy-aware AI workflows |

## Publication note
AetherWorkspace is an independent, vendor-neutral reference implementation. All examples use synthetic data. Security and performance statements in this document are engineering objectives, not certifications or measured results.

# Document map
01  Product definition & scope
02  User journeys & product metrics
03  System architecture & trust boundaries
04  Contracts, IPC & service lifecycle
05  Safe AI ActionPlan execution
06  Model-independent AI integration
07  Classification, privacy & cloud egress
08  Security, audit & threat model
09  Desktop UX and window integration
10  Observability, diagnostics & operations
11  Non-functional quality targets
12  Verification, test strategy & acceptance
13  Delivery, repository and governance
14  Architecture decisions and risk register
15  Configuration examples & references

# 01  Product definition & scope
## 1.1 Problem statement
Specialized desktop tools frequently remain isolated: context must be copied between applications, dependent tools must be launched manually, and workflows lack end-to-end visibility. AI can reduce the interaction burden, but untrusted model output must never bypass deterministic authorization and data-handling controls.
## 1.2 Product hypothesis
For an instrument-centric, multi-application task, a policy-aware planner and a deterministic cross-process executor can reduce manual operations and completion time while preserving traceability and preventing unauthorized cloud transmission of sensitive workspace data.
## 1.3 Goals
Demonstrate one complete workflow across three independent Windows processes using synthetic financial data.
Separate natural-language planning from deterministic verification, authorization and execution.
Provide contextual interoperability with a clearly declared, testable subset of the FDC3 3.0 standard.
Provide an offline-capable private execution path and an explicitly opt-in cloud path with enforced classification.
Make failures, authorization decisions and latency observable without logging raw sensitive payloads.
Publish reproducible builds, tests, performance methodology, threat model and honest limitations.
## 1.4 Explicit non-goals for MVP
Embedding arbitrary third-party windows, guaranteed cross-process HWND hosting, or universal docking.
Full FDC3 conformance, agent bridging and compatibility with every application directory.
Live trading, order execution, production market connectivity or processing real customer information.
Compliance certification, guaranteed PII detection or a guarantee of zero leakage.
Cloud model training/fine-tuning, an enterprise-wide identity service or a hosted SaaS backend.
Autonomous code execution or execution of commands generated directly by a language model.
## 1.5 Platform and baseline

| Dimension | Baseline / decision |
| Operating system | Windows 11 x64 as primary reference; exact supported editions documented and tested. |
| Runtime | C# 14 and .NET 10 LTS; Windows UI targets net10.0-windows. |
| UI | WPF; WebView2 only for the demonstration chart where required. |
| IPC | gRPC / Protobuf over local Windows Named Pipes; HTTP/2 endpoint; no network listener in MVP. |
| Interop | FDC3 3.0-aligned bounded subset, with explicit support matrix. |
| AI | Capability-driven adapters; one configured cloud adapter and one private local adapter for MVP. |
| Data | Synthetic instruments, exposures and issuer names; no sample customer records. |
| Release | M0 feasibility → v0.1.0 MVP → v0.2 → v0.3 → v0.4 → v0.5 → v0.6 → v1.0. |

All versions of external libraries, provider model identifiers and SDKs shall be pinned to tested releases in the repository; model identifiers are configuration values, not assumptions embedded in business logic. Availability and licensing require validation before release.
# 02  User journeys & product metrics
## 2.1 Primary reference workflow
1. Enter: “Open Northstar bond analytics, show the associated risk view, synchronize this instrument across the workspace and summarize the exposure privately.”
2. Parse into a versioned ActionPlan; if required, invoke the local or opt-in cloud adapter according to policy.
3. Validate schema and operation allowlist, then check app identity, data classification and operation capabilities.
4. Discover or launch the Watchlist, Risk View and Chart sample processes; complete a bounded authenticated handshake.
5. Publish the instrument context, preserving source and end-to-end correlation metadata.
6. Arrange supported top-level windows in a deterministic side-by-side layout (no reparenting required).
7. Summarize synthetic exposure privately; show actions, timing and outcomes in an opt-in trace view.
## 2.2 Secondary journeys
Unknown/untrusted input: classification resolves to UNKNOWN; cloud call is denied without retry or silent downgrade.
Unauthorized command: valid-looking ActionPlan requests an unapproved executable; validation returns a structured denial.
Child failure: one sample app exits mid-workflow; broker reports failure, preserves the shell and offers controlled restart.
Network disconnection: private commands and supported private summarization continue if the local runtime is available.
## 2.3 Outcomes and evaluation

| Metric | Measurement | Initial success criterion |
| Task completion time | Median and p95 from command to visible output, over scripted test tasks. | Record comparison against manual baseline; do not claim improvement without data. |
| Manual interaction count | Mouse/keyboard actions for manual vs assisted workflow. | Show an observable reduction on the reference task. |
| Plan correctness | Exact allowed ActionPlan match on labeled test set. | Candidate ≥95% on 100 stable synthetic commands; tune after baseline. |
| Safe execution | Denied actions blocked before side effects. | 100% of documented negative tests denied. |
| Restricted egress | External transmissions on RESTRICTED/UNKNOWN tests. | 0 observed external AI calls in the test corpus. |
| Reliability | Successful workflow executions over 100 repeated runs. | Define baseline at M0; target ≥99% on supported reference setup before v1.0. |

# 03  System architecture & trust boundaries

Figure 1. Logical responsibility boundaries (illustrative; not a network deployment diagram).
## 3.1 Component contract

| Component | Responsibility | Outside responsibility |
| Aether.Contracts | Protobuf messages, versioned ActionPlan and app manifest schemas, standard context adapters. | No UI or provider implementation. |
| Aether.Broker | Local IPC, registry, launch/handshake, channel routing, queue and health lifecycle. | No autonomous interpretation of prompts. |
| Aether.Execution | Allowlisted plan validation, capability authorization, orchestration state machine, compensation. | No direct LLM or arbitrary process execution. |
| Aether.Security | Data classifications, policy decisions, sanitized payload contract, egress deny gate, audit. | No guarantee of perfect sensitive-entity recognition. |
| Aether.AI | Provider adapters, local/private inference, structured plan generation and evaluation. | No direct IPC access or filesystem/process mutation. |
| Aether.Shell | WPF command bar, layout, status, confirmation prompts and optional diagnostics. | No authoritative policy logic. |
| Aether.SampleApps | Synthetic watchlist, risk view and chart demonstrators with explicit app identities. | No live external services. |
| Aether.Tests / Benchmarks | Contract tests, E2E automation, adversarial fixtures, latency measurement. | No production data. |

## 3.2 Trust boundaries and data movement
Boundary T1: UI and sample-app content are untrusted input; they cannot be treated as system instructions to the model.
Boundary T2: AI plans are untrusted proposals; validation, authorization and bounded execution are deterministic.
Boundary T3: every provider egress goes through a single deny-by-default gateway (including retries, logs and error telemetry).
Boundary T4: same-user IPC access does not establish app trust; process binding and capability authorization are separate controls.
Boundary T5: local model and plugin processes are separate software dependencies; their endpoints and network behavior must be verified.
## 3.3 Technical constraints
Use dependency-inverted contracts. A failed or unavailable AI adapter must not block deterministic IPC and layout. All cross-process calls carry cancellation and deadlines. UI uses non-blocking asynchronous operations with thread-affine WPF updates. Broker and Shell have distinct process lifetimes.
# 04  Contracts, IPC & service lifecycle
## 4.1 Protocol decisions
Use Kestrel gRPC/HTTP2 over Windows Named Pipes for supported local processes. Windows PipeSecurity/current-user-only rules are necessary but insufficient: additional process identity, manifest authorization and per-action capabilities are mandatory. A localhost HTTP endpoint shall not be exposed in MVP.
## 4.2 Message envelope

| Field | Rule |
| messageId | UUID; distinct for each emitted event. |
| traceId / spanId | Correlate all workflow operations, including failures. |
| sourceAppId / instanceId | Authenticated app identity, resolved from registered process. |
| schemaVersion | SemVer-compatible contract identifier; reject unsupported major versions. |
| contextType / payload | Explicit typed payload with size limits and classification. |
| classification | PUBLIC, INTERNAL, RESTRICTED or UNKNOWN; unknown is fail-closed. |
| createdAtUtc / deadlineUtc | Time controls for expiry and bounded processing. |

## 4.3 Broker functional requirements

| ID | Requirement | Acceptance evidence |
| BR-01 | Launch only registered, normalized and explicitly allowed executable paths; reject unapproved arguments. | Tampered registry and arbitrary-path negative tests. |
| BR-02 | Bind a session to a launched process and app identity before granting short-lived capabilities. | Spoofed handshake tests. |
| BR-03 | Offer bounded queues with per-channel ordering, backpressure, deadlines and explicit overflow behavior. | Overload and cancellation tests. |
| BR-04 | Define delivery as at-most-once by default; retries only for explicitly idempotent operations with deduplication keys. | Duplicate/retry chaos tests. |
| BR-05 | Provide heartbeats, per-app health state and controlled restart; app crash does not terminate Shell/Broker. | Fault injection E2E. |
| BR-06 | Apply message size limits, strict protobuf schema handling and supported major-version checks. | Schema and fuzz tests. |

## 4.4 FDC3 interoperability
A bounded FDC3 3.0 alignment target is declared rather than blanket conformance. The supported subset is documented for contexts, user/app channels, intent raise/resolve, application directory fields and metadata. Propagate required provenance (including trace identifier, timestamp and originating app instance where applicable). External compatibility claims require tests against an independent implementation.
# 05  Safe AI ActionPlan execution
## 5.1 Deterministic execution lifecycle
INPUT → CLASSIFY → PLAN (UNTRUSTED) → SCHEMA VALIDATE      → AUTHORIZE (POLICY + CAPABILITY) → CONFIRM WHEN REQUIRED      → EXECUTE BOUNDED STEPS → VERIFY STATE → AUDIT OUTCOME
Each step transitions through PENDING, AUTHORIZED, RUNNING, SUCCEEDED, DENIED, FAILED or COMPENSATED. Denial and validation failure must never trigger an alternative higher-privilege path.
## 5.2 Allowed operations for v0.1

| Operation | Allowed semantics | Guardrails |
| LaunchApp | Start one registered sample application. | Allowlisted app ID, executable and argument template. |
| PublishContext | Broadcast a typed instrument context. | Source capability, target channel grant, schema and classification. |
| ArrangeWindows | Move known sample top-level windows. | Bounded window handles owned by registered apps; no reparenting. |
| SummarizeContext | Create textual summary from synthetic/authorized source data. | Provider routing policy, no tool calls, output classification. |
| ShowStatus | Display trace-safe progress and completion status. | No raw sensitive payload in diagnostics. |

## 5.3 Execution invariants
Max actions per plan: configurable; default 5 for MVP; reject recursion and unbounded loops.
No executable paths, shell commands, arbitrary URLs or new app identities from AI output.
No direct invocation of RPC based only on model-generated text; actions must bind to typed contracts.
Confirmation required for any future mutating or irreversible capability; MVP has none.
Per-step deadlines and explicit cancellation; compensation defined for reversible window moves and launched test processes.
Prompt-injection tests treat every app content field as attacker-controlled data.
## 5.4 Human-readable denials
UI must explain denial category (for example: unauthorized app, unsafe classification, provider unavailable) without disclosing hidden policy secrets or raw sensitive data. A redacted trace ID enables incident diagnosis.
# 06  Model-independent AI integration
## 6.1 Capability-based provider abstraction
Providers implement an isolated adapter interface for structured generation, summarization, health probes and cancellation. The routing decision depends on capability, availability, sensitivity, local hardware and explicit cloud consent—not on a hardcoded model brand or model version.

| Capability | Local-first default | Cloud conditions |
| Classify command | Deterministic grammar first, optional compact model. | Opt-in and PUBLIC only, or approved sanitized INTERNAL. |
| Generate ActionPlan | Local model when installed; schema-validated output. | Opt-in, policy-approved payload and output validation. |
| Summarize restricted context | Private model only; no silent cloud substitution. | Never permitted. |
| Generate contracts | Optional future feature under review gate. | Only public/synthetic descriptions permitted. |

## 6.2 Provider and deployment rules
Default application installation uses private/offline mode; cloud mode requires explicit setup and user consent.
A local service on loopback is not automatically air-gapped. “Offline verified” requires blocked egress and an observed network test.
Model packages and quantizations are configuration values, along with license, checksum, memory requirements and tested output modes.
A missing local model yields an actionable error or deterministic reduced-function path, never an unauthorized cloud fallback.
Provider errors, partial output and malformed JSON do not bypass executor validation.
Provider-specific data retention terms are reviewed at integration time; a local JSON flag cannot establish external service guarantees.
## 6.3 Evaluation and model selection
Maintain a versioned dataset of at least 100 representative synthetic commands with canonical expected plans and adversarial negatives. Record exact schema validity, plan accuracy, task success, p50/p95 TTFT and end-to-end latency, token throughput, memory/VRAM, and estimated cost per successful workflow. Never compare unlike hardware or token budgets.

| Hardware profile | Purpose | Evidence |
| Reference desktop | Windows 11, x64, ≥16 GB RAM; CPU-only basic IPC and deterministic workflow. | Document CPU, RAM, OS build and cold-start conditions. |
| Private-AI profile | More RAM or GPU as required by the selected local model. | Publish exact device and model quantization; no universal speed promise. |
| CI profile | GitHub Windows runner; models replaced by deterministic test doubles. | Build, unit, contracts, policy and smoke tests. |

# 07  Classification, privacy & cloud egress
## 7.1 Data labels and precedence

| Class | Source / example | Default cloud policy |
| PUBLIC | Documented public market concepts or synthetic fixtures explicitly marked public. | Possible only with explicit cloud consent. |
| INTERNAL | Non-public workflows, desk references and operational metadata. | Deny by default; optional explicit permit after robust sanitization and policy review. |
| RESTRICTED | Customer information, account identifiers, material non-public information, confidential portfolios. | Hard deny; private only. |
| UNKNOWN | Missing source classification, ambiguous data, detector failure or policy error. | Hard deny; treat as restricted. |

An explicit trusted source classification sets the minimum sensitivity. A content detector may escalate but never downgrade it. Absence of a positive classification does not imply PUBLIC. Derived outputs inherit or increase input classification unless explicitly reviewed.
## 7.2 Enforcement sequence
1. Collect typed workspace data with immutable source metadata and provenance.
2. Combine trusted source classification with deterministic detectors; optional local NER may escalate sensitivity.
3. Evaluate policy for the entire request, provider selection, attachments, logs and retries.
4. For specifically permitted INTERNAL requests, tokenize in local volatile vault and check residual sensitivity; failures deny cloud.
5. Allow cloud transmission only through a single egress adapter after final policy authorization; record metadata without payload.
6. Validate provider output; restore only known locally issued placeholders; validate planned operations before execution.
## 7.3 Privacy guarantees and limitations
No claim of 100% PII detection: rules and NER provide additional signals, not a compliance guarantee.
Pseudonymized data may still be personal or confidential; sensitivity does not automatically become PUBLIC.
Vault token mappings use memory-only storage, bounded TTL, session isolation and explicit cleanup on cancellation.
Prompt/response raw text, tokens, identifiers and model credentials are excluded from default traces and logs.
No claim that GDPR, DORA or any regulatory framework is automatically satisfied by this reference implementation.
## 7.4 Policy acceptance
The release gate includes a network-denial suite that instruments all external requests. For every RESTRICTED and UNKNOWN fixture and every failed-classifier fixture, the observed count of external model calls must be zero. This is a bounded test claim, not a universal leakage guarantee.
# 08  Security, audit & threat model
## 8.1 Threat model coverage

| Threat | Mitigation | Residual limitation |
| Same-user rogue process | Bind process/session identity, capability grant checks, allowlist app manifest. | Same-user processes remain a weak OS isolation boundary. |
| Prompt injection from app text | Treat as data only; schema, allowlist, authorization and no arbitrary tools. | Semantic manipulation of summaries still requires quality testing. |
| Sensitive-data cloud egress | Deny-by-default egress gateway; policy and network tests. | Third-party library behavior must be re-audited on update. |
| Queue amplification / IPC denial | Size limits, bounded queues, deadlines, per-client throttles. | Resource pressure within a user session remains possible. |
| Trace/log data disclosure | Minimal metadata, hashing only when justified, structured audit policy. | Local users with sufficient privileges can tamper with logs. |
| Registry / artifact tampering | Pinned packages, canonical paths, checksums, signatures where available. | Development builds are not an enterprise trust anchor. |

## 8.2 Controls and requirements
SE-01: All provider credentials must use Windows Credential Manager or secure development-only environment injection; never place real secrets in repo, sample files or telemetry.
SE-02: Only allowlisted registered apps are launchable; manifest paths are canonicalized and validated against policy.
SE-03: Short-lived app capabilities are checked on each privileged IPC operation; no permission inheritance through model instructions.
SE-04: The audit stream records timestamp, trace ID, action/policy result, redaction count and provider category; never raw sensitive prompt content.
SE-05: Audit entries are hash-chained or MAC-protected with versioned verification; “tamper-evident” does not mean immutable against an admin or local system compromise.
SE-06: Dependabot or equivalent dependency scanning, code scanning, SBOM and license notices are included before v1.0.
## 8.3 Synthetic content policy
Only fictitious issuers, independently generated reference identifiers and synthetic financial values may appear in code, screenshots, benchmarks and recordings. Test cases containing intentionally sensitive-looking patterns must be synthetic and explicitly labeled as such.
# 09  Desktop UX and window integration
## 9.1 MVP window capabilities
A command bar with visible backend mode, consent, current data classification and a cancel action.
Launch, find, focus and arrange only registered sample top-level windows; deterministic layouts avoid reparenting.
Support keyboard navigation, per-monitor scale awareness, layout persistence and repair of missing displays.
Provide explicit error state if a target process is not responsive, with safe restart and no automatic privilege elevation.
## 9.2 Cross-process reparenting is gated
SetParent/HwndHost window embedding remains experimental and is excluded from the MVP definition. A separately completed feasibility spike must document DPI-awareness combinations, focus/keyboard, modal windows, UAC/integrity level, accessibility, teardown and crash behavior. Only owned sample apps that opt into a supported hosting contract may be considered. Unsupported apps use ordinary OS window arrangement.
## 9.3 Acceptance examples

| ID | Scenario | Expected result |
| UX-01 | Launch three sample apps, rearrange across two monitors and reopen workspace. | No offscreen windows; saved layout is recovered or safely normalized. |
| UX-02 | Child app stops responding for configurable timeout. | Shell stays responsive and displays restart affordance. |
| UX-03 | No local AI model installed and cloud disabled. | Deterministic commands remain available; summary returns clear unavailable status. |

# 10  Observability, diagnostics & operations
## 10.1 End-to-end trace model
Every workflow has a trace ID covering policy evaluation, model selection, plan generation, broker registration, context delivery, window action and result verification. Traces store only payload type, byte length, category, duration, action ID and outcome. Payload previews are disabled by default and cannot bypass data policy.
## 10.2 Live Inspector scope
v0.1: a minimal local structured trace log and status list sufficient to diagnose the reference flow.
v0.2: bounded queue depth, heartbeats, failure codes, retry/denial metrics.
v0.5: opt-in WPF drawer with latency histogram, per-app timeline and redacted export.
Future contract editor: generated code is reviewed, compiled and diffed before anything is persisted; no execution of generated code.
## 10.3 Operational defaults
Telemetry egress is disabled. Local log rotation and retention defaults are documented, with user-controlled deletion. Exports are explicitly initiated and must pass the same data-classification rules as provider egress.
# 11  Non-functional quality targets
The values below are proposed acceptance targets on a pinned reference machine and dataset, not measured claims. M0 establishes a baseline, and any target changes require a documented rationale and ADR.

| ID | Target & measurement | Gate |
| NFR-01 | Local 1 KB payload, 3 apps, p95 event delivery ≤25 ms after warmup; report p50/p95/p99 and distribution. | v0.2 |
| NFR-02 | Shell main thread remains interactive; no deadlock during 100 workflow runs, crashes or disconnects. | v0.2 |
| NFR-03 | 100/100 negative authorization scenarios denied before side effects; report exact corpus. | v0.1 |
| NFR-04 | 0 observed external AI transmissions in RESTRICTED/UNKNOWN scenarios, including errors and retries. | v0.1 + v0.4 |
| NFR-05 | Plan accuracy ≥95% for versioned 100-command synthetic dataset (separate valid/unsafe classes). | v0.4 |
| NFR-06 | Full test suite and docs build on clean Windows runner, with pinned SDK/dependencies. | Every release |
| NFR-07 | Publish TTFT, end-to-end latency, tokens/sec and RAM/VRAM per model and device; no universal fixed AI latency SLA. | v0.4 |
| NFR-08 | Broker survives repeated sample-app crashes; restart restores registered state without unbounded queue growth. | v0.2 |

# 12  Verification, test strategy & acceptance
## 12.1 Test pyramid
Unit: action schemas, policy precedence, classification fail-closed behavior, registry validation and plan state machine.
Contract: Protobuf serialization, schema-version compatibility, capability validation, FDC3 context adapters.
Integration: Named Pipes handshake, broker restart, bounded queues, app identity and retries.
E2E: reference workflow across independent processes, offline/private mode, crash recovery, window layout.
Adversarial: prompt injection, malformed JSON, wrong-tenant/session tokens, synthetic PII leakage, model partial output.
Performance: BenchmarkDotNet microbenchmarks plus end-to-end traces on a pinned hardware profile.
## 12.2 Release gates and traceability

| Gate | Required evidence |
| M0 → v0.1 | IPC and docking spikes written up; app trust policy reviewed; test fixture and CI skeleton ready. |
| v0.1 exit | One fully automated three-app synthetic workflow; policy denials and offline path demonstrated. |
| v0.2 exit | Fault injection and baseline latency results; no process cascade or runaway queues. |
| v0.3 exit | Published FDC3 3.0 subset matrix and interoperability evidence. |
| v0.4 exit | Privacy egress test report, model evaluation harness and selected-model evidence. |
| v0.5 exit | Desktop UX tests, Inspector usability, and docking risk decision. |
| v0.6 exit | Generated artifacts validated by compiler/parser, developer sign-off before use. |
| v1.0 exit | Fresh-install reproduction, SBOM/license review, threat model, demos, docs and signed-off acceptance matrix. |

## 12.3 Definition of done for any feature
Requirements and non-goals linked to one or more issue IDs.
Tests demonstrate normal, failure and authorization-denied cases.
No real financial/customer data or credentials included.
Performance impact and telemetry fields reviewed when applicable.
README/Wiki/API/ADR updated and CI green.
No known unresolved security-critical failure in the feature acceptance suite.
# 13  Delivery, repository and governance
## 13.1 Proposed repository structure
src/Aether.Contracts/         # schemas and generated bindingssrc/Aether.Broker/            # IPC + lifecyclesrc/Aether.Execution/         # policy-checked plan executorsrc/Aether.Security/          # classification and egress gatesrc/Aether.AI/                # provider adapterssrc/Aether.Shell/             # Windows desktop UIsamples/                     # synthetic independent processestests/                       # unit, contract, integration, E2Ebenchmarks/                  # reproducible measurementsdocs/architecture/           # diagrams and ADRs.github/workflows/           # Windows build and test
## 13.2 Repository release checklist
Root README with problem, 3-minute demonstration, quick start and supported platform.
LICENSE chosen deliberately after dependency review; THIRD_PARTY_NOTICES and model licensing matrix.
CONTRIBUTING.md, CODE_OF_CONDUCT.md and SECURITY.md with disclosure instructions.
Threat model, limitations, benchmark methodology/results and ADR index.
Windows GitHub Actions with build, unit tests, contract tests, policy tests and reproducible smoke tests.
Release artifacts with checksums, version notes and instructions for installing optional local models.
## 13.3 Roadmap ownership
The companion Implementation & Release Plan v3.0 is the authoritative ordered backlog. The GitHub Wiki Roadmap is a publishable summary; GitHub Issues are the work units. Release tags must only follow completed exit gates. No calendar commitment is implied by release labels.
# 14  Architecture decisions and risk register
## 14.1 Initial ADR candidates

| ADR | Decision to capture | Status |
| ADR-001 | Use .NET 10 LTS and Windows-only UI. | Proposed |
| ADR-002 | Use gRPC/Protobuf over local Named Pipes. | Proposed |
| ADR-003 | Separate AI planning and deterministic execution. | Mandatory |
| ADR-004 | Enforce fail-closed data policy and cloud consent. | Mandatory |
| ADR-005 | Use top-level arrangement for MVP; evaluate owned-app docking separately. | Proposed |
| ADR-006 | Declare bounded FDC3 3.0 coverage rather than full compliance. | Proposed |
| ADR-007 | Expose no network listener and no telemetry egress by default. | Mandatory |

## 14.2 Key risks and mitigations

| Risk | Likelihood / impact | Mitigation / release decision |
| Cross-process embedding fails on DPI/focus edge cases | High / high | Gate by spike; fallback to top-level arrangement; never block MVP. |
| Model API/structured output changes | Medium / medium | Provider adapters, schema validators, pinned versions, contract tests. |
| Policy false negatives permit exposure | Medium / high | Source-first classification, fail-closed defaults, egress tests; no broad zero-leakage claim. |
| Same-user process impersonation | Medium / high | Process-bound handshake, capability checks, documented residual threat. |
| Over-scoped implementation | High / high | Single workflow v0.1, issue-scoped milestones, defer optional editor and docking. |
| Unreproducible local-model setup | Medium / medium | Document hardware, hash/license checks, deterministic CI mock and CPU fallback. |

# 15  Configuration examples & references
## 15.1 Safe example policy (illustrative JSON)
{  "policyVersion": "3.0",  "cloud": {"enabledByDefault": false, "requireExplicitConsent": true},  "dataClasses": {    "PUBLIC": {"cloud": "OPT_IN"},    "INTERNAL": {"cloud": "DENY_BY_DEFAULT"},    "RESTRICTED": {"cloud": "DENY"},    "UNKNOWN": {"cloud": "DENY"}  },  "failureMode": "FAIL_CLOSED",  "audit": {"rawPayloads": false, "localOnly": true},  "patterns": {"accountId": "ACC-[0-9]{8,12}"}}
## 15.2 ActionPlan illustration (synthetic, non-executable)
{  "schemaVersion": "1.0",  "planId": "example-workflow-001",  "steps": [    {"action": "LaunchApp", "appId": "sample.watchlist"},    {"action": "LaunchApp", "appId": "sample.risk"},    {"action": "PublishContext", "contextRef": "synthetic.instrument.001"},    {"action": "ArrangeWindows", "layout": "side-by-side"}  ]}
Examples must pass automatic JSON parsing and schema validation before inclusion in the repository. This example intentionally excludes executable paths and confidential content.
## 15.3 Primary reference documentation
Microsoft .NET support policy — https://dotnet.microsoft.com/en-us/platform/support/policy
FDC3 3.0 specification (FINOS) — https://fdc3-new.finos.org/docs/fdc3-standard
gRPC over Named Pipes (.NET 10) — https://learn.microsoft.com/en-us/aspnet/core/grpc/interprocess-namedpipes?view=aspnetcore-10.0
Kestrel Named Pipes security considerations — https://learn.microsoft.com/en-us/aspnet/core/fundamentals/servers/kestrel/security-considerations
Windows SetParent documentation — https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-setparent
GitHub Wiki documentation — https://docs.github.com/en/communities/documenting-your-project-with-wikis/about-wikis
GitHub Issues documentation — https://docs.github.com/en/issues/tracking-your-work-with-issues/about-issues
Research and standards snapshot: 14 November 2026. All external compatibility and service claims should be rechecked against the provider or standards body before implementation and release. This specification is a design proposal, not an implemented product.