# Traversal: unique character verbs, and critters that change the world

Two linked ideas:

1. **Each character gets around the world in a genuinely different
   way,** derived from their actual spells and skills. No re-skinned
   "dash, but with sparkles".
2. **Collectible critters** carry world-changing abilities, as Cut and
   Surf do in Pokémon. But they *change the physics state of the world*
   rather than opening a gate, and their effects combine.

**Open on purpose:** whether abilities are locked to specific characters
or any character can learn anything. The data model below supports both,
so that decision can wait (see "Locked vs. flexible").

## Rule 1: Uniqueness means a different *physical channel*

"Not a reskin" needs a test you can apply. An ability is distinct only
if it manipulates a **different physical channel** from the others in
the party.

| Channel | What changes | Example verb |
|---|---|---|
| **Position** | Where you are, skipping the space in between | Blink, teleport, swap places |
| **Gravity / mass** | How you fall, how heavy things are | Levitate, slow fall, feather-light, heavy stomp |
| **Force** | Pushing and pulling things | Gust, shove, throw, telekinetic yank |
| **Friction / surface** | How things slide or grip | Grease slide, ice skate, spider-climb walls |
| **Geometry** | Terrain shape | Raise a pillar, carve a tunnel, wall of stone or ice |
| **Material state** | What a thing *is* | Burn, freeze, melt, soak, petrify, grow |
| **Fluid** | Where water and air go | Raise water, part water, updraft, fog |
| **Form** | What *you* are | Wild Shape into a spider, fish or bird; gaseous form |
| **Connection** | Linking things | Rope or grapple, vine bridge, tether two objects |
| **Perception** | What you can see or reach | See through walls, sense heat, a familiar scouts ahead |

**Design rule:** a party of four should cover four *different*
channels. If two characters both "move fast", one of them needs a
different verb.

## SRD-derived character traversal (sketch)

These are all ideas grounded in SRD 5.2.1 class features, spells and
species traits. **[verify each against the SRD text before building.]**

