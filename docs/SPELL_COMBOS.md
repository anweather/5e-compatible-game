# Element combos (design draft, 2026-10-29)

Andrew's direction, still being explored. The spell radial stays as a debug
and experiment tool. The real input becomes elements combined in sequence.

## Decided so far
- **Four elements on four buttons:** fire, water, earth and air on the
  bumpers and triggers. Face buttons stay jump and interact; sticks move and
  look; the d-pad is to be decided.
- **Sequence, not chord:** tap one element, then optionally a second, to load
  them. **Ordered from the start** (Andrew): fire then air is a different
  spell from air then fire, giving 4 singles + 16 ordered pairs (4 of them
  doubles) = 20 casts.
- **The elements are the wizard's own.** Players combine with each other
  through what their spells leave in the world (water, mud, oil, steam), not
  by mixing elements between players.
- **Cast time:** loading elements starts a cast meter that drains; at zero
  the spell casts. Every spell gets a cast time, which ends spamming.
  Later, harder combos must be entered within a time window.
- **The look:** an element orb in each hand; casting slams them together.
  On screen, the centre bar (where stamina shows while in use) shows the
  loaded orbs (a coloured circle with the element's icon) with the cast meter
  under them.

- **Moving while casting:** yes, at 60% walking speed while the meter
  drains, for a little tension.
- **Cancel: B (east face) while casting** cancels the cast, from the start.
  It's the usual "back" button, and casting takes priority over its normal
  use.
- **Non-elemental spells on the d-pad** (Hasten Time, Shapeshift, Light,
  Control Weather and any others): left and right cycle through them, up
  casts the selected one, down opens the grimoire.
- **The d-pad HUD:** a Dark Souls-style cross in the bottom-left corner:
  arrows on the left and right, the selected spell's icon on top, a small
  book icon on the bottom.
- **Discovery:** combos start hidden. Casting one for the first time adds
  it to the **grimoire**, opened with d-pad down.

## Draft combo table (to tune)
Combos make the materials the chemistry already simulates. Rule of thumb: the
**first** element is what the spell is made of, the **second** is how it
moves or changes.

Singles: Fire Bolt, Create Water, Raise Earth, Gust of Wind.

| first ↓ / then → | Fire | Water | Earth | Air |
|---|---|---|---|---|
| **Fire** | Fireball (held, at the ground: Meteor) | Scalding steam burst | Lava pool | Flame wave (wind-driven fire) |
| **Water** | Fog bank | Wave / flood | Mud | Frost (Ray of Frost) |
| **Earth** | Molten boulder (leaves lava) | Plant growth | Wall of Stone | Dust storm |
| **Air** | Lightning | Rain cloud / storm | Sand blast (leaves sand) | Whirlwind |

Shape from how the cast is released, the same for every combo: tap for a
projectile, hold for a beam or stream (trigger depth for power), aim at the
ground for an area or wall.

## Built (the view, 2026-10-30)
- **Controller:** fire {LB}, water {RB}, earth {LT}, air {RT}; {East} cancels; the d-pad cycles,
  casts and opens the grimoire; dodge roll moved to the right stick's click; pad zoom is gone.
- **Keyboard:** Z X C V, B to cancel, [ ] to cycle, T to cast, J for the grimoire. The mouse still
  casts what the radial picked. Tab keeps the radial, and F2 swaps the controller to its old layout.
- **Not yet:** tap, hold and ground shapes (every cast goes off at charge 1, from the crosshair);
  other players' orbs aren't drawn, only their casts.

## Open
- Three-element combos: held back as a later mastery layer.
- What the grimoire shows for a found combo (name, elements in order,
  shape, a short line) and whether there are hints for unfound ones.

## Build split
- **Headless (worker VMs):** the combo table and cast times in rules data,
  the core's "loaded elements → spell" lookup and the cast timer, in both
  engines with tests.
- **Host (Gameplay, the Editor):** the input sequence, the orbs in the
  hands, the slam animation, and the centre-bar orbs and meter.

## HUD layout (Andrew, 2026-10-29)
- Everything scales with resolution (reference 1920x1080).
- Top left: the portrait in the corner, the health and magic bars stacked to
  its right; large and readable.
- Bottom left: the d-pad cross, sized like the top-left block.
- Centre: stamina as a green ring that drains and refills, shown only in use.
- About 2/3 down the screen, below the ring: the loaded element orbs and the
  cast meter, at a similar size.
- Right side (or across the bottom): the message log.
