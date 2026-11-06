# Human playtest plan, 2026-11-05

Build: **alpha 0.1.11** (the newest release; the fixes below are all in it, checked with `git merge-base`).
About 40 minutes. Play solo unless a step says two players. For each step reply with `H1 PASS`, `H1 FAIL: what you saw`,
or `H1 SKIP`. I close the issue on PASS with a comment, and leave it open (with your note on it) on FAIL.
Nothing here needs a dev build or the Editor.

## Fixes to confirm (each closes its issue on PASS)

| Id | Issue | Do this | PASS if |
|---|---|---|---|
| H1 | #14 health at 0 | Let your health run to 0 (a Fireball at your feet, or stand in lava). Then at 1 HP, wait. Carry an item first | You fall, drop the item, and come back at the yard after about 3 s. 1 HP no longer sits there with nothing happening |
| H2 | #14 online | Same with a second player joined (skip if nobody is on) | Both peers see the fall and the return, and the checksums stay green |
| H3 | #23 surf | Stand in the surf near the island, then on the mainland shore | Near the island it pushes you toward the island, on the mainland toward the mainland, and the wave animation runs the same way |
| H4 | #5 walking hitch | Walk the forest at night for 3 minutes, across the valley between places | No stutter you can feel (a frozen frame of a tenth of a second or more) |
| H5 | #4 robe | Cast a few spells, walk to the ball court and on to the waterfall, turn the camera away and back | The robe never stretches into a long flat tail, and the legs are never bare |
| H6 | #3 waterfall pool | Walk into the waterfall's pool and look toward the falls | No flat pale band across the top of the view |

## Quick look at ones already closed (reopen if they fail)

| Id | Issue | Do this | PASS if |
|---|---|---|---|
| R1 | #21 boulder | Roll a Rolling Boulder at an angle (north-east) | It goes where you aimed, not along a compass direction |
| R2 | #12 ice | Run onto ice or Frost's glaze | You keep your speed, slide, and are slow to stop and turn |
| R3 | #6 Shapeshift | Shapeshift, then press any cast button | You change back at once |
| R4 | #15 water and fire | Water, then fire on a pool | The water boils; no brick texture on it |
| R5 | #7 charge | Hold Earth, Earth to charge; hold Plant Growth on burnt ground | A held Wall of Stone makes the ramp; Plant Growth greens the ground |

## Judgment calls (I can't tell by reading code)

| Id | Issue | Do this | Tell me |
|---|---|---|---|
| J1 | #8 HUD | Look at the HUD in play | Is the stamina bar out of the middle, the top bar small enough, the log text small enough, the element icon centred? PASS or what's still off |
| J2 | #2 aim markers | Aim a few spells: a burst, a direction, a column | Do the markers read as what the spell will do, or as broken ground? |
| J3 | #22 casting weight | Cast a lot of spells | Does casting feel weighty now (no staff, hands, slam, recoil), or still floaty? |
| J4 | #18 punch | Press the punch button at a window, then at a crate | The pane breaks and the crate is knocked. Tell me which button worked |
| J5 | #48 far casts | Cast at the far hills, 100 m or more | A far landing makes no sound. Is that fine, or do you want it heard? |
| J6 | #54 row houses | Burn or break a row house wall | Does the wall still stand in the picture after it burns? |
| J7 | #44 ice launch | Misty Step onto ice, glaze or sleet | You aren't flung. PASS if you land normally |
| J8 | #46 controller | Only if you have a Steam Deck or a Linux machine with a pad | What failed (no input, wrong buttons, glyphs, sticks) |

## Design questions for you (no play needed)

- #92: should plain lava (from Molten Boulder) hurt things, as the Lava Pool does?
- #90: should deep ice, made with Create Water then Ray of Frost, thaw by itself, or only by heat and fire?
- #91: should Frost lying on lava go when the pool sets sooner, or is 50 s fine?
