# Playtest backlog

What the playtest bot has found, ranked by severity (game-breaking >
gameplay > physics > performance > visual > sound), then how often it
occurs, then how recently it was seen (playtest/DESIGN.md, section 4).
Each finding's folder has its steps, replay command and picture.

| # | Finding | Category | Severity | Seen |
|---|---|---|---|---|
| [F-0001](findings/F-0001/finding.md) | Shocking Grasp into the river stalls a frame (199 / 663 ms) | Performance | High | 0.1.5 run 6, dev seed 103 |
| [F-0002](findings/F-0002/finding.md) | Walking hitches of 100-135 ms at chunk borders | Performance | Low | 0.1.5 runs 3, 5, 6; dev |
| [F-0003](findings/F-0003/finding.md) | A flat white square by the waterfall | Visual | Medium | 0.1.5 run 6 |
| [F-0004](findings/F-0004/finding.md) | The camera ends up inside the wizard against a cliff | Visual | Low | 0.1.5 run 5 |
| [F-0005](findings/F-0005/finding.md) | Sounds playing but nothing heard at the start | Sound | Low | 0.1.5 run 2 |

## 0.1.5 release-candidate runs (R2, 2026-10-27)

Six runs of 8 minutes on the shipped build (4a9af50), seeds 101-106, the PC
alone: 91 889 frames, 186 goals, 47 candidates, **no crash, hang or logged
error, nothing release-blocking**. 31.7 minutes were on the world. Runs 1,
2 and 4 were cut short (1.1, 2.8, 3.5 min) by the bot's sea-teleport bug
below. Compared with dev cc88909 (seed 103) and a replay of run 3.

**Triaged as expected, not filed:** spell-nothing at the village well
square, around cell (220, 300), which is paved: Plant Growth, Ray of Frost
and Thunderwave rightly do nothing there. Two Fire Bolts that hit the river
(fire on water). Gust of Wind over bare ground. Shocking Grasp with no water.
Sleet Storm on the tower's stone.

**A rules question (for a judgment call):** Sleet Storm on bare stone changes
nothing under today's rules. Should sleet leave slush or ice on stone, as the
SRD's difficult terrain suggests?

## The bot's own bugs (World lane, 0.1.6)

These made false candidates. They are fixed in the bot, not the game.

1. **The sea is placed kilometres off the world.** The bot finds the sea by its
   renderers' bounds, whose centre is at (3939, -4832), far outside the
   valley. Its unstick fallback teleported the wizard there and stranded the
   rest of the run (wizard-out, wizard-fell, wizard-airborne:sea, and
   audio-silent "chunk 44,54"). Fix: aim at the coast nearest the valley, and
   treat a bot teleport outside the world as the bot's fault.
2. **Pickups ignore reach.** Stranded off the world, the throw goal "picked up"
   the court's ball 4 km away and threw it (prop-lost, prop-awake-inactive,
   prop-flung at 2653 m/s in run 2). Fix: only reach items within reach.
3. **Dusk counts as unexpectedly dark.** After Hasten Time the clock reaches
   dusk; the image detector expects darkness only from 20:00, so a dusk
   picture was "black" (chunk 3,4, both builds). Fix: read World.Daylight
   (backlog #8) and expect dark pictures whenever it isn't bright.
4. **Spell-nothing on cells nothing can change.** Lower the confidence of
   spell-nothing when the cell has nothing the element acts on (paved
   ground, bare stone, open water for fire).
