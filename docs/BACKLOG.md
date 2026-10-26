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

## Suggested order

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
