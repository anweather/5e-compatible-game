Brief for the Mac: the Hold, phase 3, hands, the part that needs no pad or GPU. Read `docs/design/the-hold.md` (phase 3). Branch `hold-hands` off dev (4687e9e3 or later). The PC does the feel on a real pad and the look of each hand after you.

**Goal:** a wizard in the Hold has an element in each hand and casts by the phase 3 table. The sandbox's controls and spells don't change.

**Build:**
- A rules table of hand spells by element and modifier, the 28 in the design, all existing spells. Nothing in code names a spell.
- A hand's element as core state per wizard, changed only by a command, and only in the keep's attuning room. A row or a place says where that is, so nothing holds a coordinate. A simple way to choose there is enough for now.
- Right trigger casts the right hand, left trigger the left, a bumper modifies that hand, both triggers combine. Mouse: left and right click, Shift modifies. On only during a Hold run or in its loadout; the sandbox's element pad stays.
- The D-pad kit from a rules row. Start with the existing `dpad` spells unless they plainly don't fit, and say what you chose.
- A cooldown per hand, with a two-hand spell locking both. Numbers in the rules. Say if the cast window delays a one-trigger cast and by how many ticks.

**Keep:** the same rules as `the-hold.md`: core state, commands only, seeded RNG, no golden moved, words in the glossary first. `combos.json`'s behaviour in the sandbox stays. The PC is on `castle-road` in `buildings.json`, so don't edit the valley's rows without saying.

**Tests:** core tests for the table, attuning only in the room, cooldowns, and two worlds matching; a probe casting every hand input.

**Report:** one short message: the commit, gates with exit codes, and a numbered list for the PC: the feel on a pad and what each hand must show. Don't merge it. Ask before anything much larger than this brief.
