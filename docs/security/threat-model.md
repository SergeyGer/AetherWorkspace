# AetherWorkspace threat model

| Field | Value |
|---|---|
| Status | Implementation-aligned threat model for the v0.1.0 development baseline; no release tag exists. |
| Design baseline | [Technical Specification v3.0](../planning/technical-specification-v3.0.md) §3.2, §7, §8 and [AW-005](../planning/github-planning-kit/issues/AW-005-publish-threat-model-default-policy-and-constraints.md). |
| Audience | Contributors, security reviewers and operators of the synthetic reference workflow. |
| Last reviewed | 14 November 2026. |

This is a **bounded engineering threat model**, not a certification, audit opinion,
compliance statement or universal leakage guarantee. The design intent is described in
the specification; the implementation evidence is limited to what the repository and its
tests demonstrate. Claims in this document are deliberately narrower than “secure”:

- the supported platform baseline is local, single-user, Windows 11 x64 with a Linux
  headless test substitute;
- all data in the repository is synthetic and fictitious;
- “denied” means denied by the implemented control path, not proven impossible under a
  compromised OS account;
- “tamper-evident” is not “immutable”, and “local” is not automatically “air-gapped”.

The companion [limitations](../limitations.md) and
[implementation status](../IMPLEMENTATION_STATUS.md) pages remain authoritative for
what is and is not built.

## 1. Scope and non-goals

### 1.1 In scope
The local gRPC/HTTP2 broker over a named pipe (Windows) or Unix domain socket (Linux),
including registration, session TTL, capability checks and channel routing; the
deterministic `ActionPlan` pipeline (strict parsing, allowlist, capability authorization,
bounded execution, cancellation, deadlines and audit); classification, deny-by-default
cloud policy, consent and the egress gateway; the registry/launcher; metadata-only
hash-chained audit; the optional cloud and loopback model adapters; the headless shell,
broker app and three headless sample roles used by the reference workflow and E2E tests.

### 1.2 Out of scope
OS compromise, local administrator/root, physical access and debugging/ptrace (see §7 for
the audit consequence); cloud-provider internals and retention terms; model quality,
factual correctness and semantic prompt-injection resistance beyond the structural plan
constraints; compliance certification, enterprise identity, multi-tenancy, a hosted
service or a network deployment (there is no network listener); FDC3 beyond the documented
bounded subset; WPF/WebView2 rendering security; **cross-process docking/reparenting,
which is excluded from the MVP and not evaluated**; real customer or portfolio data; and
measured performance, model-quality percentages or universal zero-leakage claims.

## 2. Assets

| Asset | Why it matters | Where it lives |
|---|---|---|
| Workspace data: context payloads/source metadata, operator command, plan and model output | May be `RESTRICTED`; disclosure or downgrade is the primary privacy concern, and model output is untrusted | Trusted catalog, channels, shell and planner requests; audit stores byte length only |
| Credentials: registration/session/operator tokens and provider API keys | Bearer material for same-user IPC or external provider use; theft enables impersonation or cost | Child environment, gRPC metadata, handshake file, environment-based secret provider |
| Audit chain and trace metadata | Reconstructs who did what without storing payloads | In-memory chain plus optional local JSONL file |
| Trusted configuration/artifacts: registry, context catalog, executable roots, model files | Define what may launch and what may be read; tampering changes the trust decision | Local JSON files, broker command-line configuration and local model installation |
| Broker/shell availability and reported window metadata | Loss breaks the workflow; windows must belong to the authenticated app instance | Broker process, Kestrel host, bounded queues, `WindowRegistry` |

## 3. Actors

| Actor | Position and capability | Trust assumption |
|---|---|---|
| Local operator | Starts the broker/shell, selects data classification, grants cloud consent, reads outputs | Trusted for local configuration and for the classification they explicitly attach to a request; not trusted to be infallible |
| Same-user rogue process | Same OS user; can read user files and inherited environment, connect to local IPC, race registration | **Untrusted.** Not separated by an OS security boundary from the legitimate apps |
| Registered sample app | Authenticated with capabilities granted by the manifest; can publish/subscribe allowed context and report its own windows | Only as trusted as the registry and the launched binary; a compromised app has its granted capabilities |
| Content author / prompt injector | Controls text that may reach a planner or summary provider (command, pasted content, future app text) | Untrusted data, never system instructions |
| Cloud model provider | Receives only policy-approved payloads and returns untrusted text | Untrusted output; external service with its own retention/security posture |
| Local model or plugin dependency | Separate process/service on a configured loopback endpoint; may have its own network or file behaviour | Must be verified by the operator; no automatic air-gap claim |
| Local administrator / root | Can read or modify processes, files, binaries and audit logs | Outside the protection boundary; can defeat audit and policy controls |
| Supply-chain attacker | Modifies packages, build artifacts, model files or the registry | Mitigated only by pinning and local file permissions in this baseline |
| Remote network attacker | No TCP listener is exposed by the broker; the shell does not listen | Outside the current attack surface; cloud egress is outbound only |

## 4. Trust boundaries T1–T5

The five boundaries are taken from the specification §3.2. The table maps each boundary
to the concrete controls in this repository.

