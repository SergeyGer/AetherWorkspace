# ADR-003: Separate untrusted AI planning from deterministic execution

- **Status:** Accepted (mandatory)
- **Date:** 2026-11-14

## Context

Language models and generated text are non-deterministic, potentially manipulated, and
must never directly execute processes, RPCs or files. Security and product requirements
demand deterministic verification, authorization and bounded side effects.

## Decision

- AI provides an untrusted **`ActionPlan` proposal** only. It cannot reference executable
  paths, shell commands, arbitrary URLs, new app identities or raw data payloads.
- `Aether.Execution` owns strict JSON parsing, schema/allowlist validation, action-budget
  and deadline enforcement, capability authorization and the explicit state machine.
- Action steps bind to typed contracts and handlers wired only by the composition root;
  the plan can request `LaunchApp(appId)` or `PublishContext(contextRef)`, not an
  arbitrary process or payload.
- Denials are structured and never fall through to a higher-privilege alternative.

## Consequences

Positive: a model compromise or prompt injection cannot directly cause arbitrary code
execution; validation is testable independently of any model; the deterministic path
still works with no model installed.
Negative: the plan language is intentionally narrow (no general tool use), and every new
capability needs explicit typed support and review.

## Alternatives

Direct model tool-calling into process/RPC APIs (rejected: violates the specification and
the threat model), sandboxed arbitrary tool execution (rejected for MVP: too large a
surface), no AI at all (rejected: contradicts the product hypothesis; deterministic
grammar remains the primary path while models are optional).

## Evidence

`src/Aether.Execution/Validation/ActionPlanValidator.cs`,
`src/Aether.Execution/Executor/PlanExecutor.cs`, 100-entry unsafe corpus in
`tests/fixtures/unsafe-plans/`, `tests/Aether.Execution.Tests`.
