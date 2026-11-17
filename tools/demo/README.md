# Local Windows 11 demo production pipeline (v1.1 AW-055/AW-056)

Remote engineering (DeepSeek Harness on Hetzner Linux) authors and reviews this pipeline but never
operates the laptop desktop, OBS or FFmpeg. Everything below runs **locally on the Windows 11
reference laptop** in an unlocked interactive session under an operator-approved 40-hex SHA.

## Prerequisites

- Windows 11 x64, unlocked session, AC power, ≥10 GB free disk.
- .NET 10 Desktop Runtime x64, PowerShell 7, Git, FFmpeg 7+ on PATH.
- OBS Studio 30+ with **Tools → WebSocket Server Settings** enabled, bound to `127.0.0.1`,
  authentication on. Set the password only in the local environment:
  `$env:AETHER_OBS_WEBSOCKET_PASSWORD = '<local password>'`.
- Local LLM provider configuration for Track B in a git-ignored `.env` at the trusted repository
  root. For the default OpenAI-compatible path the WPF shell reads **`OPENAI_API_KEY`,
  `OPENAI_BASE_URL` and `OPENAI_MODEL`** (`AETHER_MODEL_PROVIDER=openai`); `AETHER_CLOUD_*` may be
  used as endpoint/model fallbacks. For Gemini use `AETHER_MODEL_PROVIDER=gemini` plus
  `GEMINI_API_KEY/GEMINI_BASE_URL/GEMINI_MODEL`. Never commit or display it.

## 1. Prepare (trust + preflight + detached worktree)

```powershell
Set-Content C:\AetherWorkspace\automation\approved-revision.txt '<40-hex-approved-sha>' -Encoding ascii

pwsh -NoProfile -File tools/demo/Prepare-Demo.ps1 `
  -RepoRoot C:\AetherWorkspace\trusted `
  -ApprovedRevisionFile C:\AetherWorkspace\automation\approved-revision.txt
```

Creates `runs/demo-<stamp>-<sha8>` with a detached worktree, builds the solution and capture test
project, validates OBS WebSocket/FFmpeg/.NET/power/session and writes `demo-preparation.json`.

## 2. Record (Track B is the primary hero track)

```powershell
$env:AETHER_OBS_WEBSOCKET_PASSWORD = '<local OBS password>'
pwsh -NoProfile -File tools/demo/Record-Demo.ps1 `
  -RunDirectory C:\AetherWorkspace\runs\demo-<stamp>-<sha8> `
  -Scenario TrackB `
  -EnvironmentFile C:\AetherWorkspace\trusted\.env
```

`Record-Demo.ps1` starts OBS locally, runs the real named-pipe desktop workflow
(`Category=DemoCapture`), drives the real WPF sample windows for the VEGA/ORIN ticker scene, stops
OBS, copies the raw MKV and writes `capture-TrackB-<stamp>.json`. Track A is the same command with
`-Scenario TrackA` (deterministic, `usedModel=false`).

## 3. Build and validate media

```powershell
pwsh -NoProfile -File tools/demo/Build-Video.ps1 `
  -CaptureManifest C:\AetherWorkspace\runs\<demo-run>\capture-TrackB-<stamp>.json
pwsh -NoProfile -File tools/demo/Validate-Video.ps1 `
  -VideoBuildManifest C:\AetherWorkspace\runs\<demo-run>\video-build.json
```

Produces `demo-hero.mp4` (H.264/yuv420p/1920×1080, 80–95 s) and `demo-preview.gif` (8–12 s,
≤8 MB), captions keyed to measured event times from `demo-scenes.json`. Validation does not publish:
human review of the MP4/GIF and provenance is mandatory before README/Wiki publication.

## One-command physical gate (recommended)

`tools/windows-test/Run-PhysicalReleaseGate.ps1` chains the approved-SHA reference-desktop suite,
per-WT ledger, 100-cycle UI measurement and the Track B capture/build/validate into one local
`results\physical-gate-<stamp>` directory with `gate-summary.json`:

```powershell
$env:AETHER_OBS_WEBSOCKET_PASSWORD = '<local OBS password>'
cd C:\AetherWorkspace\trusted
pwsh -NoProfile -File tools\windows-test\Run-PhysicalReleaseGate.ps1 `
  -RepoRoot C:\AetherWorkspace\trusted `
  -ApprovedRevisionFile C:\AetherWorkspace\automation\approved-revision.txt `
  -ConfigPath C:\AetherWorkspace\automation\test-config.json `
  -EnvironmentFile C:\AetherWorkspace\trusted\.env `
  -ManualLedgerPath C:\AetherWorkspace\wt-manual-ledger.json
```

OBS must already run with WebSocket enabled and the MKV/1080p profile/scene configured. Add
`-SkipMeasurements` or `-SkipCapture` for partial runs; `-CaptureScenarios TrackA,TrackB` records both
tracks. Share `gate-summary.json` and the sanitized manifests only.

## Failure/skip codes

`SKIPPED_UNAPPROVED_SHA`, `SKIPPED_ENVIRONMENT`, `FAIL_HARNESS`, `FAIL_PRODUCT`, `FAIL_MEDIA`.
Raw media and logs stay local; only sanitized manifests/screenshots approved by the operator are
committed.

## Files

| File | Purpose |
|---|---|
| `Prepare-Demo.ps1` | approved-SHA trust, detached worktree, build, preflight, preparation manifest |
| `Obs-Control.ps1` | minimal local OBS WebSocket 5.x start/stop control (localhost only) |
| `Record-Demo.ps1` | OBS recording + demo scenario + raw capture manifest |
| `Build-Video.ps1` | FFmpeg hero MP4 + GIF, captions from measured events |
| `Validate-Video.ps1` | codec/dimensions/duration/pixel-format/GIF-size/black-frame QA |
| `demo-scenes.json` | Track A/B captions and scene timing source of truth |
| `obs-profile.example.json` | documented OBS setup; never contains the password |
