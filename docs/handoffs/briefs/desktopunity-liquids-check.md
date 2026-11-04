Brief from the PM: you're the new DesktopUnity, the only session driving Unity while the other accounts are offline. Frugal mode: one job at a time, short messages, no screenshots unless a step asks, no sub-agents.

You're in the `host-ux` worktree on `liquids-check`. The Editor is already running batch mode on this project (pid 18276, port 7800). Follow `unity/CLAUDE.md`. Never kill Unity.

Job: say whether `liquids` compiles and passes on today's dev.

1. `git fetch origin`, then merge `origin/dev` into `liquids-check`. If it conflicts, stop and report.
2. `unity status --until-ready` with `--project-path` set to this worktree's `unity` folder, then `unity command console status --result-only`.
3. `unity command test run --mode EditMode --result-only`.
4. Report through the mailbox, then stop and wait: `bash tools/workers/bridge.sh send pm "liquids check" < report.md`. Give the commit, compile result, test counts and any failure's name and first error line. Fix nothing, and don't start `liquids-view`; the PM sends the next job.

Don't push, and don't touch `dev`.
