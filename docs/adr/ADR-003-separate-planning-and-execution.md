# ADR-003: Separate untrusted AI planning from deterministic validation, authorization and execution

- **Status:** Accepted (mandatory control)
- **Date:** 2026-11-14
- **Deciders:** AetherWorkspace maintainers

## Context

Model output is non-deterministic, can be manipulated by prompt injection and must never
directly execute processes, RPCs, shell commands or file operations. The specification
requires structured plans, schema/allowlist checks, capability authorization and bounded
side effects (`_source/Technical_Specification.md` §5). The project also needs a fully
deterministic path that works with no model installed.

## Decision

- `Aether.AI` produces an untrusted `ActionPlan` proposal only. It has no
  `Broker.Client` or `Execution` project reference and cannot perform IPC or side effects.
- Plans are flat, typed steps (`LaunchApp`, `PublishContext`, `ArrangeWindows`,
  `SummarizeContext`, `ShowStatus`). There are no executable paths, command lines,
  arbitrary URLs, raw payloads or new app identities in the schema.
- `ActionPlanValidator` owns strict parsing and validation: supported schema major,
  action allowlist, per-action field exclusivity, trusted app/context references,
  duplicate-launch rejection, unsafe path/URL/shell-like content rejection, action
  budget, mode/layout allowlists and bounded text fields.
- `CapabilityPlanAuthorizationPolicy` checks `plan.execute` and the capability, channel,
  context type and classification required by each step before a handler runs.
- `PlanExecutor` validates again, then executes each step with explicit states,
  step/plan deadlines, cancellation and structured denials. Actions reach the outside
  world only through typed handlers wired by `Aether.Workflow`; the model never supplies
  a process path or broker RPC payload.
- Denial or validation failure stops the plan and never falls through to a
  higher-privilege alternative. The deterministic grammar is the primary planner; cloud
  and local model adapters are optional and produce the same validated plan type.
- Model responses are strict-parsed (unknown fields rejected) before they become plans.

## Consequences

### Positive

- Prompt injection or a compromised model cannot directly execute code, launch an
  arbitrary path or publish arbitrary payload content; the worst case is a bounded plan
  built from allowed typed actions.
- Validation and authorization are testable without a model and are exercised by the
  100-entry unsafe-plan corpus (all denied before any handler invocation).
- The deterministic grammar path needs no model, no network and no cloud consent, so
  the reference workflow still works offline.
- Provider or model replacement does not change executor or broker code.

### Negative

- The plan language is deliberately narrow: no general tool use, no model-generated
  code, no dynamic app identities or payloads. New capabilities require typed steps,
  handler review and tests.
- A structurally valid but semantically undesirable plan can still be proposed within
  the allowed action set; validation bounds authority, not intent quality.
- Plan accuracy, model quality and prompt-injection semantics remain unmeasured here;
  the v0.4 evaluation suite is not implemented.
- No human confirmation step exists in the MVP. Actions are bounded local operations;
  published context is not retractable, but no action grants new authority.

## Alternatives considered

- **Direct model tool-calling into process/RPC APIs** — violates the core security
  requirement; rejected.
- **Sandboxed arbitrary tool/code execution** — much larger attack surface and not
  needed for the reference workflow; rejected for the MVP.
- **Schema validation only, without capability authorization** — a valid plan could
  still exceed the session's grants; rejected.
- **No AI at all, deterministic only** — would remove the product hypothesis and the
  optional provider path; the deterministic grammar is primary but not the only option.
- **Let the model choose its own action budget or app paths** — untrusted input must
  not raise limits or name executables; rejected.

## Evidence

- `src/Aether.Contracts/Models/ActionPlan.cs` (typed, flat plan) and
  `src/Aether.Execution/Validation/ActionPlanValidator.cs`.
- `src/Aether.Execution/Authorization/PlanAuthorizationPolicy.cs` and
  `src/Aether.Execution/Executor/PlanExecutor.cs` (states, deadlines, compensation).
- `src/Aether.Workflow/AetherWorkflowOrchestrator.cs` and typed handlers under
  `src/Aether.Workflow/Handlers/`; `src/Aether.AI/Aether.AI.csproj` shows no IPC or
  execution dependency.
- `src/Aether.AI/Planner/DeterministicCommandPlanner.cs` and
  `src/Aether.AI/Planner/CloudPlanProvider.cs` (strict parse, no direct execution).
- `tests/Aether.Execution.Tests/ValidatorTests.cs`,
  `tests/Aether.Execution.Tests/AuthorizationTests.cs`,
  `tests/Aether.Execution.Tests/PlanExecutorTests.cs`,
  `tests/Aether.Execution.Tests/UnsafePlanCorpusTests.cs` and
  `tests/Aether.E2E.Tests/DenialE2ETests.cs` provide the automated evidence.
