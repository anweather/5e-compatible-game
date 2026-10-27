# Feature backlog

Ideas to build next, each with what it builds on today, what's still open,
and a rough size (S: days, M: a week or two, L: several weeks, XL: a
rethink of something core). Order here is not priority; see "Suggested
order" at the end. Started 2026-10-25.

The thread through most of these: **everything is chemistry**. A new system
earns its place when spells, items and abilities can change it, and it
changes them back (rules as data + interpreter, `ARCHITECTURE.md`).

## 1. Day and night

A clock that turns the sun and moon, sky and light.

**Built (2026-10-25, D10):** the clock (`Sky.cs`; a day is 24 real minutes,
starting at 9:00), the sun and a moon, the sky, ambient light and haze by
the hour (`SkyLight`), and the clock line on the HUD. Still open: anything
the night *does* (below), stars, and lights that switch on at dusk.

- **Influenced by:** spells and items that make light or dark (Daylight,
  Darkness, the lantern's light `use`, burning things); maybe a spell that
  speeds or stops time locally.
- **Influences:** what you can see and aim at (5e dim light and darkness),
  how far the lantern matters, which critters are out (`critters.json`),
  temperature (night freezes standing water sooner?), and the weather below.
- **Builds on:** the URP lighting in the yard; `FireLightPool` already
  shows fires as lights, which will matter much more at night.
- **Open:** does time live in the Chem core (deterministic, shared in
  lockstep, so the night falls at the same tick for everyone) or only in the
  view? If it changes rules (freezing, critters), it has to be core.
- **Size:** M for the look; M more for gameplay effects.

## 2. Weather

Rain, fog, wind, storms, snow, heat.

**Built (2026-10-25, D10):** world-wide weather in stretches (clear,
cloudy, windy, fog, rain, storm) after a calm start. Rain douses and
dampens fire, fills squares and dries puddles after; windy weather and
storms bring a wind fire and gas follow; storms strike lightning. In view:
rain, haze, cloud dimming, bolts, flashes, thunder by distance, rain and
wind loops (`WeatherFx`). The Storm season mod. Still open: snow and
cold (freezing), heat, fronts per chunk, weather that reacts to the world
(a big fire's updraft), Control Weather, rain that stops under roofs,
splashes, and recorded rain and wind instead of procedural loops.

- **Influenced by:** Create Water and fire at scale (a big fire makes
  smoke and an updraft), Gust of Wind, lightning spells, Control Weather
  later, time of day.
- **Influences:** chemistry everywhere: rain puts out fires and fills
  hollows (`Water` per cell), wind steers fire (the sim already has a
  per-cell `Wind` and a `DownwindBonus` for fire spread) and gas, storms
  bring lightning (`Charge`, and the lake already conducts), cold freezes
  water (`Frozen`), fog hides things.
- **Builds on:** `World.Sim.cs`'s tick (fire, water, gas, charge, wind),
  the `damp-world` and `wildfire` mods, which already read like weather.
- **Open:** a world-wide weather state, or per chunk (fronts that move
  across `WORLD_STREAMING.md`'s chunks)? Per chunk fits streaming and makes
  "the storm is coming" visible.
- **Size:** L. Depends on 1 for the look.

## 3. Layered terrain: caves, overhangs, an underground lake

Today the world is 2.5D: one `Cell` per 5 ft square, with `Height` for
earth raised or dug. Caves need more than one walkable level per square.

- **Goal scene:** an underground lake in a cave, reached three ways:
  through a maze of tunnels, by digging down to it, or through a metal gate
  opened with something (a key item, a lever machine, Knock, or melting or
  shocking the lock).
- **Builds on:** chunks (`Chunk.cs`), earth shaping (`World.Terraform.cs`),
  water flow, machines (`machines.json`) for the gate.
- **Open:**
  - The representation: a stack of cells per column (a few levels),
    vertical chunks (64×64×N), or a separate "underground" grid linked by
    shafts. Each has costs for the sim, the goldens and the TS sandbox
    (which mirrors `Cell`).
  - The view: Unity Terrain can't do overhangs. Caves need meshes (voxel
    or marching cubes for dug-out space, or authored cave pieces) beside
    the chunk terrains.
  - Does water flow down through a dug shaft into the cave? (It should.)
- **Size:** XL. It changes the core's shape, so decide the representation
  first, with a small spike.

## 4. Terrain hardness

Dig dirt, but not rock.

- **What:** each terrain gets a hardness; digging tools and spells have a
  strength; Shape Earth moves dirt and sand, not stone; a pick or Stone
  Shape is needed for rock; metal and bedrock never dig.
- **Builds on:** `materials.json` (add `hardness`) and the terraform
  spells, which already refuse some cells. Mostly data plus one check in
  `World.Terraform.cs`, mirrored in the TS sandbox.
- **Size:** S to M. A natural first step toward 3 (it decides where
  tunnels can go).

## 5. Items

More things to carry, use and combine.

- **Builds on:** `rules/items.json` (light, zoom, send, point uses today),
  `Carryable`, `ItemUse`, `NetItem`. Adding an item is already a recipe
  (`ARCHITECTURE.md`, "Where new things go").
- **Ideas:** tools that tie into 3 and 4 (pick, shovel, rope, key), flasks
  (oil, acid, holy water, alchemist's fire), climbing gear, a compass, the
  gate key. Items that weather and time change (a wet torch won't light).
- **Open:** inventory (hands only, or a belt/backpack?), and whether items
  can be crafted from what the chemistry leaves behind (ash, ice, metal).
- **Size:** S per item; M for an inventory.

## 6. Weapons: guns, blades, hammers

Halo-style loadouts: sniper, shotgun, assault rifle, pistol, sword, hammer.

- **Open first (fit):** 5e has firearms (the DMG's pistol and musket,
  modern guns as optional), but a Halo arsenal pulls the game toward a
  shooter. Options:
  - real guns as an optional rules module (a mod in `rules/mods/`);
  - *spell weapons* that fill the same roles: a sniper Fire Bolt (long,
    slow, precise), a shotgun Burning Hands cone, an automatic Magic
    Missile, a pistol cantrip; or
  - magic-tech (wands and rods) that read as guns.
  Any of them should feed the chemistry (a muzzle flash lights oil; a
  hammer cracks ice and rock, which ties into 4).
- **Builds on:** casts as intents (`SpellCaster.Cast`, `NetChem`), which
  is how shots stay in sync online; `Knockback`; `PHYSICAL_TACTICS.md` and
  `EXPLORE_AND_FIGHT.md` for how damage and hits work.
- **Open:** hitscan or physical projectiles (the yard's spells already
  fly); aim assist on gamepad; how 5e to-hit meets aiming (`PLAN.md`,
  question 3).
- **Size:** M for the first melee and ranged pair; L for the full set.
  Wants 7 for the shooters.

## 7. First-person mode

- **Builds on:** Starter Assets ships a first-person controller; the
  aiming code (`Aiming.cs`) already centres on the crosshair when the
  cursor is locked. `PLAN.md` lists P3 (first-person immersive sim) as one
  of the prototypes to compare.
- **Open:** a toggle (V to switch, like many games) or a separate mode; the
  wizard's body and hands in view (needs 10 to look right); how spell and
  aim markers read up close.
- **Size:** M for the camera and controls; more for good hands.

## 8. Proximity voice chat and sound attenuation

- **What:** voices and sound effects get quieter with distance and
  muffled through walls and water; voices carry only so far.
- **Builds on:** online play (Relay, up to 4 players). Unity Vivox has
  positional (3D) voice channels; D8 (proposed) picks Vivox and lets
  spells change voices (`MULTIPLAYER.md` §4–5). None of it is built: the
  only sounds today are the Starter Assets footsteps. Audio stays on (`DEV_SETUP_WINDOWS.md`,
  section 6).
- **Spells:** *where is that sound coming from?* A spell (or the Alert
  feat, or a familiar's ears) shows sound sources: ripples or icons at
  footsteps, explosions and voices. Silence makes a zone where nothing is
  heard (and no verbal-component spells, 5e). Thunderwave is loud and gives
  you away.
- **Open:** is sound a chemistry thing (a per-cell "noise" that critters
  react to, like `Charge` decays) or view-only? If enemies hear, it's core.
- **Size:** M for attenuation and voice; M for the sound-sight spell and
  stealth.

## 9. Animation: inverse kinematics and modified animations

- **What:** feet that plant on slopes and steps (earth shaping makes a lot
  of them), hands that reach for what they pick up and aim where they cast,
  and adjusted animations (casting poses per spell, carrying, sneaking).
- **Builds on:** `CastPose`, the Starter Assets Animator; Unity's
  Animation Rigging package (two-bone IK, multi-aim) works with Mecanim.
- **Size:** M for foot IK and aim; ongoing for new animations.

## 10. Effects between components at scale: ECS?

The question: should effects between things (fire on a barrel, wind on a
flag, a hammer on ice) and behaviours attached to them move to an Entity
Component System, and is that still the right way at scale?

**Decided in `DECISIONS.md`, D9:** use the ECS *pattern* in the Chem core,
but don't move the game to Unity's ECS (Entities / DOTS) now. The research
and the reasons are there.

> **ECS-ready (do this as we go):** keep the core interface-compatible with
> a later move to ECS. New core code follows D9's seams: data apart from
> systems, integer ids instead of strings in cell state, cells reached
> through accessors so a chunk can become one array per field, components as
> tags in the rules, commands as the only way to change shared state, and a
> view that only adapts. Existing code moves toward them when it's touched;
> the two biggest steps are interning the string ids in `Cell` (`T`,
> `Cover`, `Gas`) and the tags in point 1 below. Size: S per step, M in all.

- **What we already have is close to ECS.** The core is data plus systems:
  cells and entities hold state, `World.Sim.cs` and `World.React.cs` are
  systems that run over them each tick, and behaviour is table-driven
  (`reactions.json`), not code on objects. That's the part of ECS that
  makes interactions scale: a new material or entity combines with every
  existing rule without new code.
- **Why not Unity Entities now:**
  - Lockstep needs the same result on every machine (D4). Our core is
    plain deterministic C#; Entities and Unity Physics don't promise
    cross-machine determinism without a lot of care.
  - Online play uses Netcode for GameObjects. Entities would mean Netcode
    for Entities, a rewrite of `Scripts/Net/`.
  - The view, Starter Assets, the Animator, Cinemachine and the packs are
    all GameObject-based. A hybrid adds a bridge layer to every feature.
  - `DECISIONS.md` (D4) already says when to reopen this: competitive
    multiplayer with server-side prediction.
- **What to do instead, as the world grows:**
  1. Give entities components as data in the rules (`entities.json`):
     `flammable`, `conducts`, `floats`, `breakable`, `heavy`... and have
     reactions match on components, not on entity ids. Then "a hammer
     breaks anything brittle" is one rule.
  2. When profiling says the sim is the bottleneck (chunks make that
     later), store cell state as arrays per field inside each chunk
     (struct of arrays, not one `Cell` object per square) and run the tick
     with Unity's C# Job System and Burst. That's most of DOTS's speed
     without changing how the game is built.
  3. Use Entities only for things that are many and dumb and view-only:
     grass, particles, debris, crowds of critters that don't need to be
     exact.
- **Size:** 1 is M and pays off immediately; 2 is L, when needed.

## 11. World streaming, part 2

**First slice built (2026-10-26):** the chemistry covers the whole valley and
chunks wake and sleep around the players, in lockstep (`WORLD_STREAMING.md`,
"Part 2, first slice"). The rest below is still open.

The endless-world foundation, built in parallel with the prototypes. Part 1
(the chunked valley) is done; part 2 is what `WORLD_STREAMING.md` lists as
not in part 1, and makes its rings real.

- **What:**
  - Loading rings around each player: the player's chunk and its 8
    neighbours simulated at full detail, the next ring drawn with the
    simulation paused and props asleep, coarse terrain further out.
  - Chunk activation and unloading: the active-chunk set follows the
    players (all chunks are active today), and `UnloadChunk` gets used
    (nothing unloads chunks yet). Fire at the edge wakes the next chunk, up
    to a cap.
  - Fast-forward on wake: a bounded number of ticks for a chunk that wakes
    after a long pause.
  - Streaming to disk: only what players changed is saved; an untouched
    chunk regenerates from the world seed (seed generation).
  - Interest by chunk online: each player follows only the chunks near
    them; entering a chunk is a per-chunk late join (a snapshot at a tick),
    with a per-chunk checksum as the safety net.
  - Floating origin past ~5–10 km.
- **Builds on:** `Chunk.cs`, the active-chunk set and per-chunk hashes,
  `SpawnChunk` / `UnloadChunk`, `world/things.json` and stable `ThingId`s,
  `NetChem`'s lockstep and late join.
- **Open:** part 1's leftovers ("Still open in part 1" in
  `WORLD_STREAMING.md`); the host today vs. a dedicated server later (D7).
- **Size:** L to XL; split it into slices as part 1 was.

## 12. Reset the world online

Reset (R / View) reloads the scene on the machine that pressed it, so since
0.1.2 it's refused while playing together (`EventLog.ResetYard`; the online
probe's `reset_refused_online`). In 0.1.1, a reset on the host dropped
everyone's wizards.

- **What:** the host resets for everyone. A "reset world" command, stamped
  into a `NetChem` tick batch like an `EffectCommand`, that every peer
  applies at the same tick: the world back to its seed and `things.json`,
  props respawned with their stable ids, wizards back to their starts.
  Clients' reset could ask the host, or stay off.
- **Builds on:** `NetChem`'s tick batches and late join (a reset is close
  to a late join from tick 0), `ThingSpawner`, stable `ThingId`s.
- **Open:** does a client's reset need a vote or the host's say-so? What
  happens to held items and rule switches (mods) on reset?
- **Size:** S to M.

## 13. Swimming

Wading and swimming in the pond and lake (playtest P13). **Partly built:** a
basic version is in `SurfaceEffects`: water over the chest floats the wizard
at the surface (the controller's gravity off, its fall speed steering it to
floating height), slower, standing pose; jump kicks it up out onto a bank;
walking up a bank ends it; ice is walked on (probe `swim`). Still to do:
a swim animation, diving, drowning.

- **What:** deep water (a cell's water over the wizard's knees, from
  `Cell.Water` and the drawn surface) switches the wizard to swimming:
  slower, floating at the surface, a swim animation, out by walking up a
  bank. Shallow water slows.
- **Influenced by:** freezing (walk on ice), currents (water flow marks),
  rain filling hollows.
- **Builds on:** `WaterSurface` heights, the Starter Assets controller
  (its grounded and gravity steps), `NetWizard` (the client moves itself).
- **Open:** animations (Starter Assets has none for swimming; a CC0 or
  Asset Store set), diving, drowning (5e: hold breath).
- **Size:** M.

## 14. Climbing

Climbing walls of stone, rocks and the cabin (playtest P14).

- **What:** walk into a steep face and hold jump (or a climb button) to
  climb it at a stamina cost (P19's bar), mantle over the top.
- **Builds on:** `MistyStep`'s `TopAbove` (finding the top of a face),
  `CellBlockers`, the stamina bar.
- **Open:** animations; which faces are climbable (all, or a tag on
  materials and props: "rough"); 5e rules (half speed, Athletics).
- **Size:** M to L.

## 15. Ball court scoreboard

A score for the ball court east of the yard (Andrew, after the recap).

**Built (2026-10-26):** `BallCourt` counts a ball fully over a goal line at
either end, logs it and shows it on a lit scoreboard by the north rail
(`CourtScoreboard`); after a goal, or 2 s outside the rails (not while
carried), the ball goes back to the centre spot. Online the host decides and
`NetCourt` syncs the score. The court also got a dirt floor and a gate on
the yard's side that opens for any wizard nearby (`CourtGate`); probe
`court`. Still open: the HUD line, a reset button, a clock or first to N.

- **What:** count the goals at each end and show them on a board by the
  court (and a line in the HUD near it). After a goal, or when the ball
  leaves the court, it goes back to the centre spot.
- **Builds on:** the court's goals and rails (`ValleyBuilder.BuildCourt`),
  the ball (`NetProp`, `Kicker`). Online the host decides goals and resets
  (it runs the physics) and sends the score like tag's state (`NetTag`).
- **Open:** a reset button; a match clock or first to N.
- **Size:** S.

## 16. Platformer tower

A climbing course: an area with a tall tower of platforms and objects to
get to the top of (Andrew, after the recap).

- **What:** a double jump; platforms that move, platforms that collapse a
  moment after you stand on them and come back later so it can be tried
  again, platforms that phase in and out on a timer, maybe a wall jump. A
  flag at the top, and fireworks when you reach it.
- **Builds on:** the Starter Assets controller (its jump; a second jump is
  a `SurfaceEffects`-style correction), the swim kick's vertical speed,
  `CellBlockers` for walk-on boxes, the meteor's lights and particles for
  the fireworks. A builder like the court's places the tower.
- **Open:** online: moving and collapsing platforms run off the shared tick
  (like the weather) so every player sees the same; a best time per player.
- **Size:** M.

## 17. Tower defense

A second play area where waves of critters walk toward a flag and the
players stop them with spells (Andrew, after the recap).

- **What:** press a button to start; waves of critters (placeholders until
  Andrew supplies models) walk a path toward a flag. Players raise
  barriers with earth spells (walls, trenches, water) and hurt them with
  damage spells (Fire Bolt, Fireball, Meteor, lightning through water).
  A wave that reaches the flag costs a life.
- **Builds on:** the sandbox's critters and entity damage (`entities.ts`,
  `RULES.critters`), `SpellForces.Hit` for wizards (the same for
  critters), pathing round blocking cells (Unity AI Navigation is in the
  manifest, or a grid search over the cells, which the chemistry already
  knows), terrain the spells already reshape.
- **Open:** critters as core entities (deterministic for lockstep) or
  host-run physics props; their stats in `rules/` as data; difficulty.
- **Size:** L.

## 18. Finer ground

Smaller squares for the ground, so a slanted trench or the river is a
smooth channel, not steps (playtest, after the slant aim).

- **What:** the chemistry's squares are 1.5 m. The terrain now slopes
  between square centres (`CellHeightfield.Blended`), which softens the
  banks, but a trench at an angle still steps a square at a time. Two
  ways on: halve the squares to 0.75 m (four times the cells: the sim,
  the network checksum and every square-based number in `rules/` change),
  or keep the squares and draw the ground from a smoother field (a
  distance field from the shaped squares' edges, or rounding each
  square's corners by its neighbours) that keeps a one-square pit at full
  depth. Tried and rejected: a wider blur, which flattens a one-square pit
  to a quarter of its depth.
- **A third way (Andrew): different granularity per effect.** Not every
  effect needs the same square. Height could live on a finer grid (say
  2 × 2 sub-squares of 0.75 m per 1.5 m square) while fire, gas, charge
  and the rest stay at 1.5 m. Shaping is rare (a cast), so a finer height
  layer costs memory and little time; it only runs when the ground moves.
  The ticking effects keep their cost. The catch is water: it flows by
  height every tick, so either it reads each square's mean height (cheap;
  a slanted trench is drawn smooth but water still moves square by
  square), or water moves to the finer grid too (smooth flow down a
  slanted channel, four times the water work). Lines and craters would
  shape sub-squares along the true aim (`SlantCells` at the finer step).
  It all stays integer and deterministic, so lockstep is unaffected, but
  the TS sandbox has to match, and the network checksum and goldens
  change. Likely the best balance: finer height plus mean-height water
  first, finer water only if the flow looks wrong.
- **Size:** M (drawing) to L (finer squares or finer height).

## 19. Performance pass

The valley has grown (81 chunks, 576 × 576 squares, the falls, four
cabins). Measured 2026-10-26 in the Editor: an ordinary frame 6.9 ms mean,
8 ms p95; a wildfire of 650 burning squares 9.5 ms mean but 25 ms p95,
with spikes up to 134 ms (drawing) and 31 ms (reshaping the ground);
entering Play mode takes about 60 s.

- **What:** find and flatten the spikes (batch the terrain writes per
  frame, cap fire effects further away, profile the 134 ms frame), and
  look at Play-mode start (the eager terrain copies at Bind, the yard
  build). Measure a player build, not just the Editor.
- **Builds on:** the wildfire and perf probes (their timing metrics).
- **Size:** M.

## Suggested order)

Dependencies, roughly:

0. **World streaming, part 2 (11)** runs alongside everything below, as
   the endless-world foundation; features that store per-cell or per-chunk
   state (weather, caves) should fit its chunks.
1. **Terrain hardness (4)** is small and sets up digging and caves.
2. **Components in the rules (10.1)** before many new items and weapons,
   so each new thing combines with the rest for free.
3. **Day and night (1)**, then **weather (2)**, which reuses the sim's
   wind, water and charge.
4. **Items (5)** alongside, especially tools for digging and the gate.
5. **First person (7)** before **weapons (6)**, if guns are in.
6. **Sound attenuation and voice (8)** once online play gets regular use.
7. **Caves and the underground lake (3)** after a representation spike;
   it's the biggest change to the core.
8. **IK and animation (9)** whenever the look of a feature needs it.
9. **Performance pass (19)** before the next playtest with friends; the
   **scoreboard (15)** is small and can go any time; the **platformer
   (16)** and **tower defense (17)** are play areas that build on the rest.
