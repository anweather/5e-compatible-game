Brief from the PM: finish `skin-looks-core`, which was put to sleep part way so the PC could run perf. Pro account.

You're a worker in a VM, running headless, with no Unity. Your final message is your full report, and it goes straight back to the PM. Before that final message, stop every background task you started, and don't send anything after it.

Your work is saved in the worktree `~/skin-looks-core` (branch `skin-looks-core`), possibly uncommitted. Look at it before anything else, and don't discard any of it. If it isn't there, check `git worktree list` in ~/5e-compatible-game and say what you find.

1. Read `docs/handoffs/briefs/skin-looks-core.md`: that's the job. Work out what's done and do what's left.
2. `git fetch`, then merge `origin/dev` into the branch. Dev has moved a lot (the lag fix among it); resolve conflicts in favour of what dev now does.
3. Run the checks that brief names, then the gates in CLAUDE.md (core build with -warnaserror, tests, format, jscpd).

Deliver: commit and push `skin-looks-core`, not merged. Final message, 10 lines or fewer: the commit; what `set_look`, `describe` and `list looks` can now do; anything left out and why; the gates; the time.
