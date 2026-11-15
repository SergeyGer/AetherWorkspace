# Aether.Shell UI automation tests (Windows only)

End-to-end UI automation for the WPF shell (`src/Aether.Shell`) using
[FlaUI](https://github.com/FlaUI/FlaUI) 5 (UIA3) and xUnit. These tests launch a real
broker through `Aether.Testing.EndToEndHarness` (Unix-domain-socket local transport),
start the compiled `Aether.Shell.exe` as a separate process with
`AETHER_ENDPOINT_FILE`, and drive the window through Windows UI Automation.

## What is covered

- **Smoke**: the shell window (`AetherWorkspace`) opens, reports the
  `unix-domain-socket` backend, and exposes the command bar, run/cancel buttons,
  classification combo, consent checkbox, steps list and status box.
- **Restricted reference workflow**: with `RESTRICTED` classification and the default
  reference command, clicking **Run workflow** completes within the 150 s poll budget,
  the status reports `Workflow succeeded`, the result tab contains a `Private summary`
  with no `ACC-` identifiers, and the reported trace is a 32-hex trace id that also
  appears in the trace/audit tab.

Both tests return immediately on non-Windows operating systems, so a Linux
cross-build proves compilation only and never claims to have verified the UI.

## Prerequisites

- Windows 10/11 with an **interactive, unlocked desktop session** (UI Automation cannot
  drive a locked screen or Session 0). The test uses UIA patterns (`Invoke`,
  `SelectionItem`), so the windows do not need to be foreground.
- .NET 10 SDK.
- Build outputs for the same configuration as the tests, in particular:
  - `src/Aether.Shell/bin/<Configuration>/net10.0-windows/Aether.Shell.exe`
  - `src/Aether.BrokerApp`, `src/Aether.SampleApps.Headless` and
    `src/Aether.Shell.Headless` outputs used by the end-to-end harness.
- No network and no external broker are needed: the broker listens on a local
  Unix-domain socket created in the temp directory.

## Running locally (Windows)

```powershell
# Builds the broker, sample apps, headless shell and WPF shell required by the harness.
dotnet build AetherWorkspace.slnx -c Release

# Build and run only the UI automation tests (Category=UI).
dotnet build tests/Aether.Shell.UiTests/Aether.Shell.UiTests.csproj -c Release
dotnet test tests/Aether.Shell.UiTests/Aether.Shell.UiTests.csproj -c Release --no-build --filter "Category=UI"
```

Use `-c Debug` instead of `-c Release` for a debug run; the configuration is inferred
from the test process output directory (`bin/Debug` or `bin/Release`). To point at a
different shell build, set `AETHER_SHELL_EXE` to an absolute path (or a path relative
to the repository root):

```powershell
$env:AETHER_SHELL_EXE = 'C:\build\Aether.Shell.exe'
dotnet test tests/Aether.Shell.UiTests/Aether.Shell.UiTests.csproj -c Release --no-build
```

The tests wait up to 30 s for the main window, 20 s for the broker registration and
backend, up to 150 s for the workflow status, and use a 300 s xUnit timeout for the
workflow fact. Broker stdout/stderr are included in failure messages.

## CI

This project is **deliberately not part of `AetherWorkspace.slnx`**, so the Linux CI
`dotnet test AetherWorkspace.slnx` run never executes it. The dedicated `windows-ui`
job in `.github/workflows/ci.yml` (Windows runner) builds the solution and this project
explicitly, then runs it with `--no-build`:

```powershell
dotnet build AetherWorkspace.slnx -c Release
dotnet build tests/Aether.Shell.UiTests/Aether.Shell.UiTests.csproj -c Release
dotnet test tests/Aether.Shell.UiTests/Aether.Shell.UiTests.csproj -c Release --no-build --logger "trx;LogFileName=windows-ui-tests.trx"
```

## Notes and risks

- The shell must open `MainWindow` on startup (`StartupUri` in `src/Aether.Shell/App.xaml`
  or equivalent `OnStartup` code). If `Aether.Shell.exe` only starts a message loop,
  window attachment times out after 30 s even though the process is healthy.
- FlaUI/UIA3 requires a real Windows desktop. On `windows-latest` the runner has an
  interactive session, but a locked or headless session will make window attachment
  fail with a clear timeout message rather than silently pass.
- The workflow is timing-sensitive by nature (broker start, three sample processes,
  UI rendering). Polling is capped at 500 ms with generous budgets; if the shell is
  slow to render, the tests may need the `AETHER_SHELL_EXE` override and a re-run.
- Test artifacts (`.trx`) land in `tests/Aether.Shell.UiTests/TestResults/` when a
  logger is configured; the temporary broker run directory is deleted by the harness
  on disposal.
