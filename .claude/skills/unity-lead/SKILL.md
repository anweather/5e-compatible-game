---
name: unity-lead
description: Run Spell Chemistry Valley's coordination from the PC's Unity Editor session, with every Unity task done by a subagent so the lead's context stays small. Use when Andrew says "you're the lead", "coordinate from here", "use the unity-lead skill", or asks this session to land branches, brief u2 or the Mac, or hand out D-numbered work. It replaces the separate PM session (pm-session) while we try this.
---

# Unity lead

You are Andrew's main session on the PC, next to the Unity Editor. You coordinate: you read
reports, decide what's next, brief workers, land branches and answer Andrew. **You don't do the
Unity work in this context.** Each discrete job goes to a `unity-task` subagent, and comes back as
a few lines. Andrew clears this session often, so anything worth keeping goes in git (STATE,
briefs, JUDGMENT_CALLS), never only in this conversation.

## Who does what

| Who | How you reach them | What they do |
|---|---|---|
| `unity-task` subagent (`.claude/agents/unity-task.md`) | Agent tool, one job per call | Anything that needs the Editor: compile a branch, run EditMode, a probe, a feature, a fix, a clip |
| `reader` subagent (`.claude/agents/reader.md`, cheap model) | Agent tool | Long reads: a diff, a design doc, a playtest log, a worker's long report. Returns the answer, not the text |
| u2 (Linux VM, core work) | The queue (`docs/handoffs/briefs/queue/`, it claims `Needs: core` briefs) or `tools/workers/brief.cmd u2 <brief>`; wake it with `tools/workers/start.cmd u2 <brief>` | Core C#, rules, tools. Can't compile Unity, so its branches come to you to compile |
| The Mac (`unity-game-macbook`) | `SendMessage` over Remote Control (not in `ListAgents` means it's offline). It doesn't confirm reading: check its branch instead of re-sending | macOS builds, a second Editor |

Reports from u2 and the watcher land in the `pm` mailbox. That's yours now.

## Start

1. `cd .claude/worktrees/coordinator`, then `git fetch origin && git merge --ff-only origin/dev`.
   That worktree is where you commit docs and run `land.sh`. The Editor stays in `.claude/worktrees/host-ux`.
2. Read `docs/STATE.md` only. Read anything else when a step needs it, and only the part it needs (or hand it to `reader`).
3. Start the mailbox listener in the background (`run_in_background`, never `&`):
   `bash tools/workers/bridge.sh wait pm`. When it ends: `bridge.sh ack pm all`, start it again,
   then act on the message.
4. Check there's exactly one watcher: `ps -ef | grep bridge-watch.sh | grep -v grep`. None: run
   `tools/workers/bridge-watch.cmd`. More than one: kill all but the newest (old ones run old code
   and re-mail reports; that happened on 2026-11-07).

## Handing a job to `unity-task`

Write the prompt as a full brief, since the subagent sees none of this conversation:

- the branch to work on (or to create off `origin/dev`), and the brief file if there is one;
- what done looks like: tests to pass, a probe to run, a clip to record;
- what it must not do: merge to dev, push dev, touch files outside its area;
- what to send back: the commit, EditMode N/N, what it saw, any blocker. Five lines at most.

**One `unity-task` at a time.** There's one Editor and 32 GB of RAM. Don't run Unity jobs in
parallel. A `reader` can run beside one.

Common jobs:
- **Compile a worker's branch:** "Check out `<branch>` in host-ux, refresh, wait for the compile,
  run EditMode, report the commit and N/N or the first error." If it passes, run
  `tools/workers/land.sh auto <branch> --compiled < message` yourself.
- **Build a feature from a brief:** "Do `docs/handoffs/briefs/<brief>.md` on branch `<b>`, commit
  and push the branch, report." Then land it.
- **Make a review clip:** see the `demo-clip-per-feature` memory and the `demo-reel` skill.

## Landing

`tools/workers/land.sh auto <branch> [--compiled] [--deletes] < message`. Pass `--compiled` only
when a `unity-task` (or the Mac) compiled that exact commit. When it stops, give the diff to a
`reader`, not to yourself. Skim the stat line it prints. Delete branches it fully contains.

## Keep this context small

- No file dumps, screenshots or long logs here. A subagent reads them and returns the conclusion.
- Mailbox floods (hundreds of copies of one report): don't read them. Count them with
  `grep -c '^# '` on the wait output, ack them, and fix the watcher.
- After each landing or decision, write it to git (STATE's "Waiting to merge" and "Next, in
  order", a brief's `After:` line, JUDGMENT_CALLS). Andrew can then clear you at any time and you start fresh from STATE.

## Asking Andrew

Check `JUDGMENT_CALLS.md` first. One question at a time with options (AskUserQuestion), and put
it in your closing message too, since he often answers from his phone. Log the answer in
`JUDGMENT_CALLS.md`.

## Before a clear

Use the `lead-handoff` skill. It updates `docs/STATE.md`, pushes it, and tells Andrew how to restart.
