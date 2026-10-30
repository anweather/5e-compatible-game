# F-0006: Cracked ground shows as flat cream squares, not a web

- **Category:** Visual
- **Severity:** Medium (shows wherever a spell cracks paving or ice, and stays)
- **Build:** alpha 0.1.8 (4378764), the release candidate; runs rc018-811 to rc018-813, seeds 811-813 (2026-10-30)
- **Reproduced:** Seen in all 3 runs. Not replayed: the shots are clear.
- **GitHub:** (issue number added when filed)

**Steps:** In the village, stand by the well and cast Thunderwave at the paving
(cell 219,299). Or at the defense field at night, cast Fog Cloud, Spike Growth
and Wall of Stone, then Create Water and Ray of Frost on the same cells.

**What happens:** Solid cream squares, one per cell, lie on the ground in a
loose grid, every other cell or so. They aren't there before the cast (run 812,
frame 1489) and are there 4 s after (frame 1610). On the frozen ground at the
defense field (run 813, frames 9361-11294) the same squares cover the ice. A few
have a web cut out of them. The rest are plain. They also lie on the
waterfall's pool (run 811, frame 6578). The release wants a spiderweb on cracked
ground and ice (CellCrackMesh, CrackWeb.hlsl), and a cracked cell shows only
its quad. Probably the web's pattern or blend isn't reaching the player build
(the decal shares the `Prototype/WindowGlass` shader with window panes). That
is unverified: check it in the Editor against the player.

**Replay:** `playtest run --replay playtest/runs/rc018-812/run.json --until-frame 1620 --player %LOCALAPPDATA%/SpellChemistryValley/current`
(the run folders stay on the PC, out of git)

![before](before.jpg)
![ice](ice.jpg)
