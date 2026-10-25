# Repository work rules

- Keep this repository private. Do not upload desktop images, wallpaper, real window identities, input reports, settings, recovery journals, credentials or local profile paths. Keep local diagnostics and trial outputs in the ignored `.local/` and `artifacts/` directories.
- Stage named files only. Do not read, modify, stage, move or delete unrelated untracked files.
- Every new commit's author and committer must use a GitHub `noreply` email. Check repository-local Git identity before committing; never print a personal email while checking it.
- Merging also creates author metadata. After verifying the exact PR head, successful CI and private repository visibility, use `gh pr merge --merge --author-email <GitHub-noreply-email> --match-head-commit <verified-head>`. Do not use a merge interface that cannot set the merge author email. Verify the resulting merge metadata and source tree.
- Run `scripts/check-repository-privacy.ps1` before pushing. CI checks tracked data and the new commit range; its historical baseline is not a claim that older metadata has been erased. Record only redacted findings in repository documents.
- Rewriting published history changes commit IDs and signatures and may leave GitHub PR/cache references. Prepare a local backup and a verified preview, and obtain the user's explicit approval before force-pushing a rewrite. Never label residual historical personal data as fully removed.
