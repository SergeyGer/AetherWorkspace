

# AetherWorkspace
Implementation & Release Plan
Version 3.0  |  Proposed design  |  14 November 2026

| Status | Design baseline; implementation remains subject to test evidence |
| Audience | Open-source contributors, maintainers and technical readers |
| Scope | Vendor-neutral desktop interoperability and policy-aware AI workflows |

## Publication note
AetherWorkspace is an independent, vendor-neutral reference implementation. All examples use synthetic data. Security and performance statements in this document are engineering objectives, not certifications or measured results.

# Document map
01  Delivery strategy and baseline
02  Release dependency map
03  M0 feasibility and engineering foundations
04  v0.1.0 minimum viable product
05  v0.2.0 reliability and observability
06  v0.3.0 bounded FDC3 interoperability
07  v0.4.0 advanced privacy and AI evaluation
08  v0.5.0 desktop experience and diagnostics
09  v0.6.0 developer contract studio
10  v1.0.0 release hardening
11  GitHub Milestones, Issues & Wiki workflow
12  Repeatable acceptance and release checklist

# 01  Delivery strategy and baseline
This companion plan translates AetherWorkspace Technical Specification v3.0 into sequential GitHub Issues. Each milestone has entry conditions, concrete tasks, deliverables and a release gate. Milestones are capability-based, not time commitments. The order is deliberately risk-first, end-to-end-first, then hardening and optional scope.
Two repositories are not required: implementation, Wiki sources and issue backlog can begin in one GitHub repository. GitHub Issues represent actionable work; a Wiki Roadmap explains the path at product level. Issue IDs AW-001…AW-038 are stable planning identifiers, not GitHub issue numbers.
## 1.1 Development operating principles
Do not start secondary features until the preceding milestone gate passes.
Choose one vertical end-to-end scenario for v0.1.0; avoid partial horizontal implementations across six subsystems.
Every AI plan is untrusted; no model directly executes processes, RPCs or files.
No actual customer, position, transaction or portfolio records in code or demos.
Cloud is disabled by default. RESTRICTED/UNKNOWN always takes a private or denied path.
Use a fixed Windows reference environment for benchmarks; report actual measurements rather than aspirational guarantees.
Code, tests, security evidence, documentation and release acceptance advance together.

Figure 1. Milestone sequence. Optional functionality never blocks an earlier release gate.
## 1.2 Key deliverable hierarchy

| Artifact | Source of truth |
| Product/architecture requirements | Technical Specification v3.0 (this pack) |
| Ordered development steps | Implementation & Release Plan v3.0 |
| Changeable engineering tasks | GitHub Issues and Milestones |
| Public release roadmap | GitHub Wiki / Roadmap page |
| Architecture tradeoffs | ADRs committed alongside code |
| Evidence | CI logs, tests, benchmarks, synthetic E2E demonstrations |

# 02  Release dependency map

| Milestone | Product result | Issue range |
| M0 | Feasibility & engineering foundation | AW-001–AW-006 |
| v0.1.0 | MVP — three-app policy-aware workflow | AW-007–AW-015 |
| v0.2.0 | Reliability & measured performance | AW-016–AW-020 |
| v0.3.0 | Scoped FDC3 3.0 interoperability | AW-021–AW-024 |
| v0.4.0 | Advanced privacy & model evaluation | AW-025–AW-029 |
| v0.5.0 | Desktop UX & inspectability | AW-030–AW-033 |
| v0.6.0 | Review-only contract generation | AW-034–AW-035 |
| v1.0.0 | Public, reproducible release | AW-036–AW-038 |

