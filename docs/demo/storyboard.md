# v1.1 hero demo storyboard (source of truth)

Baseline: published **v1.0.2** Windows desktop bundle
(`AetherWorkspace-v1.0.2-windows-x64-desktop.zip`, SHA-256
`9f5101ddd57e141d74ca6c16d7f18c2188e82dc176a94f24a4c4f6534aa83046`), approved commit recorded at
production time. Video assets are **not yet produced** — everything below is a target until
captured with the local Windows 11 harness (AW-055/AW-056).

## Delivery targets

| Asset | Target |
|---|---|
| `demo-hero.mp4` | 80–95 s, H.264, yuv420p, 1920×1080, silent-first with English lower-third captions |
| `demo-preview.gif` | 8–12 s crop of command → arrange → ticker switch, ≤8 MB |
| Manifest | commit SHA, run id, environment, event times, plan provider/usedModel, window PIDs, tickers, trace id, policy outcome, output hashes |

## Scene table

| Time | On-screen truth | Caption | Evidence / test |
|---|---|---|---|
| 00:00–00:08 | Clean desktop; compact AetherWorkspace command surface; tested instruction entered | “One command. A coordinated workspace.” | AW-051 compact launcher; manual review of clean capture region |
| 00:08–00:22 | Validated plan state; broker launches three independent processes; Watchlist/Risk/Chart tile below | “A governed plan launches three independent Windows apps.” | AW-050/AW-054; Windows GUI E2E `GuiWorkflowRunsOverWindowsNamedPipes`, packaged self-test; real PIDs in Inspector |
| 00:22–00:40 | Operator clicks VEGA then ORIN; Watchlist label, Risk metrics and Chart plotted series visibly change in the other processes | “Select once. Synchronize everywhere.” | AW-048/AW-049/AW-050; planned click-driven Windows GUI test; no fabricated state |
| 00:40–00:54 | Inspector: real PIDs, “gRPC / Windows Named Pipes”, publish/delivery sequence, shared trace id | “Typed context over authenticated local IPC.” | AW-052 Inspector/redaction tests; WT-04-01/02 |
| 00:54–01:08 | Deliberate unauthorized plan/policy scenario denied before side effects; exact reason, no cloud egress for restricted case | “AI proposes. Policy decides. Execution is enforced.” | Existing unsafe-plan corpus + E2E denial tests; Broker audit; no cloud attempts |
| 01:08–01:22 | Return to three-window workspace; reveal actual plan provider/`usedModel` and audited run status | “Observable execution. Explicit provider provenance.” | Deterministic path must show `deterministic-grammar`/`usedModel=false`; AW-053 only for Track B |
| 01:22–01:30 | Final composed shot + project URL; no fake green test card | “AetherWorkspace — Open-source desktop interoperability.” | Human review of final media; no unverified claims |

## Claim guardrails

- Track A wording only unless AW-053 provides a real validated LLM run; never caption a
  deterministic plan as “AI-generated”.
- No sub-15 ms synchronization or 60 FPS promises; latency claims only from AW-066 measurements.
- FDC3 = “FDC3-aligned supported subset”; no conformance claim.
- No real customer data, credentials, personal notifications or full-desktop capture.
- Every scene must map to a test/evidence row in
  [`acceptance-matrix.md`](acceptance-matrix.md).
