# F-0011: The lava Molten Boulder leaves doesn't hurt anything

- **Category:** Design question (Andrew to say if plain lava should burn)
- **Severity:** Low
- **Build:** core at 62fac034, text playtester (Haiku), seed 20261104 (2026-11-04)
- **Reproduced:** Yes, by hand from a fresh world.

**Steps:** `cast molten-boulder` at 204,290, charge 1; `spawn goblin` at 204,290; wait 60 ticks. To compare: `cast lava-pool` at 208,290, `spawn goblin` there, wait 30 ticks.

**What happens:** on Molten Boulder's cell (`lava, height -1`) the goblin is still 10/10 after 60 ticks. On a Lava Pool it takes 1 hot metal damage again and again and is defeated inside 30 ticks.

**What should:** the blurb says the spell "leaves a pool of lava", so a player would expect it to burn as a Lava Pool does.

**Why:** they are two terrains. `lava-pool` has `hot`, so its cell has heat; `lava` has no `hot`, so a creature on it takes nothing.

**Replay:** the commands above through `Playtest.Text replay` (playtest/cheap/README.md).
