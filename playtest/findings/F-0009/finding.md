# F-0009: Frost sits on molten lava until the pool sets

- **Category:** Chemistry
- **Severity:** Low
- **Build:** core at f1d79b01, text playtester (Sonnet), seed 20261104 (2026-11-04)
- **Reproduced:** Yes, by hand from a fresh world (the model marked it unreproduced).

**Steps:** `cast sleet-storm` at 205,287, charge 3; `cast lava-pool` at 205,287; read the cell.

**What happens:** `205,287 lava-pool, height 0, cover frost, life 300, heat 300`. The frost stays on the lava for its 300 ticks (50 s) and goes when the pool sets. With water there too, the cell ends as `rock, liquid water 7, cover frost`.

**What should happen:** frost melts in fire and heat (`fire-melt`, `heat-melt`), so a pool of lava should melt it. The `lava-pool` row's `heat` effect heats the cell but lays no `heat` element, so those rows never run. The pool's `grow` also takes over `Cell.Life`, moving the frost's wither from 240 ticks to 300.

**Replay:** the commands above through `Playtest.Text replay`.
