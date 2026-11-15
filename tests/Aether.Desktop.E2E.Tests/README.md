# Windows 11 reference-desktop E2E tests (Windows only)

End-to-end tests for the **reference desktop**: the real WPF shell
(`src/Aether.Shell`), the real broker (`src/Aether.BrokerApp`) over the **Windows
named-pipe** transport, the three independent headless sample processes, and
[FlaUI](https://github.com/FlaUI/FlaUI) 5 (UIA3) driving the shipped window.

This project implements the automated part of the v3.0 Addendum A01 Windows 11
reference-desktop test strategy. The lane/coverage description, release gates and artifact
schema live in [`docs/testing/windows11-reference.md`](../../docs/testing/windows11-reference.md)
and the run lifecycle in [`docs/testing/reference-desktop.md`](../../docs/testing/reference-desktop.md).

## Test IDs

| ID | Test | What it proves |
|---|---|---|
| **WT-04-01** | `GuiWorkflowRunsOverWindowsNamedPipes` | The shipped WPF shell starts against a real `Aether.BrokerApp --transport named-pipe` handshake file, the backend label reports `named-pipe`, the default RESTRICTED reference workflow runs from the **Run workflow** button, the status reports `Workflow succeeded` with a valid 32-hex trace id, the summary tab shows the private local summary, and an operator `BrokerClient` (endpoint + operator token from the handshake file) confirms all three sample apps `HEALTHY`, the three reported windows arrange side-by-side, and the broker audit plus child-process logs carry the same trace id. |
| **WT-04-02** | `RestrictedClassificationNeverShowsExternalProvider` | The same GUI run keeps `RESTRICTED` selected with cloud consent unchecked, the visible plan/summary provider labels are `deterministic-grammar` and `local-deterministic` with `model=False`, the summary states that no external provider was called, and no `cloud`/external-provider success indicator or `ACC-` account token appears in the backend, status, summary or trace surfaces. |
| **WT-04-07** | (orchestrator, not an xUnit fact) | Run provenance: the approved 40-hex SHA, isolated worktree build, environment manifest and PASS/FAIL/SKIPPED result artifact are produced by [`tools/windows-test/Run-AetherTests.ps1`](../../tools/windows-test/Run-AetherTests.ps1); this project is the `desktop-e2e` suite it executes. |

Both facts carry `[Trait("Category", "ReferenceDesktop")]`; WT-04-02 additionally carries
`[Trait("Category", "Privacy04")]`. Selected filters:

```powershell
dotnet test tests/Aether.Desktop.E2E.Tests/Aether.Desktop.E2E.Tests.csproj -c Release --filter "Category=ReferenceDesktop"
dotnet test tests/Aether.Desktop.E2E.Tests/Aether.Desktop.E2E.Tests.csproj -c Release --filter "Category=Privacy04"
```

## What is covered

1. **Named-pipe GUI workflow.** One helper (`NamedPipeBrokerHarness`) starts the real
   `Aether.BrokerApp` process with `--transport named-pipe` and waits for **both** readiness
   signals: the stdout line `AETHER_BROKER_READY {json}` and the atomically written
   endpoint file passed via `--endpoint-file`; the helper validates that transport, pipe
   name, operator app id, endpoint file path and audit file path agree.
2. **GUI assertions through UIA3.** `ShellWindowSession` launches the compiled
   `Aether.Shell.exe` with `AETHER_ENDPOINT_FILE`, attaches to the `AetherWorkspace`
   window and reads the automation ids defined in `src/Aether.Shell/MainWindow.xaml`
   (`BackendModeText`, `CommandBox`, `ClassificationCombo`, `CloudConsent`, `RunButton`,
   `CancelButton`, `StatusBox`, `SummaryBox`, `TraceBox`, `StepsList`).
3. **Operator correlation.** After the GUI run, the helper registers a separate operator
   `BrokerClient` from the handshake file's `operatorAppId`/`operatorToken` and asserts:
   - `HealthProbe` reports the named-pipe transport and `sample.watchlist=HEALTHY`,
     `sample.risk=HEALTHY`, `sample.chart=HEALTHY`;
   - `GetSnapshot` lists the three launchable sample apps with the expected broker version;
   - `ArrangeWindows` still applies a side-by-side layout with exactly three placements;
   - the broker audit JSONL contains a succeeded `broker.launch` event for each sample app
     and a `context.publish`/`window.arrange` event, all under the GUI trace id;
   - each child-process log contains the GUI trace id.

Both facts are timing-bounded (30–180 s waits). On a non-Windows host they return before
touching FlaUI, so a Linux cross-build is compilation evidence only — it never claims UI
verification.

## Prerequisites

- **Windows 11 x64** with an **interactive, unlocked desktop session**. UI Automation cannot
  drive a locked screen or Session 0; run the tests from an interactive user session (the
  orchestrator records `SKIPPED_LOCKED` instead of running them on a locked desktop).
- .NET 10 SDK (and the Windows Desktop runtime, included with the SDK) so the WPF shell and
  the test host can start.
- Build outputs for the same configuration as the tests:
  - `src/Aether.Shell/bin/<Configuration>/net10.0-windows/Aether.Shell.exe`
  - `src/Aether.BrokerApp`, `src/Aether.SampleApps.Headless` (built through the project
    references; the broker launches the samples with the `DOTNET`/`SAMPLES_DLL` tokens)
- No network and no external broker: the broker is a local named pipe; the workflow's
  deterministic path performs no external provider calls.

## Build and run (Windows)

```powershell
# Build the shell, broker, sample apps and this test project.
dotnet build AetherWorkspace.slnx -c Release
dotnet build tests/Aether.Desktop.E2E.Tests/Aether.Desktop.E2E.Tests.csproj -c Release -p:EnableWindowsTargeting=true

# Run both reference-desktop facts.
dotnet test tests/Aether.Desktop.E2E.Tests/Aether.Desktop.E2E.Tests.csproj -c Release --no-build `
  --logger "trx;LogFileName=desktop-e2e.trx" --results-directory artifacts/desktop-e2e
```

`-c Debug` works the same way; the configuration is inferred from the test assembly output
directory (`bin/Debug` or `bin/Release`). The compiled shell and broker are resolved from
the same repository root.

## Configuration

| Variable | Purpose |
|---|---|
| `AETHER_SHELL_EXE` | Absolute path (or path relative to the repository root) of the shell executable to drive, for non-standard builds. |
| `AETHER_BROKER_EXE` | Absolute path (or path relative to the repository root) of the broker apphost; by default the built apphost is used, falling back to `dotnet Aether.BrokerApp.dll`. |
| `AETHER_DESKTOP_E2E_KEEP_ARTIFACTS` | Set to `1` to keep each test run directory (handshake file, broker audit JSONL, broker stdout/stderr, child logs) for post-mortem inspection. The operator token is in that directory — never commit or upload it. |
| `AETHER_DESKTOP_E2E_ARTIFACTS` | Directory that hosts the temporary run directories; defaults to the OS temp directory. |

## Not in the solution

This project is **deliberately not part of `AetherWorkspace.slnx`**, exactly like
`tests/Aether.Shell.UiTests`. The Linux CI job therefore never builds or runs it. The
project cross-compiles on Linux (`EnableWindowsTargeting=true`) but the produced test host
requires the Windows Desktop runtime and an interactive desktop: local Windows execution is
the only evidence path. The `tools/windows-test` orchestrator runs it from a detached
worktree of an **approved** commit, never from a GitHub-hosted runner.

## Troubleshooting

| Symptom | Likely cause / fix |
|---|---|
| `The WPF shell did not expose a main window within 60s` | Locked/RDP-disconnected session, Session 0 service context, or a shell build that does not open `MainWindow` (`StartupUri`). Log on locally, unlock the desktop and rerun. |
| `The Aether.BrokerApp process exited ... before becoming ready` | Broker startup error; the broker stderr tail is included in the failure message. Rebuild `src/Aether.BrokerApp` for the same configuration. |
| `Did not reach broker registration` / `unavailable` backend | The shell cannot open the named pipe or the handshake file: check that `AETHER_ENDPOINT_FILE` points at the harness file and that no stale run is holding the same pipe. |
| Workflow times out at 180 s | Sample process start-up on a loaded reference desktop is slow; check `child-logs/` (kept with `AETHER_DESKTOP_E2E_KEEP_ARTIFACTS=1`) and the broker stderr tail. |
| `The broker did not report all sample apps HEALTHY` | A sample process exited; the broker stderr and child logs include the launched process output. |
