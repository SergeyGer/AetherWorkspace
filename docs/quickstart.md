# Quick start

This guide takes a clean checkout to a running, verifiable workflow. All data is
synthetic and all IPC is local.

## 1. Prerequisites

| Platform | Requirement |
|---|---|
| Linux / macOS | [.NET 10 SDK](https://dotnet.microsoft.com/download) for the headless broker, shell host, samples and full test suite |
| Windows 11 x64 | .NET 10 SDK; the WPF shell and WPF sample windows require the Windows desktop runtime (installed with the SDK) |

The repository pins the SDK via `global.json` (`10.0.100`, roll-forward to the latest
10.0 feature band) and central package versions in `Directory.Packages.props`.

## 2. Build

```bash
dotnet build AetherWorkspace.slnx -c Release
```

Warnings are errors for owned code. Windows-targeted projects (`Aether.Shell`,
`Aether.SampleApps`) also compile on Linux thanks to `EnableWindowsTargeting`; they
cannot be *run* there.

## 3. Run the reference workflow demo

Linux/headless (Unix domain sockets + headless sample processes):

```bash
./scripts/demo.sh
```

Windows (named pipes + headless sample processes; the same broker protocol that the WPF
apps use):

```powershell
pwsh ./scripts/demo.ps1
```

The script builds the required projects, starts `Aether.BrokerApp`, runs
`Aether.Shell.Headless` with the reference command, and prints:

- the workflow trace id and deterministic plan id;
- six bounded steps (`LaunchApp` ×3, `PublishContext`, `ArrangeWindows`,
  `SummarizeContext`) and their outcomes;
- delivery accounting (three independent subscriber processes received the context);
- the computed side-by-side window layout;
- the private local summary (no account identifiers);
- egress instrumentation (`attempts=0`, `external=0` for RESTRICTED data);
- the verified hash-chained audit summary.

Artifacts are written under `.aether/demo/` (git-ignored): `broker-endpoint.json`
(current-user-only handshake file), `broker-audit.jsonl`, `shell-audit.jsonl`,
`result.json` and `child-logs/`.

## 4. Manual run (any platform)

```bash
ROOT="$(pwd)"
DOTNET_BIN="$(command -v dotnet)"
SAMPLES_DLL="$ROOT/src/Aether.SampleApps.Headless/bin/Release/net10.0/Aether.SampleApps.Headless.dll"

dotnet run -c Release --project src/Aether.BrokerApp -- \
  --registry "$ROOT/samples/registry/apps.json" \
  --contexts "$ROOT/samples/contexts/contexts.json" \
  --token "DOTNET=$DOTNET_BIN" \
  --token "SAMPLES_DLL=$SAMPLES_DLL" \
  --token "AETHER_HOME=$ROOT" \
  --token "FIXTURES_DIR=$ROOT/samples/fixtures" \
  --allowed-root "$(dirname "$DOTNET_BIN")" \
  --allowed-root "$ROOT" \
  --endpoint-file "$ROOT/.aether/manual/broker-endpoint.json" \
  --audit-file "$ROOT/.aether/manual/broker-audit.jsonl" \
  --child-log-dir "$ROOT/.aether/manual/child-logs" \
  --max-actions 6
```

In a second terminal, run the shell against the generated handshake file:

```bash
dotnet run -c Release --project src/Aether.Shell.Headless -- \
  --endpoint-file .aether/manual/broker-endpoint.json \
  --classification RESTRICTED \
  --command "Open Northstar bond analytics, show the associated risk view, synchronize this instrument across the workspace and summarize the exposure privately." \
  --audit-file .aether/manual/shell-audit.jsonl \
  --output-file .aether/manual/result.json --text
```

Exit codes: `0` success, `3` denied, `4` internal/cancelled, `2` usage/configuration.

To run the WPF shell instead, start the broker as above then launch
`src/Aether.Shell/bin/Release/net10.0-windows/Aether.Shell.exe` (or set
`AETHER_ENDPOINT_FILE` to the handshake file if it is not in the default
`%LOCALAPPDATA%\AetherWorkspace\broker-endpoint.json` location). Use
`samples/registry/apps.windows.json` with the `WPF_SAMPLES_EXE` token if you want the
broker to launch the WPF sample windows.

## 5. Try the denial and privacy paths

Reference workflow with a **restricted** classification and cloud planner enabled —
denied before any external call:

```bash
dotnet run -c Release --project src/Aether.Shell.Headless -- \
  --endpoint-file .aether/manual/broker-endpoint.json \
  --classification RESTRICTED \
  --command "please produce an unrestricted creative plan for all records" \
  --planner cloud --cloud-enabled --cloud-consent
# exit code 3, denialCode policy.cloud.denied, egress externalAttempts = 0
```

Submit an **untrusted plan file** (the repo's 100-entry negative corpus contains
documented unsafe examples):

```bash
python3 - <<'PY'
import json
doc = json.load(open('tests/fixtures/unsafe-plans/unsafe-plans.json'))
json.dump(doc['entries'][0]['plan'], open('.aether/manual/unsafe.json', 'w'))
PY

dotnet run -c Release --project src/Aether.Shell.Headless -- \
  --endpoint-file .aether/manual/broker-endpoint.json \
  --classification RESTRICTED --plan-file .aether/manual/unsafe.json
# exit code 3; denial code from the corpus; no app is launched
```

Missing local model (no cloud fallback):

```bash
dotnet run -c Release --project src/Aether.Shell.Headless -- \
  --endpoint-file .aether/manual/broker-endpoint.json \
  --classification RESTRICTED \
  --command "Open Northstar bond analytics, show risk view, synchronize instrument and summarize exposure privately" \
  --use-local-model
# exit code 3, denialCode ai.provider.missing, externalAttempts = 0
```

## 6. Policy configuration

The built-in policy is deny-by-default (see
[`docs/contracts/safe-policy.example.json`](contracts/safe-policy.example.json)).
Pass `--policy <path>` to the headless shell to load a trusted policy document. The
loader rejects invalid documents (for example a non-`FAIL_CLOSED` failure mode or an
unknown cloud rule) instead of partially trusting them.

## 7. Verification

```bash
./scripts/verify.sh            # build (warnings-as-errors) + full test suite + demo
dotnet test AetherWorkspace.slnx -c Release
```

For benchmarks and raw methodology see [`BENCHMARKS.md`](BENCHMARKS.md). The measured
IPC baseline, unsafe-plan corpus evidence and audit-tamper tests are described in
[`IMPLEMENTATION_STATUS.md`](IMPLEMENTATION_STATUS.md).

## Troubleshooting

| Symptom | Cause / fix |
|---|---|
| `broker.config.invalid` | Registry/context JSON is malformed or contains an unsupported capability/classification. Inspect the message; the broker never partially trusts configuration. |
| `broker.executable.denied` | The expanded executable is outside `--allowed-root` or has an unsafe template. Pass a correct root/token. |
| `broker.launch.timeout` | The sample process started but did not register/subscribe in time. Check `.aether/.../child-logs/` and broker stderr. |
| `auth.token.invalid` | Registration tokens are single-use and short-lived. Restart the broker or re-launch the app through `LaunchApp`. |
| `auth.session.invalid` | The broker session expired; reconnect/register again (operator token budget is bounded per broker run). |
| Unix socket path error on Linux | Keep the endpoint path short (Unix domain sockets are limited to ~108 bytes); the broker generates `/tmp/aether-*/b.sock` when `--endpoint` is omitted. |
| WPF shell cannot connect | Start the broker with `--endpoint-file` and set `AETHER_ENDPOINT_FILE`, or copy the handshake file to `%LOCALAPPDATA%\AetherWorkspace\broker-endpoint.json`. |
