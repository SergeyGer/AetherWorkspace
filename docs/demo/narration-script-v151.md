# AetherWorkspace v1.5.1 hero — voiceover script

Production source of truth for the **V151** hero. The video has **no burned-in captions**; all
explanation is delivered by one continuous synthetic English voiceover, timed against the captured
video and scene events. No human voice or new functional claim is made by the narration.

## Script

AI can propose actions, but in real enterprise desktop work, a proposal is not enough.

AetherWorkspace is a local-first reference implementation for AI automation without surrendering
control.

This is the AI Command Center. The operator types a natural-language instruction, selects the WPF
watchlist, the risk view, and the independent WebView2 chart, and sets launch scope to Strict. The
model can propose, but it cannot widen that scope.

The deterministic executor validates the complete plan and authorizes every action against local
policy. Anything outside the selected applications is denied before the first step runs.

Three independent processes launch through the authenticated local broker over Windows named pipes.
Typed context synchronizes the watchlist, risk view, and chart.

Selecting VEGA, then ORIN, changes the actual chart series, not just a status label. Every step is
auditable with real process identifiers, a real trace, and truthful deterministic provenance.
Restricted data stays on the machine.

That is AetherWorkspace: AI proposes, policy decides, execution is enforced. Explore the code, run
the demo locally, and contribute on GitHub.

## Production

- Voice: Microsoft Edge neural TTS, `en-US-AndrewNeural`, rate `+3%`, pitch `-2 Hz` (synthetic; no
  human voice claimed).
- Audio asset: `docs/demo/media/narration-v151.mp3`.
- Video: V151 capture, no subtitles or caption burn-in; H.264 1080p30 + AAC 48 kHz stereo.
- Review: listen at normal speed against the video before publication; the voiceover explains the
  visible workflow and must not introduce latency, compliance, PII or model claims.
- Raw capture/provider context stays local; only approved final media and sanitized manifests are
  published.
