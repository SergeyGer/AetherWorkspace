# GitHub publication record

## Repository

| Item | Value |
|---|---|
| URL | <https://github.com/SergeyGer/AetherWorkspace> |
| Visibility | Public |
| Default branch | `main` |
| Initial code publication commit | `81d22c6` (`chore: ignore local .git-token credentials file`), followed by the v0.2 reliability-hardening commit |
| Description | Local-only, policy-aware desktop interoperability reference implementation (.NET 10, gRPC IPC, deterministic ActionPlan execution, FDC3-aligned context subset) |
| Topics | aetherworkspace, dotnet, wpf, grpc, named-pipes, desktop, interoperability, fdc3, ai-safety, policy-engine, reference-implementation |

## Published backlog and documentation

| Item | Count | Notes |
|---|---|---|
| Milestones | 8 | `M0`, `v0.1.0` … `v0.6.0`, `v1.0.0` |
| Labels | 31 | Project planning labels plus GitHub defaults |
| Issues | 38 | AW-001…AW-038 in order; issue #1 = AW-001 … #38 = AW-038 |
| Wiki pages | 7 | Home, Roadmap (with issue links), Architecture, Development-Guide, Release-Criteria, Decision-Log, `_Sidebar`; pushed to the wiki git repository |
| Repository documentation | committed tree | README, quickstart, architecture, ADRs, threat model, contracts/schemas, benchmarks, limitations, implementation status, planning baseline |

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
