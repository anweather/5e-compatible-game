# F-0008: Deep ice never thaws, and stands alone after its water has gone

- **Category:** Design question (the code does what it says; Andrew to say if it should)
- **Severity:** Low
- **Build:** core at f1d79b01, text playtester (Haiku), seed 20261104 (2026-11-04)
- **Reproduced:** Yes, by hand from a fresh world.

**Steps:** `go 202,280`; `cast create-water` there, charge 3; `cast ray-of-frost` at the same cell, dir 0, charge 3; wait 3900 ticks.

**What happens:** 3 frozen cells of 18 units stay frozen for the whole run, while the 124 liquid cells round them soak away. Census: tick 300 `frozen:water=3 liquid:water=124`, tick 3900 `frozen:water=3`.

**Why:** dry weather thaws only thin pools. `World.Weather.Freeze` skips `c.Water > 2`, so deep ice goes only by heat or fire. Fine for a lake; odd for a column of ice left standing on dry grass.

**Replay:** the commands above through `Playtest.Text replay` (playtest/cheap/README.md).
