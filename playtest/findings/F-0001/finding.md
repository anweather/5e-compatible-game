# F-0001: Shocking Grasp into the river stalls a frame (199 ms on 0.1.5, 663 ms on dev)

- **Category:** Performance
- **Severity:** High (the top performance finding for 0.1.6)
- **Build:** alpha 0.1.5 (4a9af50), the shipped release; runs rc015-1 to rc015-6, seeds 101-106 (2026-10-27)
- **Reproduced:** Reproduced on both builds (once each).

**Steps:** From the bridge, cast Create Water into the river, then Shocking Grasp at the same cell.

**What happens:** One frame takes 199 ms on 0.1.5 (RC run 6, frame 6083) and 663 ms on dev cc88909 (seed 103, frame 4745), both 8 frames after the cast. The charge probably spreads through all the connected river water in one tick (`lightning-conduct`, `conduct: 3` in reactions.json). The chemistry is the World lane's.

**Replay:** `playtest run --replay playtest/runs/rc015-6/run.json --until-frame 6100 --player .claude/worktrees/rc-0.1.5/player`

![before](before.jpg)
