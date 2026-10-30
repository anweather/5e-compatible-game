# F-0002: Walking hitches of 100-135 ms, mostly crossing chunk borders

- **Category:** Performance
- **Severity:** Low (intermittent)
- **Build:** alpha 0.1.5 (4a9af50), the shipped release; runs rc015-1 to rc015-6, seeds 101-106 (2026-10-27)
- **Reproduced:** Not reproduced: the replay of run 3 had none. Filed because it turned up in 3 of 6 runs.
- **GitHub:** [#5](https://github.com/anweather/5e-compatible-game/issues/5)

**Steps:** Walk between places across the valley (for example the tower to the waterfall to the defense field).

**What happens:** One frame of 100-135 ms now and then while walking, one per place: 12 in RC run 3 (chunks 6,7 > 6,6 > 5,6 > 5,5 in a row), one each in runs 5 and 6 (the waterfall), one on dev (104 ms). Most come with no action in the 4 s before, so the likely cause is chunks activating, props spawning or terrain detail changing at a chunk border. Frame times are otherwise steady (p50 33.3 ms, p95 33.6 ms). Some follow a cast (grease then Fire Bolt), the bot's own teleports, or opening a menu.

**Replay:** `playtest run --replay playtest/runs/rc015-3/run.json --until-frame 8800 --player .claude/worktrees/rc-0.1.5/player`

![before](before.jpg)

## Seen again on alpha 0.1.8 (4378764), 2026-10-30

Run rc018-811, frame 11315: one frame of 185 ms while walking through the
forest at night toward the sea (chunk 6,6). Nothing was cast in the 4 s before.
It's the only spike over 100 ms in the three runs, and the longest so far
(0.1.5 had 100-135 ms). Picture: `rc018-811-11315.jpg`.
