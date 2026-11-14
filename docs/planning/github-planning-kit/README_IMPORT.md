# AetherWorkspace — GitHub Planning Kit

This kit is an **offline authoring package** for an independent open-source desktop interoperability project. It does **not** create or publish GitHub Issues, milestones or Wiki pages by itself.

## Contents
- `wiki/` — ready-to-publish Markdown pages for the GitHub Wiki.
- `issues/` — 38 issues with stable `AW-001` … `AW-038` planning keys, acceptance criteria and dependencies.
- `issues_manifest.json` — source of truth for issue titles, paths, labels, milestones and dependencies.
- `.github/ISSUE_TEMPLATE/` — templates for new feature and defect submissions.
- `scripts/create_issues.py` — optional GitHub CLI importer; **dry-run is the default**; use `--apply` only after review.

## Suggested setup
1. Create the repository and enable Issues and Wiki in its settings.
2. Create milestones `M0`, `v0.1.0`, `v0.2.0`, `v0.3.0`, `v0.4.0`, `v0.5.0`, `v0.6.0`, `v1.0.0`.
3. Create labels listed in `issues_manifest.json` or ask the importer to create them when using `--apply`.
4. Review all Markdown issue bodies. Use `python scripts/create_issues.py --repo OWNER/REPO` to see the planned actions.
5. With `gh auth login` completed and explicit approval, use `python scripts/create_issues.py --repo OWNER/REPO --apply` to create milestones, labels and issues. Requires repository permission.
6. Publish Markdown Wiki files using the GitHub Wiki UI, or clone the Wiki Git repository (if enabled) and copy the pages.
7. Replace stable `AW-...` prerequisites with actual GitHub dependency links after import.

## Notes
- The importer intentionally does not modify the Wiki or create a GitHub Projects board.
- Milestone names denote capability gates, not calendar deadlines.
- The `issues/` directory is planning source and need not be published to the source repository as open issues if you use the importer.
- Do not treat aspirational performance, interoperability or privacy criteria as observed results.
