# GitHub publication record

## Repository

| Item | Value |
|---|---|
| URL | <https://github.com/SergeyGer/AetherWorkspace> |
| Visibility | Public |
| Default branch | `main` |
| Publication commits | `81d22c6` (credential ignore + initial published tree) → `38f0338` (v0.2 reliability hardening) → `0b66e12` (Windows CI test portability) → `674e2c0` (Windows named-pipe client fix; first fully green CI) → `ca747d4` (CI evidence docs) → latest docs commit on `main` |
| CI status | Green on **both** jobs for run [43641011037](https://github.com/SergeyGer/AetherWorkspace/actions/runs/43641011037) (commit `674e2c0`): Linux build + 333 tests + headless demo + IPC benchmark smoke; Windows build incl. WPF + full tests + named-pipe demo. GitHub default CodeQL setup is green. Two earlier failed runs are retained in Actions history (Windows test portability, then named-pipe client bug) |
| Description | Local-only, policy-aware desktop interoperability reference implementation (.NET 10, gRPC IPC, deterministic ActionPlan execution, FDC3-aligned context subset) |
| Topics | aetherworkspace, dotnet, wpf, grpc, named-pipes, desktop, interoperability, fdc3, ai-safety, policy-engine, reference-implementation |

## Published backlog and documentation

| Item | Count | Notes |
|---|---|---|
| Milestones | 8 | `M0`, `v0.1.0` … `v0.6.0`, `v1.0.0` |
| Labels | 31 | Project planning labels plus GitHub defaults |
| Issues | 38 | AW-001…AW-038 in order; issue #1 = AW-001 … #38 = AW-038 |
| Wiki pages | 7 | Home, Roadmap (with issue links), Architecture, Development-Guide, Release-Criteria, Decision-Log, `_Sidebar`; pushed to the wiki git repository |
| Repository documentation | committed tree | README (with CI badge), quickstart, architecture, ADRs, threat model, contracts/schemas, benchmarks, limitations, implementation status, evidence records, planning baseline |
| CI workflows | 3 | `CI` (Linux + Windows), `Benchmarks` (manual dispatch), GitHub default CodeQL setup auto-enabled |

## Credential handling (`.git-token`)

- The token file is local-only and listed in `.gitignore` (`.git-token`, `*.token`) and
  `.git/info/exclude`; it was never staged or committed.
- Verified: no occurrence of the token value in any commit of the local history
  (`git grep` over all revisions returned clean).
- Verified: `GET /repos/SergeyGer/AetherWorkspace/contents/.git-token` returns HTTP 404
  (absent from the remote).
- Push authentication used a one-shot `GIT_ASKPASS` helper reading the token from an
  environment variable; the token is not stored in git remotes or configuration.
- Any exposure of the token should be treated as compromise: rotate it at GitHub and
  update the local `.git-token` file. The published tree carries no secrets.

## Attachment handling (read-only source archives)

The supplied planning kit archive was extracted into
`docs/planning/github-planning-kit/` and validated against the source zip:

| Check | Result |
|---|---|
| Files in `AetherWorkspace_GitHub_Planning_Kit_v3.0.zip` | 49 |
| Files present in workspace copy | 49 / 49 |
| SHA-256 mismatches | 0 |
| Breakdown | 38 issue files, `issues_manifest.json`, 6 wiki pages, 2 issue templates, `README_IMPORT.md`, `create_issues.py` |

The two docx design documents were converted to Markdown under `docs/planning/` with their
source SHA-256 values recorded in the baseline evidence record. Attachment originals
remain in the read-only attachment store and are not committed.

## Milestone releases and repository governance (2026-11-15)

| Item | State |
|---|---|
| Releases | `v0.1.0` (M0 + MVP vertical slice), `v0.2.0` (reliability hardening) published with release notes and source archives + SHA-256 checksums; `v0.3.0` published by the `Release` workflow on tag push with framework-dependent binaries, source archive, SPDX SBOM and `SHA256SUMS-v0.3.0.txt` |
| Release automation | `.github/workflows/release.yml` (tags `v*`, manual dispatch): build + full test suite, package broker/shell/samples, SPDX SBOM via `Microsoft.Sbom.DotNetTool`, checksums, GitHub release assets |
| Branch protection | Classic protection on `main` (via API): required status checks are `Linux build, tests and headless demo`, `Windows build, tests and named-pipe demo`, `Windows WPF UI automation (FlaUI)`; force pushes and deletions disabled; admins not enforced (direct maintenance pushes remain possible) |
| Issue dependencies | Planning-kit `AW-0xx` references on `**Dependencies:**` lines replaced with live `#N` issue links (37 issues had dependencies; one had none) |
| Assignees | All 38 issues assigned to `SergeyGer` |
| Kanban project | **Not created** — the credential lacks the GitHub `project` scope (`createProjectV2` resource not accessible by personal access token). Requires a token with Projects read/write permission (or the project created manually in the UI); all issue/milestone/label data is ready to add |
| Wiki | Synced from the planning kit and updated for implemented milestones and CI status |

### CI evidence for the v0.3.0 milestone

- `CI` workflow: Linux job (build, 354 tests, headless demo, isolated-network-namespace
  blocked-egress check, IPC benchmark smoke, publish + SPDX SBOM) and Windows job (build
  incl. WPF, full test suite, named-pipe demo, named-pipe IPC benchmark, Firewall
  blocked-egress check) plus a dedicated Windows WPF UI automation job (FlaUI).
- UI automation found and fixed a real WPF defect: the shell never opened `MainWindow`
  (missing `StartupUri`); both UI tests pass in Windows CI.
- Any red runs are retained in Actions history as evidence of the failures and the
  corresponding fixes.
