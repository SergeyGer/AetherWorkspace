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
| `demo-hero.mp4` | **Narrated final hero** (English neural voiceover, social-preview intro, 1 s crossfades, GitHub outro; H.264 1080p30 + AAC 48 kHz, 93.37 s) | `81a50060edf3ce59de9a894eae239669a35f47471efba005bc3028f209ccdc16` |
| `demo-hero-capture.mp4` | Validated clean Track B interface capture with corrected captions (H.264/1080p30, 81.37 s, taskbar hidden, planner readable) | `9a036a92d773321674063f2a8e733a49b798d6cf861cf942c926b719442ecf24` |
| `demo-preview.gif` | Animated ticker preview from the same rebuilt capture | `13022461e3b7c3120a4138dd4f70d6785623c27826fb68efb3f955d3600ca8c4` |
| `social-preview.png` | Mobile-first final-state social preview (1280×640) | `efa6c3640cfce32ba07d784636126f520a804ba4ec661067f329a6b6701a5c9f` |

Source evidence: `docs/testing/evidence/2026-11-18-v1.1-track-b-demo-gate.md` (capture) and
`docs/testing/evidence/2026-11-18-v1.1-narrated-demo.md` (narrated derivative). The voiceover is
synthetic neural TTS; no human voice is claimed. Raw recordings and provider credentials remain local.
