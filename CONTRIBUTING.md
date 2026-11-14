# Contributing

Thanks for your interest in AetherWorkspace. This is a reference implementation; the
most valuable contributions are tests, security analysis, documentation clarity and
evidence-backed fixes.

## Ground rules

1. **Synthetic data only.** Never add real customer, position, transaction, account or
   market data, or filenames/screenshots containing them. All fixtures must be marked
   synthetic and use fictitious names/identifiers.
2. **No secrets.** No real credentials, provider keys, tokens or customer endpoints in
   code, fixtures, tests, benchmarks, logs or docs. Use environment variables or a local
   secret provider; never print secrets.
3. **Keep the trust boundaries.** AI output is an untrusted proposal; only typed,
   allowlisted actions validated by `Aether.Execution` may execute. No arbitrary
   process execution, shell command execution, URL fetching or filesystem mutation from
   model output. Cloud egress must go through `EgressGateway` and stay denied by
   default. If a change weakens a control, it needs explicit discussion and a threat
   model update.
4. **Warnings are errors.** `dotnet build AetherWorkspace.slnx -c Release` must pass
   with the repository's analyzers/errors enabled.
5. **Tests with claims.** Every behavioural change needs normal, failure and
   authorization-denied coverage as applicable. Restricted/UNKNOWN egress regressions
   must keep zero external calls.

## Development workflow

```bash
dotnet build AetherWorkspace.slnx -c Release
dotnet test  AetherWorkspace.slnx -c Release
./scripts/demo.sh
```

- The implementation is ordered by the plan in `docs/planning/implementation-release-plan-v3.0.md`.
  Reference the relevant stable `AW-` id in issue/PR descriptions.
- Architecture decisions that constrain future work should be captured in `docs/adr/`.
- Update `docs/IMPLEMENTATION_STATUS.md`, benchmark notes and limitations whenever a
  claim or a known gap changes.
- Windows-only paths (named pipes, WPF shell, Win32 arrangement) must be validated on
  Windows (the Windows CI job) before claiming runtime verification.

## Pull requests

Use the PR template. Keep changes focused; do not mix unrelated refactors with
behavioural changes. Security-sensitive changes should state the threat considered, the
control preserved and the residual risk. No compliance, performance or interoperability
claim may be added without reproducible evidence in the repository.

## Reporting security issues

Do not open a public issue for a suspected vulnerability. Follow [`SECURITY.md`](SECURITY.md).