| Boundary | What crosses it | Concrete implementation controls | Residual note |
|---|---|---|---|
| **T1** UI/app content is untrusted input | Operator command, catalog content, future app text entering classification, planning or summarization | [`SourceFirstClassifier.cs`](../../src/Aether.Security/Classification/SourceFirstClassifier.cs) treats missing source classification as `UNKNOWN` and only escalates on detector findings; plans reference catalog ids rather than payloads and the broker resolves the payload ([`BrokerPublishContextHandler.cs`](../../src/Aether.Workflow/Handlers/BrokerPublishContextHandler.cs)); the planner currently receives the command, not arbitrary app payloads | Structural containment only; semantic manipulation of summaries is not solved. `UNKNOWN` input can still drive the deterministic local path; it cannot be published or sent to cloud. |
| **T2** AI plans are untrusted proposals | Model or supplied `ActionPlan` entering deterministic validation and execution | Strict JSON with unknown fields rejected ([`AetherJson.cs`](../../src/Aether.Contracts/Json/AetherJson.cs)); flat typed actions only ([`ActionPlan.cs`](../../src/Aether.Contracts/Models/ActionPlan.cs), [`action-plan.schema.json`](../contracts/action-plan.schema.json)); allowlist and unsafe-content checks ([`ActionPlanValidator.cs`](../../src/Aether.Execution/Validation/ActionPlanValidator.cs)); capability authorization ([`PlanAuthorizationPolicy.cs`](../../src/Aether.Execution/Authorization/PlanAuthorizationPolicy.cs)); bounded state machine and deadlines ([`PlanExecutor.cs`](../../src/Aether.Execution/Executor/PlanExecutor.cs)); no paths, URLs or shell commands in the model | A plan that survives validation can still perform any allowed action, within the manifest capabilities and action budget. Validation is not a semantic intent check. |
| **T3** All provider egress goes through one deny-by-default gateway | Workspace data leaving the process toward a provider, including retries, errors and telemetry | [`PolicyEngine.cs`](../../src/Aether.Security/Policy/PolicyEngine.cs) evaluates cloud enablement, consent and classification; [`EgressGateway.cs`](../../src/Aether.Security/Egress/EgressGateway.cs) denies before invoking the transport and counts denied/external attempts; the cloud adapters ([`CloudPlanProvider.cs`](../../src/Aether.AI/Planner/CloudPlanProvider.cs), [`CloudSummaryProvider.cs`](../../src/Aether.AI/Summarization/CloudSummaryProvider.cs)) always call the gateway; no network listener is created by [`BrokerHost.cs`](../../src/Aether.Broker/BrokerHost.cs) | The gateway is an application-level choke point, not an OS firewall. Any code path that bypasses it, a child process, a dependency, or the configured local model can still perform network I/O. See §5.1. |
| **T4** Same-user IPC does not establish app trust | gRPC metadata, registration token, process identity, requested app ID and capabilities | [`BrokerAuthInterceptor.cs`](../../src/Aether.Broker/Hosting/BrokerAuthInterceptor.cs) requires a valid session except `Register` and `HealthProbe`; [`SessionManager.cs`](../../src/Aether.Broker/Sessions/SessionManager.cs) uses single-use 256-bit tokens, 30-second pending TTL, constant-time secret comparison and process identity binding; [`BrokerGrpcService.cs`](../../src/Aether.Broker/Hosting/BrokerGrpcService.cs) checks capabilities, channels, context types and classifications on every privileged RPC; manifest grants come from [`AppRegistry.cs`](../../src/Aether.Broker/Registry/AppRegistry.cs) | Same-user processes remain a weak OS boundary. Identity is resolved from a client-supplied PID at registration, not from transport peer credentials; after registration the session secret is a bearer token. See §5.1. |
| **T5** Local model and plugin processes are separate dependencies | Data sent to a loopback inference service; its configuration, model files and network behaviour | [`LoopbackLocalModelTransport.cs`](../../src/Aether.AI/Providers/LoopbackLocalModelTransport.cs) sends only to the configured endpoint; [`LocalModelSummaryProvider.cs`](../../src/Aether.AI/Summarization/LocalModelSummaryProvider.cs) routes through the egress gateway and is classified local; the deterministic provider ([`LocalDeterministicSummaryProvider.cs`](../../src/Aether.AI/Summarization/LocalDeterministicSummaryProvider.cs)) performs no network I/O | Loopback is not automatically air-gapped, and there is no plugin host in this baseline. Operator verification and, ideally, OS-level network blocking are required before an “offline verified” claim. |

## 5. Threat table

Evidence labels: **direct** means a test asserts the named control; **partial** means the
control is exercised indirectly or only with a test double; **none** means there is no
test for that specific path. The full evidence discussion is in §10 and §11.

