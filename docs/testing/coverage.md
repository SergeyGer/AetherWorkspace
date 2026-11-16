# Test coverage (portable suites)

AetherWorkspace publishes a measured **unique source-line coverage** badge for the portable test
suites. The tooling is part of the test infrastructure (`coverlet.collector` in
`tests/Directory.Build.props`); no third-party coverage service receives data.

## Current measurement (2026-11-16)

| Metric | Value |
|---|---:|
| Unique source-line coverage | **66.5 %** |
| Covered / valid lines | 7,440 / 11,185 |
| Source files in portable reports | 111 |
| Portable coverage reports merged | 8 |

Badge: [`docs/assets/coverage-badge.svg`](../assets/coverage-badge.svg). The value is published
as measured — there is no rounding up or target claim; CI does not enforce a coverage threshold yet.

## What is measured

- Source files under `src/` only; test code (`tests/`), tooling (`tools/`), generated code and the
  Windows-only UI/desktop lanes are excluded.
- Reports from all portable test projects are merged per source line: a line counts as covered if
  **any** suite hit it (unique-line coverage, not additive duplicates).
- Branch coverage is not part of the badge.

## Reproduce

```bash
dotnet test AetherWorkspace.slnx -c Release \
  --collect:"XPlat Code Coverage" \
  --results-directory /tmp/coverage

python3 tools/coverage/summarize-coverage.py \
  --results /tmp/coverage \
  --badge docs/assets/coverage-badge.svg \
  --json /tmp/coverage/summary.json
```

The script prints the summary (`linesCovered`, `linesValid`, `lineCoveragePercent`). Commit the
refreshed `docs/assets/coverage-badge.svg` together with a note of the measurement revision when
the number changes materially.

## Scope and honesty

- The badge reflects the measured portable suite on Linux at the recorded revision. It is not a
  claim of production coverage, fault-injection coverage, Windows GUI coverage or model-quality
  coverage.
- Windows-only tests (WPF UI automation, desktop E2E, `layout05`) are not instrumented; their
  evidence is tracked separately in `docs/testing/`.
- CI does not fail on a coverage threshold yet; the number is informational.
