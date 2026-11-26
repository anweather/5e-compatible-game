# Kit settings

What the andrew-claude-kit skills need to know about Spell Chemistry Valley (D21 says where this repo differs from
the kit). Filled 2026-11-24 from the kit's `templates/KIT_SETTINGS.md` and its Spell Chemistry Valley example. A
skill that finds a setting missing asks once and offers to add it here.

The repo's own `.claude/agents/unity-task.md` and `.claude/skills/unity-lead/` still carry these facts too; they
retire once Andrew installs the kit's plugins, and this file is then the only copy.

## Repository

- Integration branch: `dev`. Feature work on a short-lived branch (or worktree) off `dev`, merged into `dev` with
  no PR and pushed as soon as it's verified; never leave a finished branch unmerged.
- Release branch: `main`, tagged `alpha-X.Y.Z`. `dev` goes into `main` only when Andrew says to ship; releases are
  cut from `main` only. A fix for a released version: a branch off `main`, merged to `main`, then `main` into `dev`.
- Docs-only commits (`.md`, including CLAUDE.md files and ADRs) go straight on `dev`.
- Fast-forward only: `git merge --ff-only origin/dev`, never `git pull` (Git for Windows sets `pull.rebase`).
- Machines: the Windows PC is the main one (32 GB, the Editor); the Mac is a dev machine and a second Editor; u2 is
  a Linux VM for core work. **No macOS builds** (J-049): a release is Windows and Linux.
- Lead's checkout: `.claude/worktrees/coordinator`, branch `coordination`, pushed to `dev`.
- Engine checkout (the PC): `C:\Users\Andrew\Development\5e-compatible-game\.claude\worktrees\host-ux`. Open the
  Editor on it **by full path** (`unity open C:\Users\Andrew\Development\5e-compatible-game\.claude\worktrees\host-ux\unity`):
  `unity open unity` resolves by Hub name to the main checkout instead (2026-11-08).
- Generated, and out of git: the yard scene `ChemistryYard.unity` (Prototype > Build Chemistry Yard; rebuilt
  when missing), `Generated/`, `Builds/`, the Asset Store packs. Scenes and prefabs are made by their builder's
  menu item, never hand-edited. A tracked file changing on a rebuild is a leak to fix.

## Lead and builders

- Lead: the session beside the PC's Editor (the repo's `unity-lead` skill; the kit's `lead-session`). Every Unity
  job goes to a `unity-task` subagent, one at a time (one Editor, 32 GB); long reads go to `reader`.
- State file: `docs/STATE.md` (not `HANDOFF.md`). PM handoffs `docs/handoffs/pm-<date>.md`; history
  `docs/coordination-log/<yyyy-mm>.md`.
- Builder rules: `CLAUDE.md` and the area's CLAUDE.md (`unity/CLAUDE.md`, the package's, `Scripts/`, `sandbox/`,
  `rules/`). Check layers: `docs/TESTING_LAYERS.md`.
- Workers:
  - u2: a Linux VM for core C#, rules and tools; can't compile Unity, so its branches come to the PC to compile.
    Claims `Needs: core` briefs from `docs/handoffs/briefs/queue/`, or `tools/workers/brief.cmd u2 <brief>`; woken
    with `tools/workers/start.cmd u2 <brief>`.
  - The Mac (`unity-game-macbook`): over Remote Control (`SendMessage`; absent from `ListAgents` means offline);
    it doesn't confirm reading, so check its branch rather than re-sending. A second Editor and Linux builds.
- Split before briefing (2026-11-08): the PC costs about 4.7x u2 a feature (`docs/telemetry/2026-11-08.md`), so
  every part that runs without Unity goes to u2 first; an open look starts as a paper prototype (like
  `tools/world-paper/`) or a board of stills.
- Landing: `tools/workers/land.sh auto <branch> [--compiled] [--deletes] < message` from the lead's worktree;
  a worker lands its own branch with `land.sh here [--compiled] [--deletes] < message` (D15). `--compiled` only
  when a `unity-task` or the Mac compiled that exact commit. The gates are `tools/workers/gates.sh`.
- Mailbox: `bash tools/workers/bridge.sh wait pm` (in the background), then `bridge.sh ack pm all`; exactly one
  watcher, `tools/workers/bridge-watch.sh` (started with `tools/workers/bridge-watch.cmd`).
- Machine limits: the PC has 32 GB; one extra Editor at a time, closed when idle. Never kill a Unity Editor.
- Judgment calls: `JUDGMENT_CALLS.md`. Coordination: `COORDINATION.md`.

## The Unity job (unity-task)

- After a switch or merge in the Editor's checkout: `unity command script eval -- --code 'UnityEditor.AssetDatabase.Refresh()'`,
  then wait until `unity command console status --result-only` shows `"compiling": false`. Never probe or test
  during a recompile. Keep the Editor open between switches in one job; close one you opened at the end.
- Tests: `unity command test run --mode EditMode --filter <Name> --result-only` for what changed (the default);
  `tools/editmode.sh` for the Unity-only tests (~440, about 4 min on the Mac) when the change is wide;
  `tools/editmode.sh chem` (the Chemistry tests under Mono, about 40 min on the Mac, 14 on the PC) **only** when
  `Chemistry/` code changed, in the background, reading `Library/TestResults.xml`. Never re-run the Chemistry
  tests in the Editor otherwise: `dotnet test core` covers them. A full EditMode run (~14 min) is longer than the
  CLI's 600 s timeout.
