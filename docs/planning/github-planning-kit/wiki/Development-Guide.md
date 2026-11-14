# Development Guide

## Step-by-step implementation

1. Complete `AW-001`–`AW-006`: pin .NET 10, define contracts, prototype Named Pipes, test docking feasibility, publish threat model and baseline measurements.
2. Complete `AW-007`–`AW-012`: build the allowlisted Broker, authenticated session, three sample processes, basic Shell, context channel and deterministic executor.
3. Complete `AW-013`–`AW-015`: add the optional AI planning adapter and private local summary behind policy; automate the entire MVP demo.
4. Complete `AW-016`–`AW-020`: harden bounded queues, crash handling, benchmarks, offline behavior and redacted audit.
5. Complete `AW-021`–`AW-024`: add a declared FDC3 subset and conformance evidence.
6. Complete `AW-025`–`AW-029`: add local entity detection, vault and provider evaluation; verify external egress under failure and attack fixtures.
7. Complete `AW-030`–`AW-033`: improved layouts and trace inspector; optionally owned-window docking only after passing the spike.
8. Complete `AW-034`–`AW-035`: add review-only generated contract drafts and compiler/schema validation.
9. Complete `AW-036`–`AW-038`: package, document, test on clean Windows and publish evidence with the release tag.

## Issue workflow
Backlog → Ready → In Progress → In Review → Verified → Closed. A task is verified only with tests, documented constraints and updated usage instructions.

## Local environment
Windows 11 x64, .NET 10 SDK, Visual Studio or compatible C# toolchain. Models are optional for deterministic test mode; exact local model artifact/license/hardware must be specified in a later setup guide.

## Pull-request checklist
- [ ] Feature linked to an `AW-` issue and accepted milestone.
- [ ] Unit/contract/E2E tests added where applicable.
- [ ] Unauthorized behavior and failure cases tested.
- [ ] No real personal/confidential data or secrets added.
- [ ] Docs/ADR/benchmark notes updated.
