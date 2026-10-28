# F-0005: Sounds playing but nothing heard for 20 s at the start

- **Category:** Sound
- **Severity:** Low (low confidence)
- **Build:** alpha 0.1.5 (4a9af50), the shipped release; runs rc015-1 to rc015-6, seeds 101-106 (2026-10-27)
- **Reproduced:** Seen once; not replayed.

**Steps:** Stand in the woods near the start for 20 s.

**What happens:** The audio detector heard nothing for 20 s while sources were playing (RC run 2, frame 1492), with the wizard idle among trees. Possibly just a quiet spot in the ambience; possibly a source whose clip or volume never reaches the listener.

**Replay:** `playtest run --replay playtest/runs/rc015-2/run.json --until-frame 1500 --player .claude/worktrees/rc-0.1.5/player`

![before](before.jpg)
