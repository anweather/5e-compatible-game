# D21. The repo follows Andrew's kit, with exceptions (2026-11-24)

**Status:** Accepted (Andrew and the lead, 2026-11-24)

- **Context:** Andrew's kit (andrew-claude-kit, a Claude Code plugin marketplace) packages the skills built here
  and on the sailing game, made project-neutral, with a project shape its `bootstrap-project` lays into a new
  repository: kit settings, a lint pack, analysers, issue forms, the in-product report, routines and a site. This
  repo came first and already has most of that under its own names, so the shape is adopted by hand, not by the
  bootstrap (it was not run here).
- **Decision:** the repo follows the kit, and these are its exceptions:
  - **Adopted:** `docs/KIT_SETTINGS.md` (every kit skill's project settings; the root CLAUDE.md points at it); the
    lint pack in `tools/lint/` with its config; the issue forms in `.github/ISSUE_TEMPLATE/` and
    `CONTRIBUTING.md`; `docs/observability.md` and `docs/report.schema.json`, as a placeholder with no code.
  - **The state file is `docs/STATE.md`**, not `docs/HANDOFF.md`.
  - **The backlog's rules are `docs/BACKLOG.md`** (GitHub issues, board #3), not `docs/backlog/README.md`.
  - **Hooks stay in `.git/hooks`; no `core.hooksPath`, no `.githooks/`.** Setting `core.hooksPath` would silently
    switch off the Git LFS hooks and the cost hook there. So the lint has no pre-commit hook (`install.mjs` is not
    run); it runs by hand, at landing if added to the gates later, and in CI's quality workflow, where it prints
    and never fails the job.
  - **Cost trailers are `Worker-*`** (`COMMIT_COST_ROLE=Worker`), written by the repo's own
    `tools/workers/commit-cost.mjs` through `.git/hooks/prepare-commit-msg`, not the kit's
    `tools/telemetry/commit-cost.mjs` and its `Agent-*` role.
  - **The comments rule is a warning everywhere**, new and touched lines too (`"level": "warning"`, a local
    change to the pack), until the one-change purge of the old comments (issue #226); after it, the rule goes to
    errors on new lines as the kit has it.
  - **No analyser changes:** no root `Directory.Build.props`, no `unity/Assets/csc.rsp`, no Nullable.
    `core/Directory.Build.props` stays as it is and nullable stays off.
  - **No weekly-report, monthly-tidy or site workflows yet**, and no `HOW-WE-WORK.md`.
  - **The root CLAUDE.md's size is advisory only:** the kit's steering asks for a short root file; ours stays as
    long as it needs, and is trimmed when it's touched.
  - The lint's `ownData` and `modules` rules are off (no module map yet).
  - The repo's own `.claude/skills/` and `.claude/agents/` stay until Andrew installs the kit's plugins; their
    project facts are copied into `docs/KIT_SETTINGS.md` so they can retire then.
- **Why:** one set of skills across Andrew's projects, reading one settings file, instead of copies that drift.
  The exceptions keep what already works here (the hooks, the state file, the backlog, the cost lines, the C#
  build settings) rather than rename or rebuild it for the kit's sake. A failing comments rule today would block
  every change to the ~18,000 old comments' files; the purge takes them out at once.
- **Reopen if:** #226 lands (the comments rule goes to errors); the LFS and cost hooks move to a `.githooks/`
  that keeps them; the kit's plugins are installed and the repo's own skills retire; Andrew wants the routines or
  the site.
- **Update (2026-11-25, #226):** the purge landed. `tools/purge-comments.mjs` took the old comments out (18,473
  lint warnings, 18,396 once URP's copy was left out, to 0) from 1,014 files, each keeping one top comment; the comments rule is now `"level": "error"`, so a comment
  on a new or changed line fails the lint. URP's TerrainLit copy (`TerrainRock.shader`, `TerrainRockPasses.hlsl`)
  is excluded as third-party, and the five empty catches a comment used to fill are wrapped in
  `#pragma warning disable S108` (Sonar's empty block).
- **Sources:** the kit's `README.md`, `templates/KIT_SETTINGS.md`, `examples/spell-chemistry-valley-settings.md`,
  `plugins/project-shape/lint-pack/`, at kit commit dea4701.
