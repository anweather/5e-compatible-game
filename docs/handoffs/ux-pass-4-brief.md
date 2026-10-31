# Brief: host UX pass 4 (on the PC, with the Unity Editor and the GPU)

From Andrew's playtest of 2026-10-30 (GitHub #2, #6-#21, `docs/PLAYTESTS.md`)
and today's merges, none of which has been played yet. Pull `dev` (646ef07 or
later). Read COORDINATION.md (GPU-first, lean) first.

**Run each part as its own fresh sub-agent**, not all in one long session:
today's cost reading found that long host passes cost the most, because one
session re-reads its growing context. Merge to dev after each part when its
probes pass, so a stop loses nothing. Post issue notes through the PM (send
the text; the PM posts it). Close the Editor at the end. If you're short on
time, take the parts in order.

## Part 1: the HUD and casting (#8, #9, #10, #2)
- #8: take the centre stamina bar out (or move it out of the way; say which),
  make the top bar and the log text smaller, and centre the element icon.
- #9: the cast bar fills up smoothly, not a chunky countdown. It also shows
  charged casts' steps and the held spell's name from #7 (79f17f9); polish it.
- #10: Back closes the grimoire, straight back to play.
- #2, the targeting UI: replace the solid cream squares with a marker per
  kind of cast, taken from the spell's own shape (`SpellCells`, `shape`):
  - a direction (Wave, Boulder, Lightning, Gust): an arrow or lane at any angle;
  - a top-down burst (Fireball, Falling Star, Thunderwave): a ring that grows
    with the charge;
  - rising from below (Geyser, Raise Earth, Wall of Stone): an outline;
  - on a target (Shapeshift): a lock-on.

## Part 2: the wizard, fire and the camera (#16, #17, #19, #20)
- #16: a bloom flare on the head or neck with the camera about 45° down. It's
  probably another NaN or an over-bright pixel, as with the staff (fa76118);
  check WizardFace and the hat.
- #17: fit the hat and the cowl to the new head, with no clipping.
- #19: vary the fire: offset within the cell, size, timing and shape, so a
  burning field doesn't look stamped out. GPU-first.
- #20: the camera zooms right in when you walk up to a house. Find what it
  collides with (walls, window panes) and fix it.

## Part 3: looks and feel for today's rules (play each one)
- The wave and the boulder at an angle (#11, #21), including the wave's look
  (Andrew: "not quite right").
- Snow falling and settling, and fog weather (cost: fog gas on every active
  cell; measure it).
- The golf course: tee, flag, hole and a stroke line on the HUD (24).
- The coil-and-gate puzzle by the golf hole (rebuild the valley; make sure
  no tree stands on the pond).
- Feel: ice and Frost's glaze (#12), snow slowing you, the punch on F and
  Square (#18; a pane twice, a crate, a ball), ending Shapeshift (#6, also
  online), charged casts on keys and pad (#7: Plant Growth charged on burnt
  ground, Wall of Stone held to a ramp).
- Swimming: the `swim` probe, Frost over a swimmer, and Left Ctrl not
  rolling while climbing out.
- A player build, with one live bot run (seed 821) to check the bot's
  element casts.

## Deliver
Per part: before and after shots in unity/Builds/screens/<item>/, probes
passing, no new warnings, merged. Report to the PM after each part in 8
lines or fewer, and at the end with what's left. Then close the Editor.