| # | Threat | Attack path | Implemented mitigation | Verified by | Residual limitation |
|---|---|---|---|---|---|
| 1 | Same-user rogue process / IPC impersonation | Same user connects to the local socket/pipe, forges metadata, replays a registration token, claims a launched PID, or reads the handshake file and registers as the operator | Session requirement and validation in [`BrokerAuthInterceptor.cs`](../../src/Aether.Broker/Hosting/BrokerAuthInterceptor.cs); single-use token, pending TTL, SHA-256 token hash, constant-time session-secret compare and PID/start-time/executable binding in [`SessionManager.cs`](../../src/Aether.Broker/Sessions/SessionManager.cs); transport identity resolution in [`ProcessIdentity.cs`](../../src/Aether.Broker/Launching/ProcessIdentity.cs); per-RPC capability and classification checks in [`BrokerGrpcService.cs`](../../src/Aether.Broker/Hosting/BrokerGrpcService.cs); windows only for the authenticated app/instance in [`WindowRegistry.cs`](../../src/Aether.Broker/Windows/WindowRegistry.cs) | **Direct:** replay, unknown/expired token, identity mismatch, operator-token mismatch, protocol mismatch and heartbeat expiry in [`SessionHandshakeTests.cs`](../../tests/Aether.Broker.Tests/SessionHandshakeTests.cs); capability/channel/classification denials in [`AuthorizationTests.cs`](../../tests/Aether.Execution.Tests/AuthorizationTests.cs), [`ChannelRouterTests.cs`](../../tests/Aether.Broker.Tests/ChannelRouterTests.cs) and [`HealthAuditSnapshotTests.cs`](../../tests/Aether.Broker.Tests/HealthAuditSnapshotTests.cs). **Partial:** real multi-process launch/register in the E2E suite. **None:** transport peer-credential or pipe-ACL test. | Same-user isolation is weak by design. The registration request carries `processId`; the broker resolves that PID rather than authenticating the socket/pipe peer. The token is inherited through the child environment, readable by the same user on Linux. The operator token is reusable up to 64 uses and, once a same-user process reads the handshake file, it can register an operator session; the operator path validates the token and that the supplied PID resolves, but does not bind registration to a pre-launched shell process. No explicit named-pipe ACL or sandbox is configured in this repository. |
| 2 | Prompt injection from app or workspace text | Adversarial text instructs the model to exfiltrate data, launch an executable, call a URL or take an unplanned action; model output is crafted to look like a valid plan or a misleading summary | Model output is data: strict JSON parsing and unknown-field rejection ([`AetherJson.cs`](../../src/Aether.Contracts/Json/AetherJson.cs)), typed allowlist and per-action field checks, path/URL/shell-like rejection in [`ActionPlanValidator.cs`](../../src/Aether.Execution/Validation/ActionPlanValidator.cs#L291-L302), revalidation in [`PlanExecutor.cs`](../../src/Aether.Execution/Executor/PlanExecutor.cs#L57-L106), and no arbitrary tool/process API. Plans cannot carry a payload: `PublishContext` refers to a trusted catalog id and the broker supplies the payload. `SummarizeContext` accepts only `private` mode. Cloud planner output is revalidated by the same executor. | **Direct:** malicious model response with an extra `executable` field is rejected in [`CloudPlanProviderTests.cs`](../../tests/Aether.AI.Tests/CloudPlanProviderTests.cs); unsafe-plan corpus covers path/URL/shell-like context references in [`UnsafePlanCorpusTests.cs`](../../tests/Aether.Execution.Tests/UnsafePlanCorpusTests.cs); supplied unsafe plans are denied in [`OrchestratorTests.cs`](../../tests/Aether.Workflow.Tests/OrchestratorTests.cs). **Partial:** E2E tampered plan is rejected at the process boundary in [`DenialE2ETests.cs`](../../tests/Aether.E2E.Tests/DenialE2ETests.cs). **None:** a dedicated app-content prompt-injection corpus. | Containment is structural, not semantic. Injection can still influence which allowed actions a model chooses and can produce a misleading but correctly classified summary. There is no content sanitization or instruction/data separation for future app-content prompts, and no evaluation of model susceptibility. Detector evasion can also hide restricted-looking text from the rule-based classifier. |
| 3 | Sensitive-data cloud egress | A restricted command or context is sent to a cloud planner/summarizer, a failed classification silently falls back to a cloud model, a retry bypasses the policy check, or a side-channel/library performs its own request | `UNKNOWN` is ranked highest and fails closed in [`DataClassification.cs`](../../src/Aether.Contracts/Models/DataClassification.cs); source-first classification in [`SourceFirstClassifier.cs`](../../src/Aether.Security/Classification/SourceFirstClassifier.cs); cloud is disabled by default, requires explicit consent and classification permits in [`PolicyEngine.cs`](../../src/Aether.Security/Policy/PolicyEngine.cs); `RESTRICTED`/`UNKNOWN` deny before transport in [`EgressGateway.cs`](../../src/Aether.Security/Egress/EgressGateway.cs); all adapters are injected with the gateway; provider output is untrusted; no automatic retry loop exists; missing local model returns `ai.provider.missing` without cloud fallback (see [`OfflineModeE2ETests.cs`](../../tests/Aether.E2E.Tests/OfflineModeE2ETests.cs)) | **Direct:** restricted/unknown provider calls never invoke the transport in [`EgressGatewayTests.cs`](../../tests/Aether.Security.Tests/EgressGatewayTests.cs); policy matrix in [`PolicyEngineTests.cs`](../../tests/Aether.Security.Tests/PolicyEngineTests.cs); non-public cloud plans never reach the provider in [`CloudPlanProviderTests.cs`](../../tests/Aether.AI.Tests/CloudPlanProviderTests.cs); privacy routing in [`PrivacyRoutingTests.cs`](../../tests/Aether.AI.Tests/PrivacyRoutingTests.cs). **Direct E2E:** restricted cloud denial and unknown classification with `externalAttempts = 0` in [`DenialE2ETests.cs`](../../tests/Aether.E2E.Tests/DenialE2ETests.cs); restricted workflow with zero attempts in [`MvpWorkflowE2ETests.cs`](../../tests/Aether.E2E.Tests/MvpWorkflowE2ETests.cs). | This is a bounded application-level claim. The test doubles record gateway invocations, not OS network traffic; there is no egress-blocked or packet-capture test. Any code path or dependency that calls `HttpClient` directly bypasses the gateway. The loopback local model is not air-gapped. `INTERNAL` sanitized cloud is policy-gated but no sanitizer or token vault is implemented end-to-end, and `SanitizationReviewed` is an unverified caller assertion. Cloud transport uses a configured endpoint with default TLS validation; no certificate pinning. |
| 4 | Queue amplification or IPC denial of service | A compromised or rogue authenticated client floods `PublishContext`/`Subscribe`, creates many subscriptions, sends large messages, or stalls consumers; broker memory, locks or the audit file become the bottleneck | Bounded per-subscriber queue capacity (default 64) with explicit overflow accounting in [`ChannelRouter.cs`](../../src/Aether.Broker/Channels/ChannelRouter.cs); FIFO ordering and a bounded dedupe window; plan action cap (default 5, absolute 12) in [`PlanLimits.cs`](../../src/Aether.Contracts/Models/PlanLimits.cs); per-plan and per-step deadlines in [`PlanExecutor.cs`](../../src/Aether.Execution/Executor/PlanExecutor.cs); session TTL and short pending-registration TTL in [`BrokerOptions.cs`](../../src/Aether.Broker/BrokerOptions.cs); payload caps for the trusted context catalog; no unbounded queue growth per subscriber | **Direct:** overflow, slow subscriber isolation, FIFO, dedupe, unauthorized channel/subscription in [`ChannelRouterTests.cs`](../../tests/Aether.Broker.Tests/ChannelRouterTests.cs); deadline/action-budget negatives in [`ValidatorTests.cs`](../../tests/Aether.Execution.Tests/ValidatorTests.cs); crash recovery in [`CrashRecoveryE2ETests.cs`](../../tests/Aether.E2E.Tests/CrashRecoveryE2ETests.cs). **None:** load, soak or adversarial subscription-count test. | There is no per-client throttle, no cap on subscriptions per session, and no explicit server-side gRPC message-size configuration; the code relies on framework defaults and catalog validation. A valid session can create repeated subscriptions, each with its own 64-entry queue. Audit appends are synchronous file writes under a lock, so a slow or full disk can affect request latency. Resource pressure within a user session remains possible. |
| 5 | Trace/log disclosure | Audit or diagnostic output contains raw commands, context payloads, account IDs, session secrets, API keys or model responses; a local user reads or correlates logs | Metadata-only audit schema in [`AuditEvent.cs`](../../src/Aether.Security/Audit/AuditEvent.cs#L34-L59): sequence, timestamp, trace id, event type, outcome, code, policy version, classification, provider category, redaction count, payload bytes, duration, app id and chain hashes; no payload or credential fields. Broker publish audit records bytes/classification only. Linked verification in [`AuditChain.cs`](../../src/Aether.Security/Audit/AuditChain.cs#L138-L174). Shell/broker audits exclude raw synthetic account values in E2E assertions. | **Direct:** modified historical entry, deleted entry, wrong HMAC key, and no raw values in serialized audit in [`AuditChainTests.cs`](../../tests/Aether.Security.Tests/AuditChainTests.cs); broker file tamper detection in [`HealthAuditSnapshotTests.cs`](../../tests/Aether.Broker.Tests/HealthAuditSnapshotTests.cs); E2E broker audit contains no `ACC-00000001` and verifies in [`MvpWorkflowE2ETests.cs`](../../tests/Aether.E2E.Tests/MvpWorkflowE2ETests.cs). **Partial:** broker/child log separation is not tested as a disclosure path. | Child stdout/stderr are a separate, non-audit channel: when `--child-log-dir` is set, [`AppLauncher.cs`](../../src/Aether.Broker/Launching/AppLauncher.cs) writes raw child lines, and the recorded demo shows `syntheticAccountId` in those files. Child stderr is also echoed to the broker console. Audit/child-log files are not explicitly restricted to owner-only permissions; they rely on umask/default ACLs. Audit is tamper-evident, not immutable: a local administrator can read, delete, truncate or replace the file, and the default chain is unkeyed. Trace ids and app ids are correlation metadata and can reveal usage patterns. Rotation/retention settings are placeholders and are not enforced. |
| 6 | Registry or artifact tampering | An attacker modifies the app manifest, context catalog, executable, model file or dependency so the broker launches or trusts something not intended; a development build is substituted for a released one | Whole-registry fail-closed validation, manifest version, duplicate ids, known capabilities/classifications and launchable-executable requirements in [`AppRegistry.cs`](../../src/Aether.Broker/Registry/AppRegistry.cs); canonical executable resolution, symlink resolution, absolute/bare names only and allowed-root containment in [`AppLauncher.cs`](../../src/Aether.Broker/Launching/AppLauncher.cs#L274-L322) and [`ProcessIdentity.cs`](../../src/Aether.Broker/Launching/ProcessIdentity.cs); trusted executable templates are expanded only from broker tokens, never from plan/model text; pinned SDK and NuGet versions in [`global.json`](../../global.json) and [`Directory.Packages.props`](../../Directory.Packages.props); warnings as errors in [`Directory.Build.props`](../../Directory.Build.props) | **Direct:** unknown app, unallowlisted executable and path traversal are denied in [`LauncherTests.cs`](../../tests/Aether.Broker.Tests/LauncherTests.cs); duplicate ids, unknown capability/classification, bad manifest version and missing executable are rejected in [`RegistryTests.cs`](../../tests/Aether.Broker.Tests/RegistryTests.cs); fixture purity in [`FixturePurityTests.cs`](../../tests/Aether.Contracts.Tests/FixturePurityTests.cs). **None:** signature/digest verification test. | The registry and executables are trusted local files writable by the same user; there is no signature, checksum or attestation check at load time. A tampered registry can add a new app entry, and allowed roots can be broad (the demo allows the repository and the .NET installation directory). Argument templates are trusted configuration and are not constrained to non-path text. Development builds are not an enterprise trust anchor; SBOM, release signing and reproducible distribution are not produced yet, and no Dependabot/equivalent configuration is present in this tree. |
| 7 | Model-output supply chain changes | A provider changes model version, prompt behaviour or response shape; a local model package is swapped; an adapter or SDK upgrade changes parsing; the new output still validates as an allowed plan | Provider-neutral adapter boundary in [`CloudModels.cs`](../../src/Aether.AI/Providers/CloudModels.cs); one configured cloud adapter and one loopback adapter; strict extraction and strict plan parse in [`CloudPlanProvider.cs`](../../src/Aether.AI/Planner/CloudPlanProvider.cs); output validation and revalidation in the executor; no direct tool/process invocation from output; model id/endpoint are configuration values, not business logic; pinned package versions | **Direct:** malformed output, fenced JSON, unknown fields and non-public routing in [`CloudPlanProviderTests.cs`](../../tests/Aether.AI.Tests/CloudPlanProviderTests.cs); local summary parsing failures and missing model in [`LocalSummaryTests.cs`](../../tests/Aether.AI.Tests/LocalSummaryTests.cs). **Partial:** test doubles only, no live provider or local model. **None:** model file hash/licence check. | There is no live-provider contract test, no pinned remote model revision or output-content digest, and no runtime attestation of a local model package. A benign-looking model update can change which allowed actions are chosen or degrade a summary without violating the schema. Local model licence/checksum metadata and evaluation are v0.4 work. The adapter’s declared local/cloud category is trusted by policy; there is no network-level check that a “local” adapter is actually local. |

### 5.1 Cross-cutting qualifications
Same-user is not a security boundary: the broker authenticates a *session*, not the OS
identity of every caller, so a process that can read a session secret or the operator
handshake file can act within those capabilities until expiry. The egress gateway is not
a network sandbox: it is the only provider path in the reviewed adapters, but a
compromised app, dependency or local service can make its own connection, and “zero
external calls” describes observed gateway/transport invocations in tests. There is no
separate plugin host in this baseline; T5 covers only the loopback model endpoint. The
audit chain is a local integrity signal: it detects modification or deletion within the
verified sequence but not an administrator who rewrites or removes the whole file, or who
replaces the verifier.

## 6. Data classification and policy enforcement

### 6.1 Four labels and precedence
The labels are defined in [`DataClassification.cs`](../../src/Aether.Contracts/Models/DataClassification.cs).
`UNKNOWN` has the highest sensitivity rank, so a missing or ambiguous classification
cannot accidentally behave like `PUBLIC`.

| Label | Rank | Default cloud rule | Intended handling |
|---|---:|---|---|
| `PUBLIC` | 0 | `OPT_IN` | Cloud only when cloud mode is enabled and explicit consent was granted; synthetic public fixtures only |
| `INTERNAL` | 1 | `DENY_BY_DEFAULT` | Private by default; a sanitized-cloud path exists only as a policy flag and is not implemented end-to-end |
| `RESTRICTED` | 2 | `DENY` | Private only; never a cloud provider, including under consent |
| `UNKNOWN` | 3 | `DENY` | Treated as restricted; cloud denied, and the broker refuses to publish it |

The default policy document is created in [`PolicyDocument.cs`](../../src/Aether.Security/Policy/PolicyDocument.cs)
and illustrated in [`safe-policy.example.json`](../contracts/safe-policy.example.json).
Policy loading rejects an invalid document (`policy.invalid_document`) rather than
partially trusting it; `failureMode` must be `FAIL_CLOSED`, all four classes must be
present, and only `OPT_IN`, `DENY_BY_DEFAULT` or `DENY` are accepted.

### 6.2 Source-first classification
1. A trusted source classification sets the floor; a missing one resolves to `UNKNOWN`
   with a `trusted-source:missing:unknown` reason.
2. The deterministic detector scans up to 256 KiB with non-backtracking regular
   expressions and a 250 ms per-rule timeout: `synthetic-account-id`, `email-like` and
   `iban-like` ([`SensitivePatterns.cs`](../../src/Aether.Security/Classification/SensitivePatterns.cs)).
   A finding escalates to at least `RESTRICTED`; a detector failure resolves to `UNKNOWN`;
   the detector can never downgrade the source label.
3. The broker compares a publish declaration with the trusted catalog entry: lower is
   denied as `auth.classification.denied`, `UNKNOWN` is denied as
   `policy.classification.unknown`, and the catalog refuses to load an `UNKNOWN` entry
   ([`ResolvedContext.cs`](../../src/Aether.Broker/Catalog/ResolvedContext.cs#L51-L106)).
4. Derived output inherits the input classification unless explicitly reviewed. Content
   past the 256 KiB scan limit is not inspected; the three rules are detection signals,
   not a PII guarantee. No claim of 100% PII detection is made, and there is no NER model
   or precision/recall claim.

### 6.3 Policy enforcement sequence
1. **Collect with provenance.** The plan names a trusted catalog id; the broker supplies
   the typed payload, context type and explicit classification.
2. **Classify.** `SourceFirstClassifier` combines the source label with rule findings;
   missing input and detector failure fail closed to `UNKNOWN`.
3. **Broker clearance.** Capability, channel, context type and classification are checked
   before publish/resolve; a publish may not downgrade the catalog classification.
4. **Provider policy.** `PolicyEngine.Evaluate` requires cloud mode, explicit consent and
   a per-classification rule; local providers are the permitted private path.
5. **Deny before send.** `EgressGateway.SendAsync` consults policy first; a denial never
   invokes the transport and is audited. A permitted cloud call is an external attempt.
6. **Validate output.** Cloud plans are strict-parsed and revalidated by the executor;
   summaries must yield a `summary` field; provider errors become structured codes.
7. **Audit metadata.** Classification, code, provider category, byte count, duration and
   trace id are recorded; no prompt, response or payload is stored.

### 6.4 Denial and error codes
The stable codes are declared in [`AetherErrorCodes.cs`](../../src/Aether.Contracts/Errors/AetherErrorCodes.cs).
The table lists the codes used on the privacy/authorization path:

| Code | Emitted when | Primary enforcement point |
|---|---|---|
| `policy.classification.unknown` | Classification is `UNKNOWN` or missing for a cloud request; or an `UNKNOWN` publish is attempted | `PolicyEngine`; broker `PublishContext` |
| `policy.cloud.disabled`, `policy.cloud.consent_required`, `policy.cloud.denied_by_default`, `policy.cloud.denied` | Cloud mode is disabled, consent is absent, `INTERNAL` lacks the sanitized review path, or the classification is `RESTRICTED`/not permitted | `PolicyEngine` |
| `policy.invalid_document` | Policy load fails validation (failure mode, missing classes, malformed rules) | `PolicyDocument.EnsureValid` / `PolicyLoader` |
| `egress.denied`, `egress.provider.error` | An adapter has no policy code for a denial; or the transport throws on a permitted request | adapters; `EgressGateway` |
| `ai.provider.missing`, `ai.cloud.not_configured`, `ai.provider.unavailable`, `ai.output.invalid` | No key/endpoint/local model; provider unreachable; output empty, malformed or wrong shape | transports and adapters |
| `ai.command.unsupported` | No deterministic plan matched and no configured planner produced one | deterministic planner / orchestrator |
| `auth.classification.denied` | Session/publisher lacks classification clearance; or a publish declares a lower classification than the catalog | broker `PublishContext` / `ResolveContext`; plan authorization |
| `auth.capability.missing`, `auth.channel.denied`, `auth.context_type.denied` | Required capability, channel or context-type grant is absent | broker services; `ChannelRouter` |
| `auth.token.invalid`, `auth.token.replayed`, `auth.identity.mismatch`, `auth.operator_token.invalid`, `auth.protocol.unsupported` | Registration is rejected for the corresponding reason | `SessionManager` |
| `broker.app.unknown`, `broker.executable.denied`, `broker.launch.failed`, `broker.launch.timeout`, `broker.context.not_found`, `broker.queue.overflow`, `broker.arrange.no_windows`, `broker.not_ready` | Launch or broker-side operation cannot be completed | `AppLauncher` / broker services |
| `plan.*`, `execution.*`, `workflow.*` | Malformed/oversized/unsafe plan; execution state outcome; orchestrator outcome | parser/validator, `PlanExecutor`, orchestrator |

Some constants are defined but not emitted on a reachable path in this baseline
(`egress.response.invalid`, `auth.session.expired`, `audit.chain.invalid`,
`broker.payload.too_large`, `broker.duplicate_message`). They must not be interpreted as
active enforcement without a code change and test. An expired session is reported as
`auth.session.invalid` by the interceptor, not `auth.session.expired`.

### 6.5 No silent fallback
Cloud denial — disabled, unconsented, unconfigured, restricted or unknown — returns the
policy/egress code and does not retry another provider or quietly switch categories. A
missing local model returns `ai.provider.missing`, never cloud. If the deterministic
grammar does not match, a configured cloud planner is tried under the same policy gate; a
denial ends the workflow. `SummaryService` prefers cloud for `PUBLIC` only when
`PreferCloudForPublic` is explicitly set, and `SummarizeContext` mode `private` always
uses the local provider. Provider exceptions become `egress.provider.error` or
`ai.provider.unavailable`; they are not reclassified and retried as public.

## 7. Audit and logging model

### 7.1 What is recorded
`AuditEvent` and `AuditEventDraft` are deliberately metadata-only
([`AuditEvent.cs`](../../src/Aether.Security/Audit/AuditEvent.cs)):

| Recorded | Not recorded |
|---|---|
| Sequence, timestamp, trace id, event type, outcome, policy version, denial code | Raw command or prompt text; context payload JSON; model response text |
| Classification, provider category, payload bytes, redaction count, duration | Account identifiers, names, positions or other payload fields |
| Brokered app id, window arrangement outcome | Session/registration/operator tokens, API keys, model credentials or Authorization headers |
| Previous hash and current hash | Full denial messages or stack traces |

The stream is hash-chained: each entry includes the previous hash, and the verifier
recomputes the chain and checks sequence continuity
([`AuditChain.cs`](../../src/Aether.Security/Audit/AuditChain.cs)). An optional HMAC key
may be supplied; without one the chain is unkeyed SHA-256. The JSONL store is local and
appends one event per line.

### 7.2 Integrity and limits
A modified historical entry is detected at its sequence; a deleted entry appears as a
sequence gap; a wrong HMAC key fails verification. “Tamper-evident” does **not** mean
immutable: a local administrator can read, delete, truncate, replace or rewrite the
entire file, and an unkeyed chain can be recomputed by anyone with write access. Tail
truncation and whole-file replacement are undetectable without an external anchor or a
key the attacker cannot obtain. `PolicyDocument.Audit` exposes `RawPayloads`,
`LocalOnly`, `AuditFile` and `RetentionDays` placeholders, but rotation/retention is not
enforced; the file path is supplied by configuration. Appends are synchronous under the
chain lock and can throw on I/O failure, with no separate audit queue or disk-full policy.

**Child logs are outside this model.** With `--child-log-dir`,
[`AppLauncher.cs`](../../src/Aether.Broker/Launching/AppLauncher.cs) writes raw child
stdout/stderr, and child stderr is echoed to the broker console. The sample risk view
includes `syntheticAccountId` in its context output, so that value can land in a child
log. Audit and child-log files are not explicitly chmod-restricted and inherit
umask/default ACLs. Trace ids, app ids, classifications and timing remain linkable
metadata that can reveal which apps ran and when.

## 8. Secrets and repository hygiene

### 8.1 Secret sources
| Secret/configuration | Current source | Notes |
|---|---|---|
| Cloud API key | `AETHER_CLOUD_API_KEY` via [`EnvironmentSecretProvider.cs`](../../src/Aether.Security/Secrets/EnvironmentSecretProvider.cs) | Environment injection is development-friendly; Windows Credential Manager integration is not implemented |
| Cloud endpoint/model | `AETHER_CLOUD_ENDPOINT`, `AETHER_CLOUD_MODEL` | Configuration values, not secrets; a wrong endpoint redirects data |
| Local model endpoint/model | `AETHER_LOCAL_MODEL_ENDPOINT`, `AETHER_LOCAL_MODEL` | Loopback endpoint is a separate dependency; no air-gap guarantee |
| Registration token | Broker-generated 256-bit random value in the child environment | Single-use, 30 s pending TTL, hash stored server-side |
| Broker session secret | Broker-generated 256-bit random value returned by `Register` | Bearer credential in gRPC metadata; server stores only its hash |
| Operator token | Broker-generated random value, or explicit `--operator-token` | Written to the handshake file; reusable up to `OperatorTokenMaxUses` (default 64) |
| Audit HMAC key | Optional constructor parameter | Not loaded from an OS keystore and not persisted by this code |

`HttpCloudModelTransport` sends the key in an `Authorization: Bearer` header and returns
only structured error codes; it does not write the key or request body to logs. Missing
credentials produce `ai.provider.missing`; they are never defaulted to a built-in value.

### 8.2 Repository rules
1. **Never commit secrets or runtime state.** `.aether/`, handshake JSON, audit files,
   child logs, `.tools/`, build outputs and `_source/` are git-ignored; do not attach
   unsanitized logs to issues, pull requests or benchmarks, and rotate a leaked key rather
   than merely removing it from a commit.
2. **No real data, ever.** Code, tests, docs, screenshots, recordings and benchmarks use
   fictitious issuers and synthetic identifiers only; intentionally sensitive-looking
   values must be synthetic and explicitly labelled, and CI must not require private
   credentials.
3. **Treat the operator handshake file as a credential.** It is `0600` on Unix via a
   temporary file and atomic move; Windows uses default user ACLs. Never copy it to
   another machine or into the repository. Do not weaken redaction to debug: use trace
   ids, codes, counts and durations, and inspect payloads only outside the repository.
4. **Review dependency and model provenance before release.** Package and SDK versions
   are pinned, but SBOM generation, release signing, Dependabot/equivalent scanning and
   model checksum/licence verification remain gaps. Report vulnerabilities privately per
   [`SECURITY.md`](../../SECURITY.md).

## 9. Synthetic-content policy

All repository fixtures are fictitious. The three market fixtures are marked `synthetic`
and contain the word “fictitious”; [`FixturePurityTests.cs`](../../tests/Aether.Contracts.Tests/FixturePurityTests.cs)
rejects email-like addresses and 13–19 digit identifiers and requires the marker. The
context catalog declares an explicit classification for every entry. The `ACC-` account
identifiers, issuer names and values are synthetic and exist to exercise the classifier
and redaction accounting, not to model real accounts. Unsafe-plan fixtures are pure
planning artifacts ([corpus README](../../tests/fixtures/unsafe-plans/README.md)).
Documentation, demos, screenshots and benchmarks must contain no real customer records,
credentials, proprietary positions or live market data. “Synthetic” does not mean
“public”: synthetic context is still classified and follows the same policy, and any
intentionally sensitive-looking test value must be labelled synthetic and never copied
from a real record.

## 10. Verified controls versus not yet verified

### 10.1 Evidence snapshot
The full suite was run on the Linux development host used for this document with the
pinned .NET 10 SDK: **316 passed, 0 failed** across 7 test projects
(41 contracts, 52 broker, 38 security, 140 execution, 27 AI, 9 workflow, 9 E2E). This is
test-count evidence, not a performance or coverage claim. The repository records the
same baseline in [`IMPLEMENTATION_STATUS.md`](../IMPLEMENTATION_STATUS.md).

| Control area | Proved on Linux by |
|---|---|
| Session registration/replay/expiry/identity, capability, channel and classification denials | [`SessionHandshakeTests.cs`](../../tests/Aether.Broker.Tests/SessionHandshakeTests.cs), [`AuthorizationTests.cs`](../../tests/Aether.Execution.Tests/AuthorizationTests.cs), [`HealthAuditSnapshotTests.cs`](../../tests/Aether.Broker.Tests/HealthAuditSnapshotTests.cs), [`ChannelRouterTests.cs`](../../tests/Aether.Broker.Tests/ChannelRouterTests.cs) |
| Classification, policy, cloud denial and zero transport invocation | [`ClassificationTests.cs`](../../tests/Aether.Security.Tests/ClassificationTests.cs), [`PolicyEngineTests.cs`](../../tests/Aether.Security.Tests/PolicyEngineTests.cs), [`EgressGatewayTests.cs`](../../tests/Aether.Security.Tests/EgressGatewayTests.cs), [`CloudPlanProviderTests.cs`](../../tests/Aether.AI.Tests/CloudPlanProviderTests.cs), [`PrivacyRoutingTests.cs`](../../tests/Aether.AI.Tests/PrivacyRoutingTests.cs) |
| Strict parsing, allowlist, unsafe content, action budget, authorization, queue bounds and audit integrity | [`ValidatorTests.cs`](../../tests/Aether.Execution.Tests/ValidatorTests.cs), [`UnsafePlanCorpusTests.cs`](../../tests/Aether.Execution.Tests/UnsafePlanCorpusTests.cs), [`ChannelRouterTests.cs`](../../tests/Aether.Broker.Tests/ChannelRouterTests.cs), [`AuditChainTests.cs`](../../tests/Aether.Security.Tests/AuditChainTests.cs) |
| Multi-process workflow, denial, unknown, offline, crash recovery and synthetic-data hygiene | [`MvpWorkflowE2ETests.cs`](../../tests/Aether.E2E.Tests/MvpWorkflowE2ETests.cs), [`DenialE2ETests.cs`](../../tests/Aether.E2E.Tests/DenialE2ETests.cs), [`OfflineModeE2ETests.cs`](../../tests/Aether.E2E.Tests/OfflineModeE2ETests.cs), [`CrashRecoveryE2ETests.cs`](../../tests/Aether.E2E.Tests/CrashRecoveryE2ETests.cs), [`FixturePurityTests.cs`](../../tests/Aether.Contracts.Tests/FixturePurityTests.cs) |

The E2E suite runs a real broker process, a real headless shell process and three
independent headless sample processes over a Unix domain socket. It is the strongest
runtime evidence in this environment.

### 10.2 Windows-only paths: compiled, not executed here
These paths are present and compile-verified, but not runtime-tested by the evidence run
used for this document. **Named pipes:** `BrokerHost` selects `named-pipe` on Windows and
`Aether.Broker.Client` creates a `NamedPipeClientStream`; Linux/CI uses a Unix domain
socket. No explicit `PipeSecurity`/current-user ACL or transport peer-credential check is
configured in the reviewed code. **Win32 arrangement:** `Win32WindowArranger` calls
`SetWindowPos`; the headless E2E uses `VirtualWindowArranger`, which records a layout
without touching a desktop. Focus, DPI, monitor removal and real top-level behaviour are
untested. **WPF:** the shell and sample apps compile (including cross-compilation on
Linux) but cannot run here; the WPF shell uses the same broker client as the executed
headless shell, but rendering, keyboard and UI-thread behaviour are unverified. **Windows
identity/ACLs:** `ProcessIdentity` uses `Process.MainModule`, and the handshake file relies
on default Windows ACLs rather than Unix `0600`; neither was inspected at runtime.

The repository's [CI workflow](../../.github/workflows/ci.yml) defines Linux and Windows
jobs, including a named-pipe demo. That workflow is the evidence source for the Windows
runtime paths; this document does not claim its result, and the Windows paths must not be
presented as “verified” until those jobs run and their evidence is attached.

### 10.3 Not implemented or not evaluated
- **Cross-process docking is excluded from the MVP and not evaluated.** No
  `SetParent`/`HwndHost` implementation or feasibility evidence is part of this baseline;
  only ordinary top-level placement and the virtual headless arranger exist.
- No live cloud provider or installed local model; deterministic doubles cannot validate
  provider health, retention, latency, model licence or output quality. There is no durable
  consent UI or consent-version record; consent is a per-run option in the headless host.
- No OS-level egress-blocked/packet-capture test, load or soak test, per-client rate limit,
  subscription cap, server-side gRPC message-size setting or disk-full audit test.
- No NER detector, token vault, reversible pseudonymization, end-to-end sanitized
  `INTERNAL` cloud path, audit rotation/retention, verification CLI or external anchor.
- No SBOM, release signing, installer, reproducible Windows distribution, dependency
  scanner or model checksum verification.
- No Windows runtime test for named pipes, Win32 arrangement, WPF shell or ACLs.
- No dedicated prompt-injection corpus or semantic-quality evaluation; structural plan
  rejection is tested, model susceptibility is not.
- No FDC3 intent/user-channel implementation or independent interop test; no real
  multi-monitor/DPI test; no compliance or certification evidence.

## 11. Release-gate evidence

This section maps the requested AW-005/NFR evidence to artifacts. The claims are bounded
to the test setup.

### 11.1 Unsafe-plan corpus: 100 entries denied before side effects (NFR-03)
[`unsafe-plans.json`](../../tests/fixtures/unsafe-plans/unsafe-plans.json) contains
**exactly 100** unique entries with documented expected denial codes: 10 each for unknown
actions, unknown applications, path-traversal and URL-like context references, over-budget
plans, unsupported schema majors, cloud-summary requests and unknown trusted contexts; and
5 each for unsupported layouts, invalid status texts, duplicate launches and forbidden
JSON fields.
[`UnsafePlanCorpusTests.cs`](../../tests/Aether.Execution.Tests/UnsafePlanCorpusTests.cs)
asserts the count/uniqueness and executes every entry through the real parser, validator
and executor: every plan is denied with its expected code, every recording action handler
stays empty, and the audit chain verifies. [`DenialE2ETests.cs`](../../tests/Aether.E2E.Tests/DenialE2ETests.cs)
exercises a representative 12-entry slice across the real broker boundary, asserting exit
code 3, the expected denial code, no `succeeded` step, no broker `broker.launch` audit
event and no child-process log contents. [`OrchestratorTests.cs`](../../tests/Aether.Workflow.Tests/OrchestratorTests.cs)
adds a supplied unsafe plan with no broker launch and a valid audit chain. This proves
the sampled slice, not arbitrary future plans.

### 11.2 Zero external calls for RESTRICTED/UNKNOWN (NFR-04)
- [`EgressGatewayTests.cs`](../../tests/Aether.Security.Tests/EgressGatewayTests.cs)
  asserts restricted/unknown requests never invoke the sending delegate and
  `ExternalAttempts` stays 0; [`PolicyEngineTests.cs`](../../tests/Aether.Security.Tests/PolicyEngineTests.cs)
  covers the whole cloud-enabled/consent/sanitized matrix for `UNKNOWN`.
- [`CloudPlanProviderTests.cs`](../../tests/Aether.AI.Tests/CloudPlanProviderTests.cs) and
  [`PrivacyRoutingTests.cs`](../../tests/Aether.AI.Tests/PrivacyRoutingTests.cs) assert
  `transport.Calls == 0` for non-public classifications, missing consent, disabled cloud
  and private summaries.
- [`DenialE2ETests.cs`](../../tests/Aether.E2E.Tests/DenialE2ETests.cs),
  [`MvpWorkflowE2ETests.cs`](../../tests/Aether.E2E.Tests/MvpWorkflowE2ETests.cs) and
  [`OfflineModeE2ETests.cs`](../../tests/Aether.E2E.Tests/OfflineModeE2ETests.cs) record
  `attempts = 0`/`externalAttempts = 0` for a restricted cloud command with consent, an
  `UNKNOWN` command, the restricted private workflow and a missing local model.

Boundary: these are **gateway-level observations with test doubles**, not an OS network
capture. The suite separately proves detector failure resolves to `UNKNOWN` and `UNKNOWN`
denies cloud, but no single end-to-end fixture forces a detector failure and then attempts
a cloud call; that composition remains a test gap.

### 11.3 Audit tamper evidence
[`AuditChainTests.cs`](../../tests/Aether.Security.Tests/AuditChainTests.cs) covers
untampered chains, disk persistence, a modified historical entry, a deleted entry as a
sequence gap, wrong-HMAC rejection and absence of synthetic account/name values from
serialized audit. [`HealthAuditSnapshotTests.cs`](../../tests/Aether.Broker.Tests/HealthAuditSnapshotTests.cs)
modifies the broker audit file and asserts verification fails.
[`MvpWorkflowE2ETests.cs`](../../tests/Aether.E2E.Tests/MvpWorkflowE2ETests.cs) verifies
both shell and broker chains and checks that the broker audit lacks the synthetic account
id. Not covered: tail truncation, whole-file replacement with a recomputed unkeyed chain,
key theft, rotation, disk-full behaviour and an external anchor.

### 11.4 Reproduction
```bash
./scripts/verify.sh                         # Release build, all tests and the demo
dotnet test AetherWorkspace.slnx -c Release # Test suite only
./scripts/demo.sh                           # Unix socket + headless sample workflow
```

The demo writes git-ignored artifacts under `.aether/demo/`, including the `0600`
handshake file on Unix, audit JSONL files and raw child logs. Do not publish those
directories or attach them to issues without checking for synthetic-only content.

## 12. Residual risk summary and review triggers

The controls reduce the cost of accidental disclosure and unauthorized execution; they
do not create an OS-grade trust boundary. The principal residuals are:

1. **Same-user impersonation** remains possible through readable registration/operator
   credentials, client-supplied PID binding, bearer session secrets and the absence of a
   transport peer-credential/ACL check.
2. **Egress and diagnostics leak paths remain.** The gateway cannot constrain a
   compromised app, dependency or local model, and raw child logs sit outside the
   metadata-only audit.
3. **Audit is tamper-evident, not immutable**; unkeyed chains are forgeable by a writer,
   and administrators can truncate or replace the file.
4. **Artifacts and detection are not authenticated.** Registry, binaries and model files
   lack signature/digest verification, while three bounded rules are not PII detection;
   values beyond the scan window or unmodelled identifiers can pass as `PUBLIC`.
5. **Model behaviour can drift, and Windows paths are unproven.** Schema validation
   bounds effects, not meaning; named-pipe ACLs, peer identity, Win32 arrangement and WPF
   runtime behaviour require Windows evidence.

Review this model when the transport/peer identity model, policy or detector, egress
surface, audit schema/key management, launcher/registry trust model, model/plugin
integration, platform matrix or Windows CI evidence changes. Re-run
`./scripts/verify.sh`; do not promote a “verified” label without a test and a documented
residual limitation.

## 13. References

[Technical Specification v3.0](../planning/technical-specification-v3.0.md) (§3.2 trust
boundaries, §7 privacy/egress, §8 security/audit); [Implementation & Release Plan v3.0](../planning/implementation-release-plan-v3.0.md)
(AW-005, NFR-03/NFR-04); [honest limitations](../limitations.md);
[implementation status](../IMPLEMENTATION_STATUS.md); [`action-plan.schema.json`](../contracts/action-plan.schema.json),
[`app-manifest.schema.json`](../contracts/app-manifest.schema.json),
[`context-envelope.schema.json`](../contracts/context-envelope.schema.json) and
[`safe-policy.example.json`](../contracts/safe-policy.example.json); [SECURITY.md](../../SECURITY.md);
[`tests/`](../../tests); [CI workflow](../../.github/workflows/ci.yml).

This threat model is a living engineering document. It supersedes no release gate by
itself and must be read with the limitations page; absence of a listed threat is not
evidence that the threat is impossible.

## Credential sources (v1.3)

The default secret source remains explicit environment/`.env` injection. An optional Windows
Credential Manager adapter reads Generic credentials named `AetherWorkspace/<VARIABLE_NAME>`; it
never writes credentials, never returns them in diagnostics and rejects the source on non-Windows
hosts. Provider configuration stores only variable names, validates endpoints (HTTPS for cloud,
loopback-only for local inference; no silent brand replacement) and never logs secret values.
