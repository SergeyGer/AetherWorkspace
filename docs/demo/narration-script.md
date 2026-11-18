# AetherWorkspace narrated demo — voiceover script

Final video structure: social-preview intro (≈12 s) → Track B interface capture (81.37 s) with
crossfades → GitHub outro card. One continuous professional voiceover (synthetic neural TTS,
English) with no deliberate gaps; the script below is the production source of truth.

## Script

AI can propose actions, but in real enterprise desktop work, a proposal is not enough.
AetherWorkspace is a local-first reference implementation for teams that need AI automation without
surrendering control. One request becomes a strict, versioned plan. Policy is enforced before
anything runs. And execution happens across independent desktop applications, with sensitive data
kept on the machine.

Here is the Track B flow. A natural-language request enters the shell. The configured model proposes
a plan. The deterministic executor validates every action and authorizes it against local policy.
Three independent Windows applications launch: a synthetic watchlist, a risk view, and a bond chart.
Each is a separate process with explicit capabilities, and they communicate only through the
authenticated local broker over Windows named pipes. The plan publishes typed context. The watchlist
selection synchronizes the risk and chart views. Selecting VEGA, then ORIN, changes the actual chart
series, not just a label. Every step is auditable: real process identifiers, a real trace, and
truthful provider provenance. Restricted data never leaves the machine. The model is a proposal
engine; the deterministic executor remains the source of authority.

That is AetherWorkspace: AI proposes, policy decides, execution is enforced. Explore the code, run
the demo locally, and contribute on GitHub at github dot com slash Sergey Ger slash Aether
Workspace.
