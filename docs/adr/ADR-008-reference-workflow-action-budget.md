# ADR-008: Reference workflow action budget of six actions (documented deviation from the default five)

- **Status:** Accepted (documented deviation)
- **Date:** 2026-11-14
- **Deciders:** AetherWorkspace maintainers
- **Related:** [Architecture overview](../architecture/overview.md), [ADR-003](ADR-003-separate-planning-and-execution.md)

## Context

The specification (§5.3) sets "Max actions per plan: configurable; default 5 for MVP".
The reference workflow needs three `LaunchApp` steps (Watchlist, Risk View, Chart), one
`PublishContext`, one `ArrangeWindows` and one `SummarizeContext`: **six** actions. A
five-action budget rejects the reference plan, so either the workflow or the default
would have to change. The bounded-execution requirement still forbids unbounded plans.

## Decision

- Keep the **library default at 5** (`PlanLimits.DefaultMaxActions`, the default in
  `ActionPlanValidationOptions`, `ExecutionOptions`, `BrokerOptions` and
  `BrokerEndpointInfo`). No silent global change is made.
- Set the **reference configuration to 6**: `ShellOptions.MaxActions = 6`,
  `BrokerAppOptions.MaxActions = 6`, the WPF shell's explicit validator/executor budget,
  `scripts/demo.sh --max-actions 6`, the E2E harness `--max-actions 6` and
  `WorkflowOrchestratorOptions.MaxActionsPerPlan` (the reference-workflow composition
  surface, whose initializer is 6).
- Keep `PlanLimits.AbsoluteMaxActions = 12` as the hard cap. Configuration is validated
  to 1–12; the JSON Schema sets `steps.maxItems = 12`; the validator/executor reject a
  larger plan even if a host is misconfigured.
- Record the deviation here and in the implementation status/limitations docs; do not
  let a plan declare or raise its own action budget.

## Consequences

### Positive

- The three-process reference workflow runs as one bounded plan without weakening the
  spec's library default for other adopters.
- The budget remains explicit, per-deployment configurable and hard-capped at 12.
- Automated tests cover both the default (a six-step plan is rejected when the budget is
  5) and the configured reference path (six steps accepted).

### Negative

- Two numbers must be explained: the low-level library default is 5, while the reference
  host configuration and the workflow-orchestrator options initializer use 6. A host
  that forgets to pass an explicit budget will reject the reference plan.
- Anyone copying the demo configuration inherits six actions; the deviation must stay
  visible in scripts, help text, docs and tests or it becomes accidental drift.
- The reference plan cannot grow without a configuration change (up to the hard cap);
  future workflows may need a deliberate budget review.

## Alternatives considered

- **Raise the library default to 6** — would silently contradict §5.3 for every embedder;
  rejected. The deviation is confined to the reference configuration instead.
- **Redefine the workflow to five actions** — would drop one of the three required
  independent apps or the summary; rejected.
- **Combine the three launches into one compound action** — changes action semantics,
  audit granularity and compensation behaviour; rejected.
- **Let the plan carry its own budget or let an untrusted plan raise it** — plan input is
  untrusted and must not widen its own limits; rejected.
- **Remove the hard cap or make it much larger** — loses the bounded execution guarantee;
  rejected.

## Evidence

- `src/Aether.Contracts/Models/PlanLimits.cs` (`DefaultMaxActions = 5`,
  `AbsoluteMaxActions = 12`); `docs/contracts/action-plan.schema.json` (`maxItems: 12`).
- `src/Aether.Execution/Validation/ActionPlanValidator.cs`,
  `src/Aether.Execution/Executor/ExecutionOptions.cs`,
  `src/Aether.Broker/BrokerOptions.cs` (default 5 and 1–12 validation).
- `src/Aether.BrokerApp/BrokerAppOptions.cs` (default/help text "demo default 6; library
  default 5"), `src/Aether.Shell.Headless/ShellOptions.cs` (default 6) and
  `src/Aether.Shell/MainWindow.xaml.cs` (explicit 6).
- `scripts/demo.sh` (`--max-actions 6`), `tests/Aether.Testing/EndToEndHarness.cs`
  (`--max-actions 6`), `tests/Aether.Testing/TestData.cs` (six-step `ReferencePlan`) and
  `tests/Aether.E2E.Tests/MvpWorkflowE2ETests.cs` (six successful steps).
- `tests/Aether.Execution.Tests/ValidatorTests.cs` and
  `tests/Aether.Execution.Tests/UnsafePlanCorpusTests.cs` exercise the configured budget
  and all 100 unsafe plans; `tests/Aether.Execution.Tests/PlanExecutorTests.cs` covers
  rejection above a configured budget.
