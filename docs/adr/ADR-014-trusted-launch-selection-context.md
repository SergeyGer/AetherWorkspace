# ADR-014: Trusted launch-selection context for application selection

- **Status:** Accepted
- **Scope:** v1.5.1 Workspace UX & AI Command Center
- **Related:** ADR-003 (untrusted planning vs deterministic execution), ADR-005 (top-level arrangement), issue AW-071

## Context

v1.5.1 introduces explicit application selection in the Shell: the operator can choose an approved
subset of registered applications and choose **Preferred** or **Strict** launch scope. The selection
must constrain an AI plan without letting the untrusted planner forge or widen it.

`ActionPlan` is untrusted JSON. Adding selection fields to the plan would let a malicious/buggy
planner self-authorize. The existing broker registry and capability checks remain authoritative for
app identity and launch permission; selection is an additional scope gate owned by the operator.

## Decision

The Shell creates an immutable `LaunchSelectionContext` **adjacent to** the ActionPlan:

- `Mode`: `Preferred` (advisory) or `Strict` (mandatory whole-plan scope);
- `AllowedLaunchAppIds`: immutable set of app IDs from the UI's registered-app selection;
- `SelectionRevision`: identifier for the selection snapshot used for this run.

The context is passed as a trusted out-of-band `WorkflowRequest.LaunchSelection` value, never parsed
from plan JSON and never sent to a model. Before the first executor step, the orchestrator
pre-authorizes the complete plan:

1. all steps are schema-validated against the current catalog/registry;
2. in `Strict` mode, **every** `LaunchApp.appId` must be present in `AllowedLaunchAppIds`;
3. any violation returns `plan.selection.app_not_selected` (denial category `UnauthorizedApp`)
   before handlers, broker RPCs or summary generation; the denied app ID is recorded as
   metadata-only audit evidence;
4. in `Preferred` mode the selection is advisory and existing registry/capability policy applies.

The model cannot forge the selection: it is not in `ActionPlan`, not in planner prompts, and cannot
be supplied via the UI checkbox state without the Shell's trusted out-of-band object. The UI
selection is loaded from the current broker snapshot and revalidated against the registry even when
selected.

## Consequences

- Strict mode can deny an otherwise registered/authorized plan; this is an expected policy denial,
  not an infrastructure failure.
- Selection changes during a run cannot expand the already-captured allowed set; the Shell captures a
  revision per run and the context is immutable.
- `Launch Selected` is a separate broker-mediated coordinator action, not an AI workflow and not a
  bypass of capability authorization; it uses the same allowlisted launcher.
- No arbitrary executable paths, shell commands or cloud-INTERNAL routing are introduced.
- Tests cover Strict rejection before any launch, Preferred pass-through, phase progress and the
  trusted/chart-variant path.
