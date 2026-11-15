# Windows 11 reference-desktop local orchestrator

Local-only test runner implementing the published
[Reference Desktop lifecycle](../../docs/testing/reference-desktop.md) (Addendum A01).
This directory is **not** part of CI and must never be used to register the laptop as a GitHub
runner. It runs only a previously reviewed full commit SHA in a non-elevated interactive
session.

## Layout

```
C:\AetherWorkspace\trusted      controlled clone of the repository
C:\AetherWorkspace\automation   locally reviewed runner scripts/config (not overwritten by repo updates)
C:\AetherWorkspace\runs         detached worktrees per approved SHA/run
C:\AetherWorkspace\results      ACL-restricted TRX/manifests/redacted logs/screenshots (retention)
```

## Usage

```powershell
# 1. Approve a reviewed revision (full 40-hex SHA, one line)
Set-Content C:\AetherWorkspace\automation\approved-revision.txt '<40-hex-sha>'

# 2. Run a profile
pwsh -NoProfile -File .\Run-AetherTests.ps1 `
  -Profile daily-smoke `
  -ApprovedRevisionFile C:\AetherWorkspace\automation\approved-revision.txt
```

Profiles: `developer-smoke`, `daily-smoke`, `release-full`. Optional
`test-config.example.json` documents paths/preflight/privacy settings; copy it to
`test-config.json` in the automation directory if you want to override defaults.

Exit/result semantics: `PASS`, `FAIL_PRODUCT`, `FAIL_HARNESS`, `SKIPPED_LOCKED`,
`SKIPPED_ASLEEP`, `SKIPPED_UNAPPROVED_SHA`, `SKIPPED_HARDWARE`, `INCONCLUSIVE` — written to
`results\<run-id>\run-result.json` together with `environment-manifest.json` (OS build,
hardware/display/DPI, .NET SDK, SHA, suite version) and TRX logs. A locked/asleep session never
fails the product; it is recorded as a skip.

## Task Scheduler

Create a **per-user** task: “Run only when user is logged on”, least privileges (not SYSTEM),
no wake/unlock, single instance; trigger at logon or at a convenient daily time. Do not expose
an HTTP endpoint, SSH hook or runner agent.

## Existing automated commands (run by CI and locally)

```powershell
dotnet build AetherWorkspace.slnx -c Release
dotnet build tests/Aether.Shell.UiTests/Aether.Shell.UiTests.csproj -c Release
dotnet test  tests/Aether.Shell.UiTests/Aether.Shell.UiTests.csproj -c Release --no-build --filter "Category=UI"
dotnet build tests/Aether.Desktop.E2E.Tests/Aether.Desktop.E2E.Tests.csproj -c Release
dotnet test  tests/Aether.Desktop.E2E.Tests/Aether.Desktop.E2E.Tests.csproj -c Release --no-build --filter "Category=ReferenceDesktop|Category=Privacy04"
```

The Windows Named Pipes GUI E2E project (`tests/Aether.Desktop.E2E.Tests`) is not in
`AetherWorkspace.slnx`; CI builds/tests it in the dedicated Windows UI lane.
