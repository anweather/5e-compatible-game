Brief from the PM: the PC's next three jobs, in order, on Andrew's Max account (the new PC worker; DesktopUnityPro has stopped). One Unity job at a time; the Editor is in `.claude/worktrees/host-ux`. Report each job to the PM's mailbox (`BRIDGE_FROM=<your name> bash tools/workers/bridge.sh send pm "<subject>" < report`) as it finishes: a few lines, the commit, what you saw.

Before anything: `git fetch origin`, bring `host-ux` up to `origin/dev` (castle-seat and hold-progress are landed), refresh the Editor and wait for the compile (no probes during a recompile).

1. **A fresh `Builds/PcDev` from dev**, with no test code: Andrew plays from it tomorrow (`docs/playtests/2026-11-07-human-plan.md`). Report its commit.
2. **Proximity voice with canned audio:** `docs/handoffs/briefs/vivox-canned.md`. It fills in V1 to V3 in tomorrow's plan.
3. **The Hold phase 5 on screen** (branch `hold-ui` off dev; design `docs/design/the-hold.md`, the Mac's report at the end of `docs/handoffs/pm-2026-11-05.md`). Phase 5 runs in core with no HUD yet. Build:
   - the offer between waves, with pad picking and time left;
   - upgrades named in words, and picks during a run;
   - unlocks: what you have, what's next, points away;
   - a run's end summary;
   - a locked gesture or hand saying so when pressed (today nothing happens);
   - the attuning room showing only what's unlocked;
   - Tough changing health visibly;
   - F7 fresh run, with quit, reload and reset keeping points.
   A Linux VM (u2) writes the core lockstep test for two peers with different points; you do the real two-peer run on screen, and check for no desync. Gates, EditMode on your tip, and the probes you touched before reporting. Frugal: check your own shots at 480 px, no clips.

Not yours: tuning Cinders and Roots (waits on Andrew's verdicts); #164; the sky-camera bug, unless Andrew sees it again tomorrow.
