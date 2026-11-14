# Synthetic samples

Everything here is fictitious and safe to commit. There is no live market data, no
customer data and no external service connectivity.

| Path | Contents |
|---|---|
| `fixtures/watchlist.json` | Three synthetic instruments used by the Watchlist sample |
| `fixtures/risk.json` | Synthetic exposures/positions used by the Risk View sample |
| `fixtures/chart.json` | Deterministic synthetic yield series for the Chart sample |
| `contexts/contexts.json` | Trusted context catalog: `fdc3.instrument` and `aether.exposure` payloads with explicit classifications (`PUBLIC`/`RESTRICTED`) |
| `registry/apps.json` | Allowlisted app manifest for the headless sample processes (Linux CI/E2E) |
| `registry/apps.windows.json` | Allowlisted app manifest that launches the WPF sample windows on Windows |

## Launch templates and tokens

Registry `executable`/`arguments` values may contain `{TOKEN}` placeholders. Tokens are
supplied by the broker process at startup (for example `--token DOTNET=/usr/bin/dotnet`)
and are **never** derived from model output. The demo scripts supply:

- `{DOTNET}` — .NET muxer path (launches the sample assembly)
- `{SAMPLES_DLL}` — built `Aether.SampleApps.Headless.dll`
- `{AETHER_HOME}` — repository root (working directory and allowed executable root)
- `{FIXTURES_DIR}` — `samples/fixtures`
- `{WPF_SAMPLES_EXE}` — built `Aether.SampleApps` executable (Windows registry only)

The broker resolves the executable in PATH or as an absolute path, canonicalizes it, and
refuses to launch it unless it is under a configured `--allowed-root`. Sample apps must
be launched by the broker: they receive their single-use session token through the child
environment (`AETHER_SESSION_TOKEN`, `AETHER_BROKER_ENDPOINT`,
`AETHER_BROKER_TRANSPORT`, `AETHER_APP_ID`, `AETHER_INSTANCE_ID`).

## Adding a sample

1. Add synthetic fixture data (marked `"synthetic": true`) and any new catalog context
   with an explicit trusted classification.
2. Add a registry entry with the minimum capabilities (`context.subscribe`,
   `window.report`), allowed channels/context types/classifications and a bounded launch
   timeout.
3. Implement a headless role for CI/E2E if possible; WPF UI is optional and
   compile-verified only on non-Windows hosts.
4. Add tests: registration/readiness, context rendering, and a negative authorization
   path. Update docs and the synthetic-content checks.
