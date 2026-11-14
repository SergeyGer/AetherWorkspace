# ADR-008: Reference workflow action budget of six actions

- **Status:** Accepted (documented deviation)
- **Date:** 2026-11-14

## Context

The specification (§5.3) says: "Max actions per plan: configurable; default 5 for MVP".
The reference workflow requires launching **three** independent sample processes
(Watchlist, Risk View, Chart), publishing the typed instrument context, arranging windows
and producing the private summary: six typed actions. A five-action budget would make the
required three-app workflow impossible in a single plan.

## Decision

- Keep the library default at **5** (`PlanLimits.DefaultMaxActions`,
  `ExecutionOptions.MaxActionsPerPlan`) as specified.
- The reference demo configuration explicitly sets `--max-actions 6` (broker, shell and
  workflow options), and the hard cap remains 12 (`PlanLimits.AbsoluteMaxActions`).
- Document the setting in the demo scripts/quickstart and in
  `docs/IMPLEMENTATION_STATUS.md`.

## Consequences

Positive: the three-process reference workflow is expressible without changing the
library default or unbounded action counts; the budget remains configurable and bounded.
Negative: a documented deviation from the stated default configuration; anyone copying
the demo configuration inherits six actions. The validation test suite covers both the
default (rejects six) and configured (accepts six) behaviour, so the distinction is
explicit.

## Alternatives

Redefine the workflow to five actions (rejected: would drop one of the required three
independent apps or the summary), raise the global default to six (rejected: contradicts
the specification and silently changes bounded execution for all adopters), launch an app
from another app (rejected: only the broker may launch allowlisted apps).

## Evidence

`src/Aether.Contracts/Models/PlanLimits.cs`, `ExecutionOptions`, broker/shell
`--max-actions` handling, demo scripts, validator/executor tests.