| Source | Channel | Traversal verb | Why it's not a reskin |
|---|---|---|---|
| **Fighter** | Force | Throw allies across gaps ("fastball"); brace as a platform others can jump off; break walls | Moves *other* people, not self |
| **Rogue** (Thief) | Friction / connection | Climb anything (Thief's climbing feature); grapple line; squeeze through gaps | Vertical routes others can't take, then drops a rope |
| **Monk** | Gravity / friction | Run on walls and water at higher levels (Unarmored Movement); slow fall; huge jumps (Step of the Wind) | Momentum and surfaces: a speed-running character |
| **Druid** | Form / material | Wild Shape into beast forms: spider (climb), fish (swim), bird (fly, small); grow plants; vine bridges | Becomes a different body with different physics |
| **Wizard** | Position / gravity | Misty Step, Levitate, Feather Fall for the party, Mage Hand at range | Bends rules locally, with a limited budget (slots) |
| **Sorcerer** (Draconic) | Gravity / material | Late wings; elemental affinity (a fire-immune walk through flames) | Elemental immunity *is* traversal |
| **Cleric** | Material | Create or Destroy Water (fill or drain a pit); light against darkness hazards | Shapes fluids and hazards, not self |
| **Paladin** | Force / connection | Find Steed: a mount carries the party; shield-charge breaks barricades | A vehicle and a battering ram |
| **Ranger** | Perception / friction | Tracks, sees hidden paths, climb and swim speed, and animal friends | Finds routes others don't see |
| **Warlock** | Position | Invocation-style at-will Levitate or blink, short range | At-will but weak, versus the Wizard's strong but limited version |
| **Goliath / Dragonborn** (species) | Force / gravity | Large Form (heavier, pushes more); Draconic Flight (short glide) | The species layer stacks on top of the class |

Spells that *aren't* class-locked (Gust, Ray of Frost, …) remain
the shared chemistry kit. Class verbs are the signature moves.

## Critters: collectible world-changers

### What a critter is

A small creature that **is an element or material source** with a
personality. It doesn't open gates; it adds something to the world, and
the chemistry engine does the rest.

| Critter (placeholder) | Emits or does | Example uses |
|---|---|---|
| **Ember newt** | Fire | Burn brambles and webs, light braziers, heat a boiler, ignite oil |
| **Puddle frog** | Water | Fill a basin, soak a fire, make mud, feed a water wheel, grow plants |
| **Rime moth** | Cold | Freeze water into a bridge, put out fire, make ice ramps |
| **Burrow mole** | Earth / geometry | Raise a rock pillar, break boulders, dig under walls, dam a stream |
| **Gale finch** | Air / force | Updrafts, push a sail or windmill, blow fog or gas away |
| **Spark eel** | Lightning | Charge metal, power a mechanism, electrify water (careful!) |
| **Glow beetle** | Light | Banish shadow hazards, signal, photosynthesis for plants |

5E flavour: critters are bonded through **Find Familiar**, **Animal
Friendship** or Druid and Ranger features, rather than captured.

### Rule 2: The world has machines that consume physical states

Combination puzzles need something to *combine for*. Put physical
machines in the world whose inputs are chemistry outputs:

| Machine | Input (a physical state) | Output |
|---|---|---|
| **Steam engine** | Water + fire → steam pressure | Drives a lift, a gate or a drawbridge |
| **Hot-air balloon** | Fire under the envelope | Carries the party up; wind steers it |
| **Water wheel** | Flowing water (dam or undam with the mole) | Turns a mill, a pump or a crank |
| **Windmill / sail** | Air force | Grinds, pumps, moves a raft |
| **Counterweight** | Mass: a boulder, an ice block, a thrown ogre | Opens a portcullis or raises a platform |
| **Lightning rod / coil** | Charge, via metal path or wet path | Magnetic door, elevator, trap |
| **Greenhouse vine** | Water + light | A living bridge or ladder that grows over time |

**Steam = water + fire** is the canonical "you need two things" puzzle.
Ice block + fire = water you can move. Water + cold + mole's pillar
gives an ice slide down a raised ramp. Solutions are recipes, and
recipes are discoverable.

### Rule 3: Obstacles are states, never locks

- **Every obstacle is a state that can be changed,** never "requires item X".
- **At least two solutions per gate.** A boulder can be:
  - broken by the mole;
  - shoved by a strong fighter;
  - frozen-cracked, if you heat it and then cool it;
  - bypassed by the climber.
- **Changes persist and have side effects.**
  - A burned forest stays burned: new paths, lost cover, fleeing
    creatures, smoke.
  - Flooding a valley drowns fires, and also your stealth route.
  - Consequences cut both ways, which is what makes them feel real.
- **The world fights back sometimes.** Wind spreads your fire to the
  bridge you needed.

### Avoid the HM tax

Pokémon's HMs were widely disliked because a party slot was spent on a
field move [mem]. Fixes:
- Critters **ride along outside the combat party**: a satchel, a
  shoulder perch, a lantern.
- Or critters are usable in combat *and* field: an ember newt in P5
  combat ignites oil under enemies.
- Carry a limited number, so choosing *which* critters to bring is a
  light puzzle.

### Legal caution: creature-collecting patents

Nintendo sued Pocketpair (Palworld) in 2024 over *patents* covering
creature-capture and riding mechanics [mem: **verify the current
status before building capture mechanics**]. Copyright doesn't protect
game mechanics, but patents can.
- **Safer path:** befriending or bonding through 5E means (Find
  Familiar, Animal Friendship, quests). Avoid throwing a capture device
  at a creature, and avoid Pokémon-like naming and visuals.
- Check this before Phase 4, not now.

## Locked vs. flexible (deferred, but supported)

Abilities are data with a **source**:

```
Ability { id, channel, source: Class|Subclass|Species|Critter|Item|Spell,
          emits?: Element|Material, force?, geometry?, cost: slot|stamina|cooldown|atWill }
Party   { members[], critters[] }  →  available verbs = union of sources
```

| Model | How it plays | Prior art |
|---|---|---|
| **Locked** (verbs belong to characters) | Party choice *is* the traversal toolkit. You swap members to solve a route. Strong identity | The Lost Vikings, **Trine** (the wizard conjures boxes, the thief grapples, the knight lifts) [mem] |
| **Flexible** (anyone learns anything) | Build-crafting freedom. Identity comes from what you *chose* | Skyrim, Divinity OS2 |
| **Hybrid** (likely sweet spot) | A signature verb per class (locked) + shared spells (learnable) + critters (swappable) | — |

`Game.Core` doesn't care. Only the source-to-character mapping changes.

**Party picking:** a roster of recruitable characters plus a
critter collection, with a choice of who comes along, works with every
model. Revisiting old areas with a new party or critter to reach new
places is the Metroidvania payoff.

## How this lands in the prototypes

| Prototype | What to test |
|---|---|
| **P1** (2D side-view) | **The best fit.** A Metroidvania with party swapping (Trine-style) and 2–3 critters. One steam-engine room |
| **P2** (3D platformer) | Two characters with different channels (Monk: walls and water; Wizard: blink and levitate), plus the mole and frog. One hub with a balloon or counterweight |
| **P5** (physical tactics) | Critters in combat: the newt ignites oil, the mole raises cover mid-fight, the eel electrifies a pool full of enemies. Fighter throws allies to high ground |

**Add to the shared kit:** three critters (ember newt, puddle frog,
burrow mole), two machines (steam engine, counterweight), and the
channel tags on every ability.
