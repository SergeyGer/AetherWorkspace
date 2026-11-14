# Evidence record — v0.2 reliability hardening (2026-11-14)

Second development step after the v0.1 vertical slice. Scope: close the remaining
reliability/observability gaps of AW-016, AW-017, AW-019 and AW-020, and publish the
repository. Windows-only runtime evidence remains delegated to the Windows CI job.

## Implemented

| Item | Implementation |
|---|---|
| Offline mode + provider health (AW-019) | `--offline` headless shell flag: cloud adapter is not constructed, cloud flags forced off, `--offline --planner cloud` rejected as a usage error. Structured result gains `providers` (mode, deterministic planner, local deterministic summary, cloud/local-model adapter configured, cloud flags) computed **without network I/O**. |
| Audit rotation/retention (AW-020) | `RotatingJsonlAuditStore`: size-bounded JSONL rotation (`--audit-max-bytes`), bounded retention (`--audit-files`), verification across retained/rotated files, and continuation of one hash chain across process restarts (`--audit-file` re-opened in continue mode; tampered persisted chains rejected fail-closed with `audit.chain.invalid`). |
| Fault injection (AW-017) | `SIGSTOP`-hung sample app → `UNRESPONSIVE` health state → bounded restart → `HEALTHY`; `SIGKILL`ed app → `EXITED` → restart; broker process killed → shell exits fast (code 4) with `shell.broker_error`, no hang; restart after crash keeps the reference workflow green. |
| Retry/idempotency evidence (AW-016) | RPC-boundary test: the same `clientMessageId` published twice is delivered once, the second call returns the original `messageId` with `deliveredCount = 0`; without a dedupe key each publish is a distinct message. Retry semantics documented in `docs/contracts/README.md`. |

## Automated tests added

| Suite | Added |
|---|---|
| `tests/Aether.Security.Tests` | `AuditRotationTests` (rotation, retention, tampered rotated file, deleted rotated file gap, invalid options) and cross-process chain continuation/tamper rejection in `AuditChainTests` |
| `tests/Aether.Broker.Tests` | `FaultInjectionTests` (hung app + restart, killed app + restart, duplicate `clientMessageId` delivered once, distinct publishes without key) |
| `tests/Aether.E2E.Tests` | offline workflow + provider health, offline/cloud usage error, configured-cloud health without network call, shell audit rotation across two runs, broker-disconnect fail-fast, crash-restart workflow |

Totals after this step: **333 tests** across 7 projects — contracts 41, security 45,
execution 140 (incl. 100 unsafe-plan cases), AI 27, broker 56, workflow 9, E2E 15 — all
passing in Release on this Linux workspace.

## Reproduce

```bash
dotnet build AetherWorkspace.slnx -c Release
dotnet test  AetherWorkspace.slnx -c Release
./scripts/demo.sh
```

## CI verification (Linux and Windows)

CI run [43641011037](https://github.com/SergeyGer/AetherWorkspace/actions/runs/43641011037)
on commit `674e2c0` is green on both jobs:

- **Linux:** Release build (0 warnings), 333 tests, headless reference demo, IPC benchmark
  smoke.
- **Windows:** Release build including WPF projects, full test suite, and the headless
  reference workflow over **Windows named pipes** (the demo step).

Two real problems were found and fixed by the CI loop: the Windows named-pipe client set
stream properties before connecting, and several tests assumed Unix path canonicalization.
Earlier failing runs are retained in Actions history as evidence of the failures.

## Not evidenced here

- Windows runtime behaviour: named pipes, WPF shell/windows, Win32 placement (Windows CI
  job is the evidence source).
- OS-level network-egress-blocked capture on a reference Windows machine; the bounded test
  claim remains "zero observed external provider attempts through the egress gateway".
- SIGSTOP/SIGCONT hang injection is Unix-only; Windows hang behaviour is not runtime-tested.
- Signed/externally anchored audit storage and an operator runbook (v0.5/v1.0 scope).
