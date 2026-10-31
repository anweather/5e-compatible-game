---
name: pm-session
description: Start or hand off a PM (project manager) session for Spell Chemistry Valley. Use at the start of any session where Andrew says "you are the PM", "take over as PM", "start as PM" or points at a pm handoff; and at the end, after a release or when he says "hand off", "write the handoff", "restart the PM". Covers the startup reads, the mailbox listener, checking pending branches, briefing DesktopUnity and the Mac, and writing the next handoff plus STATE.md.
---

# PM session

The PM plans, briefs workers, merges, keeps GitHub issues, cuts releases and reports. It writes no
game code unless Andrew asks. A long PM session became the largest single cost, so: read little,
delegate long reads to sub-agents (a cheaper model for read-only work), keep messages short, and
restart after every release.

## Start

1. Work in `C:\Users\Andrew\Development\5e-compatible-game\.claude\worktrees\coordinator` (branch
   `coordination`, pushes to dev). If the session started elsewhere, move there first.
   `git fetch origin && git merge --ff-only origin/dev`. If it won't fast-forward, stop and say so.
2. Read only `docs/STATE.md` and the newest `docs/handoffs/pm-*.md`. Nothing else up front: open
   other docs when a step needs them, and only the section it needs.
3. Start the mailbox listener as a background task (`run_in_background`), never detached with `&`:
   `bash tools/workers/bridge.sh wait pm`. When it ends with a message: `bridge.sh ack pm all`, start
   the wait again at once, then act on the message. Don't poll; the wait ending wakes you.
4. Check the watcher: `ps -p $(cat ~/.agent-bridge/watch/pid)`. If it's dead, run
   `tools/workers/bridge-watch.cmd`.
5. For each branch in STATE's "Waiting to merge" table: `git fetch -q origin`, then whether it's
   already in dev (`git merge-base --is-ancestor origin/<b> origin/dev`) and whether it has dev's
   latest merge. That tells you who you're waiting on.
6. Introduce yourself with `SendMessage` (find names with `ListAgents`) to `DesktopUnity` and
   `unity-game-macbook`: one short message each, saying you've taken over and what you expect from them
   next. The Mac's route doesn't confirm reading; if it stays quiet, check its branch instead of
   re-sending. Workers may report through the mailbox or reply directly.
7. Before asking Andrew anything, check `JUDGMENT_CALLS.md` and STATE's "Open questions". Ask one
   question at a time, with options; he answers from his phone over Remote Control, so put the
   question in your closing message too, not only in a box.
8. Carry on with the handoff's "Mid-flight" and STATE's "Next, in order".

## While running

- Land a worker's branch: `tools/workers/land.sh merge <branch> < message`, run the core and sandbox
  checks (CLAUDE.md "Commands"), then `land.sh push <branch>` (deletes the merged branch).
- Brief VMs with `tools/workers/start.cmd` / `brief.cmd` and briefs in `docs/handoffs/briefs/`. No
  VMs while a release candidate or perf run holds the PC. No idle compute; Pro before Max.
- Issues: file, comment, label, close on ship, with a `Discovery-Cost:` line; a rediscovery is a
  comment, never a duplicate.
- Keep Andrew's board current (STATE, "Andrew's board"): one small publish after each verdict,
  landing or assignment.
- Anything Andrew reviews goes on the board as images or clips, never as text alone: upload with
  the Artifact tool (`asset: true`, `url` = the board) and reference the returned `/_blob/<id>`.
  Shrink screenshots first (`ffmpeg -i in.png -vf scale=1280:-2 -q:v 4 out.jpg`); pair before and
  after. A review item with no media isn't asked yet: get a worker to record it first.
- Images: follow `screenshot-budget`. Status reports: `project-update`, one screen.

## Hand off (after each release, or when Andrew asks)

1. Update `docs/STATE.md` in place and keep it one page: releases, "Waiting to merge", "Next, in
   order", "Open questions", the latest telemetry line. Move history to
   `docs/coordination-log/<yyyy-mm>.md`.
2. Write `docs/handoffs/pm-<yyyy-mm-dd>.md` (add `-2` for a second one that day) with only what
   STATE doesn't say: what's mid-flight, who you're waiting on and for what, verdicts Andrew still
   owes. Keep it under 40 lines. Its start-up section just says "use the `pm-session` skill".
3. Commit both on the `coordination` branch and push to dev (docs-only, no feature branch).
4. Tell Andrew the handoff is pushed and that he can restart the PM with: "You are the PM. Use the
   pm-session skill."
