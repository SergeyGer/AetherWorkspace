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

## Demo media (v1.5.2)

**v1.5.2 presentation patch:** the reviewed V151 hero with the previous hero's intro and outro
cards/voiceover added. No runtime changes. Caption-free; synthetic English voiceover.

| File | Description | SHA-256 |
|---|---|---|
| `demo-hero-v152.mp4` | V151 hero + previous intro/outro (84.9 s, H.264 1080p30 + AAC 48 kHz stereo) | `799d686b0f7fcddd8e3338b861d1424f94d9683699a6c128875f34f48c974fcf` |
| `demo-preview-v152.gif` | 10 s preview of the composed hero | `3c024d196026da5cb6e4c267a4f29fb7ba9b9a7732f86150b5a6a3821f43ed24` |

Evidence: [`2026-11-19-v1.5.2-hero-intro-outro.md`](../testing/evidence/2026-11-19-v1.5.2-hero-intro-outro.md).

## Demo media (v1.5.1)

**v1.5.1 hero:** AI Command Center → Strict workspace → WPF + WebView2 execution. Synthetic English
voiceover, **no on-screen captions**; deterministic provenance (`usedModel=false`). The approved MP4
was reviewed on a physical Windows 11 render; YouTube publication is pending explicit operator
approval.

| File | Description | SHA-256 |
|---|---|---|
| `demo-hero-v151.mp4` | Approved v1.5.1 hero (76.8 s, H.264 1080p30 + AAC 48 kHz stereo, caption-free synthetic English voiceover) | `90eac9809d44b4e9adefb6e8426173679f06035dd81242d46afafded00f813b3` |
| `demo-preview-v151.gif` | Animated 10 s preview re-derived from the approved MP4 (960×540, no captions) | `a341b70cc5544d403ba74d1855ceb24bfdbc01d3985c23f398d3b8133b623bde` |
| `../demo/media/narration-v151.mp3` | Synthetic English voiceover source | `a31f43a43d5003b4f7e4e7d46e76debc413f1f06df672d452c6c2851274f93d1` |

Evidence: [`2026-11-19-v1.5.1-final-gate.md`](../testing/evidence/2026-11-19-v1.5.1-final-gate.md)
and [`narration-script-v151.md`](../demo/narration-script-v151.md). The prior v1.1 narrated Track B
hero remains below as historical media.

## Demo media (v1.1)

**Published narrated demo:** [YouTube — AetherWorkspace Track B](https://youtu.be/CFJJZX-QcnU)
(English synthetic voiceover). The repository MP4 is the same final media derived from the validated
capture.

| File | Description | SHA-256 |
|---|---|---|
| `demo-hero.mp4` | **Narrated final hero** (English neural voiceover, social-preview intro, 1 s crossfades, GitHub outro; H.264 1080p30 + AAC 48 kHz, 93.37 s) | `81a50060edf3ce59de9a894eae239669a35f47471efba005bc3028f209ccdc16` |
| `demo-hero-capture.mp4` | Validated clean Track B interface capture with corrected captions (H.264/1080p30, 81.37 s, taskbar hidden, planner readable) | `9a036a92d773321674063f2a8e733a49b798d6cf861cf942c926b719442ecf24` |
| `demo-preview.gif` | Animated ticker preview from the same rebuilt capture | `13022461e3b7c3120a4138dd4f70d6785623c27826fb68efb3f955d3600ca8c4` |
| `social-preview.png` | Mobile-first final-state social preview (1280×640) | `efa6c3640cfce32ba07d784636126f520a804ba4ec661067f329a6b6701a5c9f` |

Source evidence: `docs/testing/evidence/2026-11-18-v1.1-track-b-demo-gate.md` (capture) and
`docs/testing/evidence/2026-11-18-v1.1-narrated-demo.md` (narrated derivative). The voiceover is
synthetic neural TTS; no human voice is claimed. Raw recordings and provider credentials remain local.
