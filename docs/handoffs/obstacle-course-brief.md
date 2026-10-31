# Brief: the Gauntlet, an obstacle course across the lake (exploratory; after 0.1.9)

Andrew, 2026-10-31: "be creative ... go a little wild and do some weird things". Height counts; a
carnival funhouse with three or four layers side by side; a side-scrolling course; silly camera
tricks. The point is to find out **whether there's juice in an obstacle course**, cheaply. So build
a small playable slice, let Andrew play it, and only go further if it's fun. Also a first try at
**spell-restricted zones** (only certain spells work in an area), for a more curated experience.

## The idea: a carnival gauntlet on the far shore

A gaudy funhouse on the far side of the lake: striped awnings, a ticket booth, a bell at the end.
Step through the gate and **the camera swings to a locked side-on view**: a 2.5D side-scroller.
The course runs left to right in **three or four depth lanes** (front, middle, back, and the
rafters), like a carnival shooting gallery's layered cutouts. You hop between lanes at marked
spots. Height matters: the rafters are the fast route if you can get up there.

Each booth is a little spell puzzle, and **each booth allows only certain elements** (the
spell-restricted zones). The HUD shows the allowed elements on the booth's sign; anything else
fizzles with a comic puff.

## Booths: a menu to pick from, not a list to finish

Build 3 or 4 for the slice, picking the most promising. The rest are ideas for later.

1. **The lava pit (Water and Air only).** A pit of lava; Frost on it crusts it into rock you can
   walk on, but the crust re-melts after a few seconds, so you hop across while freezing ahead.
   (Lava plus cold makes rock in the rules already.)
2. **The Misty gaps (Misty Step only).** Gaps too wide to jump: teleport across, and teleport up
   into the next lane.
3. **The ice wall (Fire only).** An ice wall blocks the lane; melt it with fire. The meltwater
   floods the floor below, which re-freezes into a slide down to the next booth. A chain, on
   purpose.
4. **The geyser launch (Water and Fire).** Pools in the floor: a Geyser under you throws you up
   to the rafters.
5. **The boulder alley (Earth).** Boulders roll at you down a slope: jump them, or throw up a
   Wall of Stone to stop them, or roll your own back.
6. **The updraft (Air).** Gust fans and a Whirlwind elevator carry you up between lanes.
7. **The vine climb (Earth then Water: Plant Growth, charged).** A sheer wall: grow vines and climb.
8. **The dark tunnel (Light only).** Pitch black; cast Light to see the path.
9. **The barrel roll.** A rotating drum you run through, carnival style: the camera rolls with it.
10. **The hall of mirrors.** The camera flips upside down, or mirror-images left and right, or a
    funhouse fish-eye shader warps the view. Disorienting, on purpose.
11. **The finish: the strongman bell.** Punch the plate (Circle) to ring the bell. A charged
    Thunderwave rings it louder.

## The rules of the course

- **Checkpoints** at each booth: falling in lava or off the rafters means you're downed (the new
  downed system) and back at the last checkpoint, not the yard.
- **A timer** from the gate to the bell, a best time per player, and a scoreboard at the gate
  (like the ball court's).
- **Spell zones** are data: an area lists its allowed elements or spells; casting anything else
  fizzles. Put this in the rules (both engines, shared goldens), so it works online and is reusable
  anywhere in the valley.
- **Online:** two wizards race side by side, in different lanes. Later: a ghost of your best run.

## The camera (the weird part)
- On entering: a smooth swing into a locked side-on view along the course's rail, which follows
  the player left and right. Lane hops slide the camera's depth a little.
- Per-booth camera tricks are part of the fun: roll in the barrel, flip in the mirrors, pull back
  wide for the boulder alley, push in tight for the dark tunnel.
- On leaving (or with Back): it swings back to the normal third-person camera.

## Split
- **Worker (rules, both engines):** spell zones, checkpoints and respawn there, the timer and best
  times, the course's place in the world data (on the lake's far shore, clear of the island and
  the golf course; pick it from the world files), and the booths' rules (crust that re-melts, the
  ice wall's meltwater, the updraft).
- **Host (GPU):** the side-on camera and its tricks, the carnival dressing (awnings, cutout lanes,
  the bell), the booths' looks, and one recorded run for the reel.

## Deliver
A playable slice: the gate, 3-4 booths, the bell, the timer, and the side camera. A one-minute
clip of a run. A short note: which booths felt fun, which didn't, and what to try next. Then
Andrew plays it and decides whether to go further.