## 2.1 Critical path
M0 contracts/IPC/security → v0.1 Broker and independent apps → guarded executor → provider routing → automated E2E workflow → reliability hardening → FDC3 subset evidence → privacy regression → UI diagnostics → final release. Any external-window embedding, broad provider catalog or contract generation is optional until its own milestone.
## 2.2 GitHub issue workflow states
Backlog → Ready (dependencies met) → In Progress → In Review → Verified → Closed. An issue is not complete when code is merged: documented acceptance tests, security review and reproduction evidence are required. Use GitHub Projects for a Kanban board if desired; the Wiki is the narrative roadmap.
# 03  M0 feasibility and engineering foundations
## Objective
Demonstrate that fundamental transport/security choices are viable and establish failure-mode baselines.
Entry: No active implementation dependencies.
Implementation guidance: Keep spikes small and reproducible; document decisions before production-like abstractions.
## Ordered implementation steps
### Step 1 — AW-001: Bootstrap .NET 10 solution and CI skeleton
Create a supported, reproducible Windows build baseline.
Create solution, global.json, Directory.Packages.props and editorconfig.
Add src/tests/benchmarks/docs folders and Windows build workflow.
Enable warnings-as-errors on owned code and unit-test discovery.
Exit evidence: Fresh Windows runner restores, builds and runs placeholder unit suite.; No hardcoded secrets; exact SDK and tool versions documented.
### Step 2 — AW-002: Define versioned contracts and synthetic data fixtures
Specify concrete message and plan contracts before implementation.
Write protobuf envelope, app manifest and ActionPlan JSON schemas.
Define four classification labels and end-to-end provenance fields.
Create three independent synthetic financial data fixtures.
Exit evidence: JSON examples validate; protobuf compiles from clean checkout.; Fixtures contain no real personal or financial data.
Prerequisites: AW-001
### Step 3 — AW-003: Prototype gRPC/Named Pipes transport and authenticated handshake
Test feasibility of the local broker transport and session binding.
Implement minimal gRPC/HTTP2 Named Pipe server and client.
Evaluate current-user pipe ACLs and process identity binding.
Benchmark 1 KB request/reply with trace IDs.
Exit evidence: Transport works across two independent processes.; Document threat boundary and supported Windows build.
Prerequisites: AW-001, AW-002
### Step 4 — AW-004: Run cross-process docking feasibility spike
Decide if HWND embedding deserves a later optional milestone.
Test two owned windows under DPI variations, modal dialogs and keyboard focus.
Record crash, teardown and accessibility behavior.
Write go/no-go ADR with top-level arrangement fallback.
Exit evidence: Spike report and reproducible samples are committed.; No dependency on docking remains for MVP.
Prerequisites: AW-001
### Step 5 — AW-005: Publish threat model, default policy and constraints
Capture trust boundaries before AI integrations.
Model IPC impersonation, injection, cloud exfiltration and log disclosure.
Define deny-by-default PUBLIC/INTERNAL/RESTRICTED/UNKNOWN policy.
Set consent and capability rules; document residual risks.
Exit evidence: Threat model includes mitigations and residual limitations.; UNKNOWN always denies cloud in policy unit tests.
Prerequisites: AW-002
### Step 6 — AW-006: Create benchmark and golden workflow harness
Establish reproducible baselines before performance claims.
Create stopwatch trace harness, fixture corpus and manual task script.
Specify reference PC, p50/p95/p99 measurement protocol.
Add smoke launch scenario to Windows CI.
Exit evidence: Benchmark methodology and raw schema are documented.; Initial baseline recorded without unsupported performance claims.
Prerequisites: AW-003
## Milestone gate
Clean build, IPC spike, HWND decision, policy baseline and benchmark method.
Required: green CI for implemented requirements; updated README/Wiki; security and regression review; recorded evidence linked from milestone or release notes.
# 04  v0.1.0 minimum viable product
## Objective
Ship one complete policy-aware command driving three separate synthetic desktop processes.
Entry: All M0 gate evidence exists.
Implementation guidance: Prefer top-level window arrangement, single supported command, fixed schema and one local adapter.
## Ordered implementation steps
### Step 1 — AW-007: Implement allowlisted app registry and process launcher
Broker discovers and launches only authorized demo apps.
Parse and validate app registry manifest.
Enforce canonical executable paths and fixed argument templates.
Provide launch timeouts and error results.
Exit evidence: Authorized sample app launches; path traversal/unknown app denied.; No arbitrary path from model input is used.
Prerequisites: AW-002, AW-003, AW-005
### Step 2 — AW-008: Implement broker registration and capability handshake
Bind caller sessions to trusted sample app instances.
Design registration handshake and bounded session TTL.
Verify process identity and map to approved capabilities.
Deny spoofed identity and replayed session tokens.
Exit evidence: Authenticated sample apps register; negative tests denied.; Every privileged call includes capability authorization.
Prerequisites: AW-007
### Step 3 — AW-009: Build three independent synthetic sample apps
Create visible cross-process context propagation targets.
Build Watchlist WPF process, Risk WPF process and chart module.
Include typed context handlers with source metadata.
Generate deterministic synthetic holdings and summaries.
Exit evidence: Each sample app starts independently and renders fixture data.; No actual market data integrations.
Prerequisites: AW-002, AW-008
### Step 4 — AW-010: Build basic WPF Shell and top-level window arrangement
Provide a usable desktop without reparenting.
Create command/status toolbar and window registry.
Implement open/focus/side-by-side arrangement for registered windows.
Handle absent/closed window and cancellation.
Exit evidence: Three windows can be launched and arranged on reference desktop.; Shell remains responsive when child process terminates.
Prerequisites: AW-007, AW-009
### Step 5 — AW-011: Implement typed context channels and trace propagation
Synchronize synthetic instrument context across apps.
Implement protobuf context broadcasts on a named default channel.
Preserve message ID, trace ID, source and classification.
Provide cross-process E2E assertion.
Exit evidence: Selecting an instrument in Watchlist updates two other apps.; Every event has source and correlation metadata.
Prerequisites: AW-008, AW-009
### Step 6 — AW-012: Implement guarded ActionPlan validator and executor
Do not let model output invoke arbitrary commands.
Define allowlisted LaunchApp/PublishContext/ArrangeWindows/SummarizeContext actions.
Validate schema, authorization, action count and deadlines.
Implement explicit states, cancellation and structured error handling.
Exit evidence: Valid plans execute deterministically.; 100 documented unsafe plans produce no side effects.
Prerequisites: AW-005, AW-007, AW-011
### Step 7 — AW-013: Add command bar and pluggable planning adapter
Connect user command to typed plan without direct tool execution.
Support deterministic command parser for reference workflow.
Add one optional configured cloud planning adapter for PUBLIC synthetic input.
Make cloud opt-in and validate model outputs before executor.
Exit evidence: Reference command yields correct ActionPlan via deterministic path.; Cloud mode requires explicit consent and denied payloads never reach provider.
Prerequisites: AW-010, AW-012
### Step 8 — AW-014: Implement minimal privacy gateway and private local summary
Demonstrate a genuinely private route for restricted synthetic context.
Implement source-first classification and UNKNOWN fail-closed.
Provide one local inference adapter with configurable model/endpoint.
Block external provider calls for RESTRICTED and UNKNOWN, including retries.
Exit evidence: Private demo summary can run on documented local-AI hardware.; Network test observes zero cloud AI calls for RESTRICTED/UNKNOWN.
Prerequisites: AW-005, AW-012, AW-013
### Step 9 — AW-015: Automate MVP end-to-end workflow and quick-start demo
Publish a complete, repeatable vertical slice.
Script three-app synthetic workflow including denial test.
Add mock-based Windows CI E2E and an optional local-model manual test.
Write quick start, screenshots and MVP limitations.
Exit evidence: New checkout reproduces the workflow with tests.; Acceptance screenshots and trace ID demonstrably correspond to same workflow.
Prerequisites: AW-010, AW-011, AW-012, AW-014
## Milestone gate
Demo runs end-to-end and enforces zero external calls on restricted/unknown test fixtures.
Required: green CI for implemented requirements; updated README/Wiki; security and regression review; recorded evidence linked from milestone or release notes.
# 05  v0.2.0 reliability and observability
## Objective
Prove deterministic behavior under bursts, crashes, offline operation and routine debugging.
Entry: MVP workflow and denial regression are green.
Implementation guidance: No performance claim before measured reference data; limit retries and queues.
## Ordered implementation steps
### Step 1 — AW-016: Add bounded queues, ordering, backpressure and deduplication
Handle bursts and partial delivery failures explicitly.
Add per-channel queue bounds and overflow result.
Define ordering guarantees and retry eligibility.
Add cancellation/deadline and deduplication tests.
Exit evidence: Overload does not cause unbounded memory growth.; Retry does not duplicate documented idempotent actions.
Prerequisites: AW-011, AW-012
### Step 2 — AW-017: Add heartbeat, fault isolation and controlled restart
Recover from expected child app failures.
Implement health states and missed-heartbeat rules.
Provide restart action with bounded attempts.
Inject app crashes, hangs and broker disconnects.
Exit evidence: Shell and Broker survive child crash tests.; UI reports deterministic failure state and safe restart path.
Prerequisites: AW-015, AW-016
### Step 3 — AW-018: Measure IPC, UI responsiveness and resource limits
Replace aspirational speed claims with evidence.
Run warmed 1 KB, three-app p50/p95/p99 benchmark.
Record CPU/memory allocations and GC metrics.
Document hardware, variance, instrumentation and regressions.
Exit evidence: BENCHMARKS.md contains raw reproducible protocol and result.; Any target miss has issue/ADR, not a hidden exception.
Prerequisites: AW-006, AW-016
### Step 4 — AW-019: Verify offline-mode network isolation and failure behavior
Distinguish private routing from genuine offline operation.
Add private mode UI and provider health status.
Run with network egress blocked; test missing local runtime.
Check no unauthorized fallback to cloud.
Exit evidence: Offline-supported commands run with egress disabled.; Uninstalled model yields safe error without unexpected cloud call.
Prerequisites: AW-014
### Step 5 — AW-020: Build minimal tamper-evident local audit chain
Expose accountable, redacted execution outcomes.
Define structured audit schema and HMAC/hash-chain verification.
Exclude prompt and raw PII, add retention settings.
Document local-admin limitations and rotation semantics.
Exit evidence: Audit verifier detects a modified historical test entry.; Secret and PII fixtures do not appear in logs.
Prerequisites: AW-012, AW-014
## Milestone gate
Backpressure, crash recovery, offline verification and audit evidence.
Required: green CI for implemented requirements; updated README/Wiki; security and regression review; recorded evidence linked from milestone or release notes.
# 06  v0.3.0 bounded FDC3 interoperability
## Objective
Expose a documented, testable subset of FDC3 3.0 with provenance metadata.
Entry: Stable broker and context transport with trace IDs.
Implementation guidance: Conformance is limited to explicitly listed API/context/intent surfaces.
## Ordered implementation steps
### Step 1 — AW-021: Document FDC3 3.0 supported-subset matrix
Specify exactly what the project does and does not implement.
Map Desktop Agent API, channel, context and intent surfaces.
List unsupported methods and deviations.
Add schema/version compatibility expectations.
Exit evidence: Matrix is published and reviewed against official standard.; No blanket FDC3 compliance claim.
Prerequisites: AW-015
### Step 2 — AW-022: Implement supported FDC3 contexts and channels
Extend context interoperability beyond a custom demo contract.
Create context mappings and validator tests.
Implement selected user/app channel semantics.
Propagate trace ID, timestamp and source metadata.
Exit evidence: Selected contexts round-trip without unsupported field loss.; Source metadata is preserved across owned apps.
Prerequisites: AW-021, AW-011
### Step 3 — AW-023: Implement limited intent resolution and app directory
Support bounded standard-intent workflows.
Add selected raise-intent and resolution flow.
Map app-registry entries to declared App Directory subset.
Ensure existing capability checks apply after resolution.
Exit evidence: Supported intent resolves to expected registered sample target.; Unknown intent and unapproved target are rejected.
Prerequisites: AW-021, AW-022, AW-008
### Step 4 — AW-024: Validate FDC3 subset with contract and external tests
Back interoperability claims with evidence.
Create standards fixture suites with provenance checks.
Test version mismatch and unsupported behavior.
Attempt independent implementation interop where practicable.
Exit evidence: Conformance matrix contains pass/fail/unsupported evidence.; No unsupported behavior silently masquerades as supported.
Prerequisites: AW-022, AW-023
## Milestone gate
Declared subset with provenance and independent contract evidence.
Required: green CI for implemented requirements; updated README/Wiki; security and regression review; recorded evidence linked from milestone or release notes.
# 07  v0.4.0 advanced privacy and AI evaluation
## Objective
Add local sensitivity detection, controlled tokenization and evidence-based provider selection.
Entry: Core policy/egress tests and FDC3 schema baseline are stable.
Implementation guidance: NLP classifiers can escalate but not downgrade; source classification always governs.
## Ordered implementation steps
### Step 1 — AW-025: Add optional local entity detection and sensitivity escalation
Improve classification while keeping deterministic rules authoritative.
Integrate optional local NER/PII detector behind interface.
Create synthetic adversarial entities and false-negative review.
Prove detector can escalate but never downgrade trusted classification.
Exit evidence: Failure leads to UNKNOWN/RESTRICTED cloud denial.; Publish precision/recall only with labeled dataset and setup.
Prerequisites: AW-014, AW-020
### Step 2 — AW-026: Implement session-scoped reversible token vault
Enable explicitly authorized sanitization without claiming anonymity.
Add volatile token mappings scoped to session and TTL.
Reject unknown/unissued placeholders on response.
Add cancellation cleanup and output rehydration guard.
Exit evidence: No raw sensitive values appear in cloud test doubles.; Expired or foreign tokens cannot be rehydrated.
Prerequisites: AW-025
### Step 3 — AW-027: Implement capability-based model router
Select models based on task, availability, policy and hardware.
Define provider capability registry and local health probes.
Implement routing with cloud opt-in, fail-closed restrictions and fallback matrix.
Keep exact model IDs in config and document license metadata.
Exit evidence: Restricted tasks never route to cloud when local unavailable.; Provider swap does not alter executor or broker code.
Prerequisites: AW-014, AW-026
### Step 4 — AW-028: Publish model evaluation suite and cost/latency study
Provide reproducible model-selection evidence.
Build ≥100 command labeled corpus and score exact plan correctness.
Measure TTFT, end-to-end latency, tokens/sec and model footprint.
Compare deterministic, small local and cloud strategies with equivalent tasks.
Exit evidence: Report includes hardware, model versions, uncertainty and raw outputs scrubbed.; Selected model policy justified by evidence rather than brand.
Prerequisites: AW-027, AW-006
### Step 5 — AW-029: Add egress instrumentation and privacy adversarial regression
Test real network requests, not only mocked provider functions.
Intercept and count external requests in controlled test environment.
Exercise errors, retries, unknown classifications and injection fixtures.
Publish threat-to-test traceability summary.
Exit evidence: Zero external AI calls in every RESTRICTED/UNKNOWN test case.; All known egress paths are enumerated and monitored.
Prerequisites: AW-025, AW-026, AW-027
## Milestone gate
NER/vault/routing guarded by egress regression and model performance report.
Required: green CI for implemented requirements; updated README/Wiki; security and regression review; recorded evidence linked from milestone or release notes.
# 08  v0.5.0 desktop experience and diagnostics
## Objective
Improve layouts, triage tools and redacted trace visibility.
Entry: Reliability and privacy guards exist; docking spike verdict is written.
Implementation guidance: Optional window embedding is isolated and retains top-level fallback.
## Ordered implementation steps
### Step 1 — AW-030: Optionally integrate owned-window docking after go decision
Keep experimental window embedding isolated from core workflow.
Read AW-004 ADR and recheck Windows versions.
If feasible, implement an opt-in owned-app contract and teardown fallback.
Otherwise explicitly close as not planned and retain top-level layout.
Exit evidence: No third-party arbitrary HWND support claim.; Focus/DPI/modal/crash tests pass or feature stays disabled.
Prerequisites: AW-004, AW-017
### Step 2 — AW-031: Implement durable multi-monitor workspace layouts
Improve repeatability and desktop recovery.
Persist layout with monitor identity and scale hints.
Clamp restored windows to visible work areas.
Test monitor removal, DPI changes and corrupted JSON.
Exit evidence: Layout recovers across two tested display topologies.; No invisible/offscreen sample window after restore.
Prerequisites: AW-010, AW-017
### Step 3 — AW-032: Build Live Inspector with redacted trace timeline
Expose debugging without leaking content.
Create WPF drawer for action timeline, IPC timings and heartbeats.
Provide drill-down by trace ID and compact histogram.
Prohibit raw sensitive payload preview by policy.
Exit evidence: Reference workflow is inspectable by trace ID.; Synthetic PII does not appear in export or UI detail.
Prerequisites: AW-020, AW-018, AW-031
### Step 4 — AW-033: Add local diagnostics export and support bundle
Make reproducible incident reports possible.
Define sanitized JSON export with user-initiated confirmation.
Add support bundle metadata and retention controls.
Test filename/path escaping and export classification.
Exit evidence: Bundle includes reproducer metadata and no confidential values.; Export off by default; no telemetry uploads.
Prerequisites: AW-032, AW-029
## Milestone gate
Multi-monitor restore and inspector; owned-app docking only if spike succeeded.
Required: green CI for implemented requirements; updated README/Wiki; security and regression review; recorded evidence linked from milestone or release notes.
# 09  v0.6.0 developer contract studio
## Objective
Offer a safe draft-and-review tool for contract files.
Entry: Stable contracts, inspector and threat controls.
Implementation guidance: Generated code never executes or mutates live registry without review.
## Ordered implementation steps
### Step 1 — AW-034: Build review-only contract generation assistant
Generate bounded drafts, never executable artifacts directly.
Create templates for proto, registry and typed client boilerplate.
Constrain prompt to documented public/synthetic schemas.
Require explicit diff review and disk write approval.
Exit evidence: Draft is marked untrusted and cannot change active registry.; No auto-compilation or execution before explicit review.
Prerequisites: AW-024, AW-029, AW-032
### Step 2 — AW-035: Validate generated schemas before acceptance
Ensure generated artifacts are reproducible and syntactically safe.
Run protoc/JSON schema checks in isolated build step.
Detect name collisions, breaking contract changes and forbidden paths.
Store tests and explanation with generated diff.
Exit evidence: Invalid or breaking output is rejected with actionable diagnostics.; Approved generated code passes contract tests.
Prerequisites: AW-034
## Milestone gate
Generated artifacts validated and reviewed before use.
Required: green CI for implemented requirements; updated README/Wiki; security and regression review; recorded evidence linked from milestone or release notes.
# 10  v1.0.0 release hardening
## Objective
Publish a reproducible, documented and honest public release.
Entry: All blocking milestone criteria are met or explicitly removed from v1.0 scope.
Implementation guidance: Proof and known limitations outrank marketing claims.
## Ordered implementation steps
### Step 1 — AW-036: Package reproducible Windows distribution and supply-chain evidence
Ship a verifiable public build.
Choose distribution format and automate release artifact build.
Publish hashes, SBOM, dependency/license inventory.
Test clean Windows installation and uninstall.
Exit evidence: Artifact hashes match documented release.; Clean-machine setup does not require private credentials.
Prerequisites: AW-015, AW-024, AW-029, AW-033
### Step 2 — AW-037: Finalize public docs, Wiki and narrated demonstration
Present verified product claims and reproducible use.
Write README, architecture, security, limitations, quick start, glossary and ADRs.
Record a short demo using only synthetic content.
Review usability, keyboard navigation and source/license attribution.
Exit evidence: Documentation links match tagged release and commands work.; Demo contains no real confidential records or affiliation claims.
Prerequisites: AW-036, AW-035
### Step 3 — AW-038: Run final release gate and publish acceptance report
Close measurable requirements before version tag.
Run full E2E/reliability/egress suite and benchmark on reference setup.
Document deviations and remaining non-critical risks.
Create version notes and tagged-source acceptance matrix.
Exit evidence: All release-blocking requirements pass with evidence.; No unverified compliance, performance or interoperability claim.
Prerequisites: AW-036, AW-037
## Milestone gate
Clean install, SBOM/license review, docs, acceptance report and tagged build.
Required: green CI for implemented requirements; updated README/Wiki; security and regression review; recorded evidence linked from milestone or release notes.
# 11  GitHub Milestones, Issues & Wiki workflow
## 11.1 Initial configuration
1. Create one GitHub repository with Issues and Wiki enabled (enable Wiki where account/repository settings permit).
2. Create eight GitHub Milestones matching the release names. No due dates are necessary until capacity is known.
3. Create common labels: foundation, contracts, ipc, broker, execution, ai, privacy, security, fdc3, windows, ui, observability, reliability, testing, benchmark, documentation, spike, samples, developer-tools, release.
4. Create issues using the Markdown files in the companion GitHub Planning Kit. Issue titles embed stable AW IDs. Apply milestone, labels and dependency links.
5. Publish Wiki Home, Roadmap, Architecture, Development Guide, Release Criteria and Decision Log from supplied Markdown pages.
6. Set a GitHub Projects board if useful; filter by milestone and Ready/In Progress/In Review/Verified. Milestones contain outcomes, not arbitrary time promises.
## 11.2 Issue lifecycle and dependencies
Each issue Markdown file contains a description, ordered tasks, acceptance criteria, stable prerequisite IDs and verification steps. AW IDs are not GitHub issue numbers: after import, replace stable IDs with actual linked issues to enable GitHub native dependency tracking. The optional script included in the pack is dry-run by default and requires explicit --apply to make remote changes.
## 11.3 Release process
1. Freeze the target issue list and verify the milestone gate with a written checklist.
2. Run security, privacy, E2E and benchmark test suites; collect evidence artifacts.
3. Update Wiki Roadmap: Planned → In progress → Released only after passing the gate.
4. Create CHANGELOG notes, source tag and distribution artifacts; cite remaining known limitations.
# 12  Repeatable acceptance and release checklist
## 12.1 Completion checklist
Requirement and non-goal mapping reviewed; issue dependencies satisfied.
Normal path, failure path and unauthorized path tested.
Cloud egress tests include RESTRICTED and UNKNOWN failures and retries.
No synthetic examples accidentally contain real customer or proprietary information.
Windows CI reproducible; SDK, packages and provider config pinned.
Architecture decision, README and Wiki updated as applicable.
Performance figures include hardware and methodology; untested claims removed.
Security limitations and unresolved lower-severity issues documented.
## 12.2 Suggested first implementation session
1. Start with AW-001 and AW-002 in parallel only where contracts do not depend on repository setup.
2. Complete AW-003 transport spike and AW-005 threat/policy model before broker lifecycle implementation.
3. Run AW-004 window-hosting spike independently; publish decision even if embedding fails.
4. Start AW-007 through AW-012 in dependency order, producing a deterministic non-AI workflow first.
5. Then add AW-013/014 AI adapters and private-route enforcement; close MVP only with AW-015 automated E2E.
## 12.3 Out-of-scope and scheduling guidance
Estimates are intentionally not assigned: staffing, machine availability, model evaluation and Windows integration risks can change the critical path. After M0, estimate each issue in person-days or story points using observed throughput. Any new request should enter a triaged GitHub Issue, not silently expand an existing milestone.