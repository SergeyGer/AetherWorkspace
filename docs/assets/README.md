# Brand and repository preview assets

## Social preview

- [`social-preview.png`](social-preview.png) — **1280 × 640** raster for GitHub's repository
  social preview (link unfurls on social platforms).
- [`social-preview.svg`](social-preview.svg) — editable vector source (same layout).

Design language (v1.1 final-state refresh, 2026-11-18): strict banking/institutional template —
deep navy field, restrained gold rules and guilloche ornament, serif wordmark and **large mobile-first
typography** with no fine print. Primary message: “AI proposes. Policy decides. Execution is
enforced.” Secondary product line: “Hybrid AI · Privacy gateway · Heterogeneous UI” (the v1.1→v1.5
direction). Every text element is at least 42 px at 1280×640 so the preview stays legible on a
smartphone. Palette: `#06131F`–`#0C2539` (navy), `#C8A45C` / `#E7CD92` (gold), `#F4EFE4` (ivory),
`#93A7B8` (muted slate). Type: DejaVu Serif for the wordmark, DejaVu Sans Bold for messaging.

**Set it on GitHub:** repository → **Settings → General → Social preview → Edit → Upload**
`docs/assets/social-preview.png`. (GitHub has no API for this setting.)

Regenerate after editing the SVG with any SVG rasterizer; the committed PNG was rendered with
`sharp` at 2× then downscaled to 1280 × 640:

```bash
npm install sharp
node -e "const sharp=require('sharp');const fs=require('fs');sharp(fs.readFileSync('docs/assets/social-preview.svg'),{density:288}).resize(1280,640,{fit:'cover'}).png().toFile('docs/assets/social-preview.png')"
```

## Coverage badge

[`coverage-badge.svg`](coverage-badge.svg) is generated from measured portable-suite coverage by
`tools/coverage/summarize-coverage.py` (see [`docs/testing/coverage.md`](../testing/coverage.md)).
Do not hand-edit it; regenerate after a coverage measurement.

## Demo media (v1.1)

| File | Description | SHA-256 |
|---|---|---|
| `demo-hero.mp4` | Validated clean Track B hero capture (H.264/1080p/yuv420p, 81.37 s, taskbar hidden, planner readable) | `4a452df5b86ebd659d458e1c708524e8c97530975051ff09135ab48787367ea3` |
| `demo-preview.gif` | Animated preview derived from the same capture (367 KB) | `73bb03b8145cc0d2d85d9977edf6650afe5145d1bfc59bbbfb4cffcef4d90904` |

Source evidence: `docs/testing/evidence/2026-11-18-v1.1-track-b-demo-gate.md`. The files are local
Windows 11 captures of synthetic data; raw recordings and provider credentials are not committed.
