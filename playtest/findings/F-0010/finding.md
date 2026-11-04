# F-0010: Thorns stay on a lava pool until they wither

- **Category:** Chemistry
- **Severity:** Low
- **Build:** core at 62fac034, text playtester (Haiku), seed 20261104 (2026-11-04)
- **Reproduced:** Yes, by hand from a fresh world.

**Steps:** `cast spike-growth` at 200,286, charge 1; `cast lava-pool` there, charge 1; wait 100 ticks.

**What happens:** the cell reads `lava-pool, cover thorns, life 200, heat 200`. Nothing burns; census at tick 100 is `cover:thorns=56 t:grass=72 t:lava-pool=9`. The thorns go only by withering. A Fire Bolt on the same thorns lights them.

**What should:** Lava Pool's blurb says it "burns whatever stands on it".

**Why:** probably the cause of #91 (F-0009, frost on lava): the pool heats the cell without laying an element, so the rows that burn or melt a cover don't run. Not confirmed in the code for thorns.

**Replay:** the commands above through `Playtest.Text replay` (playtest/cheap/README.md).
