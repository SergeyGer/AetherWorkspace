# Windows 11 local demo capture runbook

**Scope:** local interactive execution on the physical Windows 11 reference laptop. The remote
Linux engineering environment (DeepSeek Harness/Hetzner) authors and reviews these steps but never
operates the desktop. Tooling: PowerShell 7, FlaUI, OBS Studio (local WebSocket on 127.0.0.1),
FFmpeg. Scripts are specified by AW-055 (#83) and AW-056 (#84); commands below are the target
contract and are **not** claimed available until implemented.

## 1. Trust and preparation

```powershell
# Operator-controlled file with a full approved commit SHA
Set-Content C:\AetherWorkspace\automation\approved-revision.txt '<40-hex-approved-SHA>'

git -C C:\AetherWorkspace\trusted fetch origin
git -C C:\AetherWorkspace\trusted cat-file -t '<40-hex-approved-SHA>'   # must print: commit
```

- Create a detached worktree from the approved SHA; build/test there; never use unpinned working
  copies.
- Clean desktop: notifications off, personal windows closed, target capture region only.
- Synthetic fixtures only; no credentials, customer data or real market data.

## 2. Preflight (SKIPPED/INCONCLUSIVE, never product FAIL)

- Interactive, unlocked user session; AC power; sufficient disk; expected monitor profile/capture
  region; .NET Desktop runtime; OBS and FFmpeg present; free/local OBS WebSocket; no unknown apps
  in capture region.
- Display/DPI changes are out of scope here and must follow the approval-only plan in AW-065.

## 3. Capture sequence

1. Start only owned broker/shell processes and reset the demo-owned layout/trace directory.
2. Start OBS via local authenticated 127.0.0.1 WebSocket; wait for recording confirmation; timestamp
   zero and write the event manifest.
3. Drive the demo by FlaUI AutomationIds and row identifiers (no coordinates); assert real state:
   selected ticker, Risk metrics, Chart series, PIDs, trace id.
4. Stop OBS; verify source media; finalize event manifest; terminate only owned process IDs.

## 4. Failure codes

| Code | Meaning |
|---|---|
| `SKIPPED_UNAPPROVED_SHA` | approved file missing/malformed or revision mismatched |
| `SKIPPED_ENVIRONMENT` | locked session, power/disk/display/OBS/FFmpeg preflight failure |
| `FAIL_HARNESS` | tooling/harness error before a truthful capture could be made |
| `FAIL_PRODUCT` | product assertion failed during the demo sequence |
| `FAIL_MEDIA` | output validation failed (codec/duration/black frames/captions/sensitive content) |

Failures retain raw local media/logs for operator review; no “successful” video is published.

## 5. Editing and validation

- MKV source → FFmpeg 1080p H.264/yuv420p MP4 (80–95 s) + 8–12 s GIF (≤8 MB), captions keyed to
  measured event timestamps.
- Validate codec, dimensions, duration, audio policy, black frames, caption readability, real
  PIDs/trace/tickers against the manifest; scan for synthetic-only content and secrets.
- Human approval is required before README/Wiki publication; sanitized manifest only, raw media
  remains local unless explicitly approved.

## 6. Evidence hand-off

Commit sanitized manifests, storyboard/acceptance updates and approved still/short preview assets
only. Never commit raw full-desktop media, OBS profiles with credentials, endpoint tokens or logs
containing personal data.
