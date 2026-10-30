# Element combos (design draft, 2026-10-29)

Andrew's direction, still being explored. The spell radial stays as a debug
and experiment tool. The real input becomes elements combined in sequence.

## Decided so far
- **Four elements on four buttons:** fire, water, earth and air on the
  bumpers and triggers. Face buttons stay jump and interact; sticks move and
  look; the d-pad is to be decided.
- **Sequence, not chord:** tap one element, then optionally a second, to load
  them. **Unordered for now** (fire then air = air then fire), giving 4
  singles + 6 pairs + 4 doubles = 14 casts. Ordered pairs can come later,
  since a sequence makes order readable.
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

## Draft combo table (to tune)
Combos make the materials the chemistry already simulates.

| | Fire | Water | Earth | Air |
|---|---|---|---|---|
| **single** | Fire Bolt | Create Water | Raise Earth | Gust of Wind |
| **Fire** | Fireball (held, at the ground: Meteor) | Steam / fog | Lava | Lightning |
| **Water** | | Wave / flood | Mud, plant growth | Frost / ice |
| **Earth** | | | Wall of Stone | Sand / dust storm |
| **Air** | | | | Whirlwind / weather |

Shape from how the cast is released, the same for every combo: tap for a
projectile, hold for a beam or stream (trigger depth for power), aim at the
ground for an area or wall.

## Open
- Can a cast be cancelled, or interrupted by damage? Can the wizard move
  while the meter drains?
- Where the non-elemental spells go (Hasten Time, Shapeshift, Light, Control
  Weather): d-pad abilities, items or scrolls, or cut.
- Three-element combos: held back as a later mastery layer.
- Discovery: whether combos start hidden and fill a grimoire as found.

## Build split
- **Headless (worker VMs):** the combo table and cast times in rules data,
  the core's "loaded elements → spell" lookup and the cast timer, in both
  engines with tests.
- **Host (Gameplay, the Editor):** the input sequence, the orbs in the
  hands, the slam animation, and the centre-bar orbs and meter.
