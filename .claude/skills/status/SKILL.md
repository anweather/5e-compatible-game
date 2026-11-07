---
name: status
description: A cheap status check of the PC's work in one command. Use when Andrew asks "status?", "how's it going", "where's the subagent", or for any progress check on a running unity-task job, instead of running git, Unity and mailbox commands one by one.
---

# Status

Run `bash .claude/skills/status/status.sh` (from the repo or any worktree) and answer from its
output. It prints about 15 lines: the Editor checkout's branch, changed files and last commits,
whether the branch is pushed, Unity's compile state, the queue, the mailbox, watchers, free
memory and new clips.

- Don't read a subagent's transcript, diffs or logs for a status. If the output can't answer
  something, say so.
- Answer in a few lines: what's running, what's done (commits, pushed or not), what's stuck.
  Compare with the last status in this conversation if there was one (e.g. more changed files
  means it's still writing).
- Flag problems it shows: compile failed, more or fewer than one watcher, unread mail, under
  1.5 GB free.
