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

# 2. Run a profile (from a copy under automation, pass -RepoRoot explicitly)
pwsh -NoProfile -File C:\AetherWorkspace\automation\Run-AetherTests.ps1 `
  -Profile daily-smoke `
  -RepoRoot C:\AetherWorkspace\trusted `
  -ApprovedRevisionFile C:\AetherWorkspace\approved-revision.txt

# Alternatively run the script directly from the trusted checkout (RepoRoot defaults
# to the checkout root):
# pwsh -NoProfile -File .\tools\windows-test\Run-AetherTests.ps1 -Profile release-full `
#   -ApprovedRevisionFile C:\AetherWorkspace\approved-revision.txt
```

Profiles: `developer-smoke` (1 reference-desktop GUI E2E), `daily-smoke` (+2 WPF UI automation
tests), `release-full` (portable solution suites + both Windows UI suites). The
`desktop-e2e` suite runs the two `ReferenceDesktop`-traited tests (WT-04-01 and WT-04-02).
Optional `test-config.example.json` documents paths/preflight/privacy settings; copy it to
`test-config.json` in the automation directory if you want to override defaults.

Recorded run: 2026-11-16, `release-full` **PASS** on a physical Windows 11 laptop for approved
commit `e7007af` — see
[`docs/testing/evidence/2026-11-16-windows11-release-full.md`](../../docs/testing/evidence/2026-11-16-windows11-release-full.md).

Exit/result semantics: `PASS`, `FAIL_PRODUCT`, `FAIL_HARNESS`, `INCONCLUSIVE`,
`SKIPPED_LOCKED`, `SKIPPED_UNAPPROVED`, `SKIPPED_OVERLAP`, `SKIPPED_ON_BATTERY`,
`SKIPPED_NO_INTERACTIVE_SESSION`, `SKIPPED_INSUFFICIENT_RESOURCES` — written to
`results\<run-id>\run-result.json` together with `environment-manifest.json` (OS build,
hardware/display/DPI, .NET SDK, SHA, suite version), `run.log` and TRX evidence. Skips exit
with code 0; product failures with 1 and harness failures with 2. A locked/asleep session never
fails the product; it is recorded as a skip. Solution test runs keep one TRX per test project
and the orchestrator aggregates their counters (fixed in v0.4.1).

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
