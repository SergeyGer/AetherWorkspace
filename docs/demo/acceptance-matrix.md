# Demo acceptance matrix and tests-to-scene mapping

Production is allowed only when every row below has evidence for the same approved SHA. This matrix
is the v1.1 release gate; it is intentionally stricter than a “CI is green” statement.

## Scene-to-evidence mapping

| Scene | Requirement | Automated / manual evidence | Status |
|---|---|---|---|
| Launch + three independent processes | Three distinct Windows PIDs own visible top-level windows; no impostor tabs | Windows GUI E2E (`DesktopGuiE2ETests`), real-WPF-sample tests (`WpfSampleAppPersistenceTests`, `GuiWorkflowWithRealWpfSampleAppsKeepsWindowsOpen`), packaged self-test; physical Windows 11 run | Automated baseline PASS; per-release re-verification required |
| Plan validated + arrangement | Plan schema/authorization/executor evidence; windows tile within real work areas | Portable execution/authorization suites; `layout05`; Windows GUI E2E; physical geometry check | Layout evidence recorded; demo layout polish pending AW-051 |
| Ticker synchronization | VEGA and ORIN selection updates Watchlist label, Risk KPI/selection and actual Chart series within bounded waits; no echo loop | Windows GUI test `WatchlistSelectionSynchronizesRiskAndChartWithoutEchoPublish`; catalog parity tests; audit publish count 1→2 | **Automated (CI PASS, 2026-11-17)**; physical capture pending AW-055 |
| IPC provenance | Real PIDs, transport label, publish/delivery events, shared trace id | AW-052 Inspector assertions + redaction tests | Pending AW-052 |
| Policy denial | Denied before side effects; exact denial code; zero cloud attempts for restricted/unknown | Unsafe-plan corpus, E2E denials, egress instrumentation | Existing; capture in demo pending |
| Provider provenance | `deterministic-grammar`/`usedModel=false` for Track A; real provider/model for Track B only | Shell output/Inspector assertions; AW-053 live PUBLIC smoke (planner=cloud-plan, usedModel=true, 6/6 steps) and fallback tests | Automated Track A + Track B smoke PASS; captured Track B video pending AW-055/AW-056 |
| Clean bundle startup | Fresh ZIP extraction + launcher + workflow + cleanup on Windows 11 | v1.0.2 clean-machine evidence; repeat per release (AW-054) | Recorded for v1.0.2; repeat required |
| Media validity | Codec/duration/dimensions, no black frames, caption readability, PIDs/traces match manifest | `tools/demo/Build-Video.ps1` + `Validate-Video.ps1` + human review | **PASS** on physical laptop 2026-11-18 (Track B, 81.37 s, 0 black, taskbar hidden, planner readable, operator review) |
| Privacy | Synthetic only; no secrets/personal data; raw media local | Privacy checklist + sanitized manifest review | Required every capture |

## Mandatory gates before recording

- [ ] Approved full 40-hex SHA and matching origin/revision; no unapproved revision.
- [ ] Three real Windows processes with visible windows registered through the broker.
- [ ] VEGA and ORIN selectable; Risk and actual Chart series change correctly; no duplicate publish.
- [ ] Deterministic provider/`usedModel` truthful in UI; LLM caption only with AW-053 evidence.
- [ ] Denial scenario blocked before side effects; restricted/unknown cloud attempts = 0.
- [ ] Windows CI green; local reference-desktop GUI suite PASS; physical Windows 11 run PASS.
- [ ] OBS capture region excludes personal windows, notifications and credentials.
- [ ] Final output SHA/approved revision recorded; human review before publication.

## Definition of Done for v1.1

All required v1.1 Issues (AW-047…AW-056, AW-065, AW-066) closed with evidence; one approved SHA
matches code/CI; physical Windows 11 GUI and capture validated; MP4/GIF produced locally and
reviewed; sanitized provenance manifest attached; storyboard captions truthful; Wiki Roadmap and
README updated with working links.

## WT formal exceptions (v1.1)

The automated WT rows and UI measurement pass. Manual DPI 125/150, focus/modal, display-topology
recovery, physical crash/hang and manual Inspector/export privacy are **accepted known limitations
(won't test), not PASS**, recorded in
`docs/testing/evidence/2026-11-18-v1.1-wt-formal-exceptions.md`; #120 is closed with that risk
acceptance. They are not part of the published demo claim.
