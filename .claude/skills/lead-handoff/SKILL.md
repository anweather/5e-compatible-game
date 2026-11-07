---
name: lead-handoff
description: Save where coordination stands into docs/STATE.md before Andrew clears the lead session (DesktopUnity), so the next lead starts from git with nothing lost. Use when Andrew says "hand off", "save state", "before I clear", "wrap up", "lead-handoff", or after a release or a big batch of landings. The next session starts with the unity-lead skill.
---

# Lead handoff

The lead's conversation is about to be cleared. Everything the next lead needs must be in git.
It reads only `docs/STATE.md` on start, so that's the file to fix. No separate handoff file.

## 1. Gather (cheaply)

Give a `reader` subagent this job and read only its answer:

> In C:\Users\Andrew\Development\5e-compatible-game\.claude\worktrees\coordinator: `git fetch origin`, then list:
> (a) every `origin/*` branch not in `origin/dev`, with its last commit, author and age, and whether it has dev's latest merge;
> (b) `docs/handoffs/briefs/queue/` (unclaimed) and `queue/taken/` with who took each;
> (c) unread mail: `ls ~/.agent-bridge/pm/*.md` (subjects only, with copies counted);
> (d) how many `bridge-watch.sh` processes run (`ps -ef | grep bridge-watch.sh`).
> Ten lines at most.

Add what only this conversation knows: verdicts Andrew gave, what a worker was told and hasn't
answered, and anything you were about to do.

## 2. Write `docs/STATE.md`

Update it in place and keep it to one page:

- **"Now"** at the top: the date, what landed since the last handoff (branch and commit), what's
  waiting and on whom (compile, Andrew, a worker), and anything held on purpose with its reason.
- **"Who runs where":** what each worker is on right now.
- **"Next, in order":** the next three things, the first one ready to start.
- **"Open questions for Andrew":** each with its options. Answered ones go to `JUDGMENT_CALLS.md`.
- Older detail that no longer drives a decision moves to `docs/coordination-log/<yyyy-mm>.md`.

Facts only, each with its commit or file. Write "unknown" rather than guessing; the last lead
wrongly believed `hold-ui` never started because nobody had checked dev.

## 3. Commit and push

On the `coordination` branch in that worktree:
`git add docs/STATE.md docs/coordination-log JUDGMENT_CALLS.md`, commit as
`docs: STATE for <date> (lead handoff)` with the attribution line, `git fetch origin && git merge
--ff-only origin/dev` (if it won't fast-forward, merge and check the stat), then `git push origin HEAD:dev`.

## 4. Tell Andrew

One short message: the commit, the top of "Next, in order", and anything he owes. Then:

> Clear this session, then say: "Use the unity-lead skill."

Stop your mailbox listener (TaskStop) before he clears, so the next lead's listener is the only one.
