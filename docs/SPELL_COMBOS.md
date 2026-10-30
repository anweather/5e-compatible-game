# Element combos (design, 2026-10-29; the table redone 2026-10-30)

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

## The combo table: one verb per cast (Andrew, 2026-10-30; J-039)
The first draft gave most casts the same job ("put an element on an area,
plus damage") in different shapes. Now each cast has **its own verb**. Rule
of thumb, unchanged: the **first** element is what the spell is made of, the
**second** is how it moves or changes.

**How every cast is built:**
- **Through the material system.** A spell applies its element; the
  material verbs in `rules/materials.json` (burn, melt, freeze, soak, boil,
  break, shatter, conduct, float, grow, rot, rust, dissolve, dig) decide what
  happens to each cell. New behaviour goes in a material verb, not in a
  reaction written for one spell. The hand-written spell reactions left in
  `rules/reactions.json` (`fire-mud`, `fire-ice` and the like)
  move into materials as they're touched.
- **Drawn with the effects library.** The pooled particles, cell tints and
  shader terms that exist; GPU first, no swarms of entities. A real object
  (the boulder) is built from simple pieces.
- **Setup or trigger.** A setup leaves something in the world that lasts and
  changes; a trigger transforms what's already there.
- **Chaining:** an element cast onto a spell still in the world changes that
  spell (fire into a dust cloud explodes; lightning into a rain cloud makes a
  storm). That one rule turns 20 casts into far more outcomes.

| first ↓ / then → | Fire | Water | Earth | Air |
|---|---|---|---|---|
| **Fire** | Fireball | Scalding Steam | Lava Pool | Falling Star |
| **Water** | Geyser | Wave | Mud | Frost |
| **Earth** | Forge | Plant Growth | Wall of Stone | Rolling Boulder |
| **Air** | Lightning | Rain Cloud | Sand Blast | Whirlwind |

Singles: Fire Bolt, Create Water, Raise Earth, Gust of Wind.

**Fire: light, blast, scald, make terrain, strike from above.**
- **Fire Bolt, the match:** small and exact; lights one thing (a fuse, a
  lamp, a bush).
- **Fireball, the explosion:** a blast that scatters burning debris a few
  cells out and sets off oil, dust and gas. (Its "held: Meteor" goes; Meteor
  stays for meteor showers.)
- **Scalding Steam, the fog that hurts:** scalds, blocks sight, rusts metal
  faster, condenses back to water.
- **Lava Pool, the terrain maker:** runs downhill like water, crusts over so
  it can be walked on, turns to rock at once in water.
- **Falling Star (replaces Flame Wave), the strike from above:** the only
  delayed cast. A glowing mark shows where it lands about a second later, so
  you aim ahead. A crater, burning rock as fuel, hot rock or a little lava,
  snow and ice melted, water flashed to steam. A small Meteor; the full one
  stays for showers.

**Water: fill, launch, wave, stick, freeze.**
- **Create Water, the bucket:** fills holes, puts out a fire, soaks fuel
  against fire, waters plants. Still, on purpose.
- **Geyser (replaces Fog Bank), the launcher:** acts on water already
  there. Aimed at a pond or river, it boils that spot into a jet that throws
  players, props and creatures upward (reach a ledge, launch a goblin) and
  scalds swimmers. Fog's hiding job goes to Fog Cloud.
- **Wave (replaces Flood), the moving one:** travels over the ground,
  carries light props, puts out fires on the way, knocks creatures back,
  leaves mud where it stops.
- **Mud, the sticky setup:** creatures get stuck. Fire (Forge) bakes it to
  brick, a building material; frost freezes it hard; it dries to dirt over
  time.
- **Frost, the bridge builder:** freezes water surfaces into walkable ice
  and wet creatures solid (a hit shatters them). Frozen ground is slippery:
  an ice-slide mini game.

**Earth: dig, transform, grow, build, roll.**
- **Raise Earth, the shovel:** raises or digs soft ground only, by terrain
  hardness (soil, sand, mud and rubble; not rock).
- **Forge (replaces Molten Boulder), the transformer:** heats the ground or
  a thing where it's aimed, and the material decides: sand to glass, mud to
  brick, metal red-hot (it burns whoever touches it), ice and snow melt, wet
  things dry.
- **Plant Growth, the living one:** vines climb walls and bridge gaps;
  roots crack stone over time (break); it grows fuel that burns, and rots
  when it dies.
- **Wall of Stone, the builder:** walls, ramps and dams. Stone is hard, so
  only a pick, Shatter or Falling Star breaks it. Over water it makes
  stepping stones.
- **Rolling Boulder (replaces Dust Storm), earth set moving:** a boulder
  rolls downhill with the terrain, knocks creatures over, smashes brittle
  things (glass, ice, crates), stops in water as a dam, and leaves a trail
  of dust (light it: a dust explosion). A bowling mini game, and a ball for
  the ball court.

**Air: fan, conduct, rain, scour, carry.**
- **Gust of Wind, the fan:** blows gas, fog and steam away, pushes floating
  things across water, blows out small fires and feeds big ones.
- **Lightning, the conductor:** jumps to the nearest metal or water in range
  instead of flying straight, and runs through it. Shocks wet creatures,
  lights dry fuel, shatters frozen things, and powers machines (gates,
  levers, `machines.json`): "get the charge to the gate" puzzles.
- **Rain Cloud, weather in a bottle:** drifts with the wind; soaks, puts
  out fires, waters plants, slowly fills holes. Lightning into it makes a
  storm that keeps striking; cold makes snow; Gust steers it.
- **Sand Blast, the scourer:** strips surface layers (rust, moss, snow,
  frost, acid stains), wears wooden walls thin and raised dirt down, and
  leaves sand that Forge turns to glass.
- **Whirlwind, the carrier:** travels, and takes on what it crosses: a fire
  tornado over fire, a waterspout over water, a sandstorm over sand, a
  smoke column, a snow flurry. It carries what it picks up and drops it all
  when it dies out.

**Built (2026-10-30), the water row:** Geyser (a spell `needs` water: the pond boils to steam, a `thrown` status and a scald, a fizzle on dry ground), Wave (a rolling front, `World.Waves` for the view's props: douses, soaks, knocks back, mud where it breaks) and Frost (ice bridges, the wet frozen solid and shattered by the existing cracked stage, and a slippery `frost` cover from cold of power 4), in the rules and both engines. The view still needs the jet launching players and props, props riding the wave, and the frost's look.

**Built (2026-10-30):** Whirlwind taking on elements and the dust explosion,
in the rules and both engines (`rules/README.md`).

**Build order:** first Whirlwind taking on elements, the dust explosion
(Rolling Boulder's dust or any dust cloud, plus fire), Forge on mud and sand,
and Falling Star. Then the rest of the table, a few casts per task.

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

**The looks (UX pass 2, 2026-10-30):** each drawn from the core, GPU-first, and staged by the
`spelllooks` probe: Forge's sparks and brick's courses, Falling Star's mark and a small meteor timed
to `World.Stars`, Whirlwind's funnel tinted by what it carries, dust and rain clouds as soft
billboards (`FogBank`) with rain from one particle system, lava as a contour mesh with glowing
cracks that close as it cools (`CellLavaMesh`), Light as a pool of real lights (`CellLightPool`),
Sand Blast's jet, the pick's swing and chips, and frozen raiders in ice, split when cracked.
Reel clip: `spell-chain.mp4`.

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