- Probes: only the ones the change touches, plus `tools/probe.sh smoke`. Re-run a failure once alone before
  chasing it. Clips only when the brief asks.
- Work log, one call a phase (start, switched, compiled, tests, probe, clip, pushed, blocked, done):
  `bash tools/workers/note.sh pc-task <phase> "<branch> <commit or N/N, a few words>"`.
- A `unity-task` commits and pushes its branch but never merges into `dev` or pushes `dev`; it reports in five
  lines (branch and commit, compile, EditMode N/N and probes, what it saw, blockers).

## Project shape

- Lint: `node tools/lint/lint.mjs` (config `tools/lint/lint.config.json`, base `origin/dev`); its tests
  `node --test "tools/lint/**/*.test.mjs"`. Comments at error level since the purge (#226, `tools/purge-comments.mjs`): a comment on a new or changed line past a
  file's top comment fails; paths on; ownData
  and modules off. No commit hook (D21).
- The plain core: `packages/com.anweather.chemistry-core/` (namespace `Chem`), built and tested without Unity by
  `core/`: `dotnet build core -c Release -warnaserror` (on the Mac add `-m:1`), `dotnet test core`,
  `dotnet format core --verify-no-changes`. Duplicate code: `tools/jscpd/run.sh unity/Assets/Prototype sandbox/src`.
- C# analysers: as they are (`core/Directory.Build.props`, `unity/Assets/Default.globalconfig`, `.editorconfig`);
  nullable stays off (D21).
- Tools folder: `tools/` (workers in `tools/workers/`, CI helpers in `tools/ci/`).
- Workflows: `quality.yml`, `duplicates.yml`, `sandbox.yml`, `release.yml`. No weekly-report, monthly-tidy or site
  workflows yet (D21).

## Reports

- The in-product report: not built yet; `docs/observability.md` is the placeholder, `docs/report.schema.json` the
  schema.

## Backlog

- GitHub issues and Project board #3 (https://github.com/users/anweather/projects/3), as `docs/BACKLOG.md` says
  (not the kit's backlog README). Epics carry `epic`; issues a kind (`bug`, `enhancement`, `design`, `perf`), an
  `area:` and a priority (`p1`, `p2`, `p3`). Design detail of the old backlog: `docs/backlog-archive.md`.
- Issue forms in `.github/ISSUE_TEMPLATE/` and `CONTRIBUTING.md`, with the repo's own labels (no `idea` or
  `needs-grooming` label: an idea is `enhancement`, a bug `bug`, a question `question`).
- Brief queue: `docs/handoffs/briefs/queue/` (claimed into `queue/taken/` by `tools/workers/queue.mjs`).
- Glossary: `docs/GLOSSARY.md`. Words never to use: the trademarked name of the tabletop game; say
  "5E-compatible".

## Decisions and reports

- ADR index: `docs/DECISIONS.md`, one file a decision in `docs/adr/`.
- Status script: `KIT_EDITOR_CHECKOUT=.claude/worktrees/host-ux`, `KIT_QUEUE_DIR=docs/handoffs/briefs/queue`,
  `KIT_MAILBOX=pm`, `KIT_WATCHER_PATTERN=bridge-watch.sh`, `KIT_CLIPS_DIR=unity/Builds/demo-videos`,
  `KIT_IGNORE_CHANGED='unity/ProjectSettings/Packages/'`.

## Game

- Releasing: `docs/RELEASING.md` (the `release` skill), tags `alpha-X.Y.Z`, Windows and Linux only; published
  only on Andrew's go-ahead.
- In-place updates: `tools/ship-update.sh X.Y.Z [--test] [--windows-only | --linux-only]` (Velopack); app id
  `SpellChemistryValley`, test app id `SpellChemistryValleyTest`.
- Probes: `tools/probe.sh <scenario>`, smoke `tools/probe.sh smoke`, every probe `tools/probe.sh sweep`
  (`docs/FAST_LOOP.md`).
- Scene builder: Prototype > Build Chemistry Yard.
- Rules change through the rules MCP (`docs/RULES_MCP.md`, D13).
- Demo reel: source in `showcase/`, clips in `unity/Builds/demo-videos/`.
- Sound: `docs/SOUNDS.md`; game audio `unity/Assets/Prototype/Audio/CC0/<preset>`.
- Frame budget: 60 fps, 16.7 ms a frame, judged by the slowest frame, in a player build.

## Cost telemetry

- Trailers `Worker-*` (`COMMIT_COST_ROLE=Worker`, the default), from the repo's own `tools/workers/commit-cost.mjs`
  through each clone's `.git/hooks/prepare-commit-msg` (no `core.hooksPath`, and not the kit's copy of the script);
  the coordinator's merges carry `Coordinator-*` (`land.sh merge`). Reports: `tools/telemetry/feature-costs.mjs`,
  `tools/telemetry/issue-costs.mjs`; notes in `docs/telemetry/`.
