# F-0004: The camera ends up inside the wizard against a cliff

- **Category:** Visual
- **Severity:** Low
- **Build:** alpha 0.1.5 (4a9af50), the shipped release; runs rc015-1 to rc015-6, seeds 101-106 (2026-10-27)
- **Reproduced:** Seen in one run, at two goals.

**Steps:** Walk the wizard up to the cliff by the waterfall so the camera is pushed in behind them.

**What happens:** The follow camera's collision brings it into the wizard's body, filling the picture with the robe (RC run 5, frames 5476 and 5597). Low camera angles looking up at the wizard are common in the other screenshots too.

**Replay:** `playtest run --replay playtest/runs/rc015-5/run.json --until-frame 5600 --player .claude/worktrees/rc-0.1.5/player`

![before](before.jpg)
