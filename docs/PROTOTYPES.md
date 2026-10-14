# Prototypes: finding the paradigm

The constant is **5E-style characters and spells, with deep, whimsical
interactions between them**. Everything else is open: camera,
dimension, how much platforming, how progression works, and real-time
vs. turn-based. Turn-based is in only in its *physical* form (P5,
`PHYSICAL_TACTICS.md`). Grid tactics stays out, because Pocket Tactics
already covers it (D5).

The goal of this phase is to *feel* the options side by side, cheaply,
and to pick the one where the spells are both the most fun and the most
original.

## The three axes, which are separate questions

| Axis | Options | How to test it |
|---|---|---|
| **A. View / dimension** | 2D side-view · 3D third-person · 3D first-person · 3D top-down/isometric | **Cheap.** A camera and controller swap; a prototype each |
| **B. What the spells are *for*** | Movement and traversal (platforming) · fighting · solving the world (immersive-sim puzzles) | **Cheap.** The same spell kit, judged by which use is most fun in each view |
| **C. Progression structure** | Discrete levels (Mario, Cuphead) · hub plus levels (Mario 64/Odyssey) · interconnected map gated by abilities (Metroid) · open world (BotW) | **Expensive.** It needs content, so decide mostly on paper (below), then test with one small slice of the winner |

Mixing these up is the trap. "Open world vs. 3D Mario" is really two
questions: third-person view (A) with a platforming focus (B), versus
open-world structure (C). Prototype A×B; reason about C.

## The shared spell kit (identical in every prototype)

All of these are SRD 5.2.1 spells. Every prototype implements the same
eight, so the comparisons are fair:

| Spell | As movement | As combat | As world-solving |
|---|---|---|---|
| **Fire Bolt** | — | Damage; ignites | Burns webs, vines, bridges; melts ice |
| **Ray of Frost** | Freezes water into a platform | Slows | Ice bridges; puts out fire |
| **Gust of Wind** | Updraft and glide boost; push yourself | Pushes enemies off ledges | Blows fog, fire spread, sails, windmills |
| **Grease** | A slide or speed lane | Enemies slip and fall prone | Low-friction ramps; flammable |
| **Create Water** | Fills pits to swim up | Douses fire; enemies get wet (then lightning) | Raises the water level; grows plants |
| **Levitate** | A float or a slow-fall column | Lifts an enemy out of the fight | Lifts crates or yourself to a ledge |
| **Misty Step** | A short blink (dash or teleport) | Escape or reposition | Passes through bars or grates |
| **Mage Hand** | Carry a platform; grab a ledge item | Disarm; drop things on enemies | Levers, keys, out-of-reach items |

Under the kit is a chemistry table, the first job of `Game.Core`:
- **Elements:** fire, cold, water, air, oil, lightning.
- **Material tags:** flammable, wet, frozen, conductive, light, anchored.
- **Rules:**
  - element × tag
  - element × element

**Traversal additions** (see `TRAVERSAL.md`):
- three critters as element and material sources: ember newt (fire),
  puddle frog (water), burrow mole (earth);
- two machines that consume states: a steam engine (water + fire) and a
  counterweight (mass);
- a *physical channel* tag on every ability, so characters' verbs stay
  distinct.

The same table drives the 2D and 3D prototypes. Unity's 2D physics
(`Rigidbody2D`, Box2D-based) and 3D physics (PhysX) are *separate*
systems. Without a shared core, every rule would be written twice.

## The prototypes

Each one is a **timebox of 1–2 weeks** of evenings: greybox only,
the shared kit, one small space.

### P1: 2D side-view sandbox (Metroid / Cuphead / Noita direction)

- **Build:** one screen-sized room, then three linked rooms. The kit,
  three enemy types, water, oil, crates, a rope bridge.
- **Hypothesis:** 2D makes interactions *most legible*: you see the
  whole chain at once. Noita proved huge depth reads fine in 2D.
- **Fresh angle:** an authored Metroidvania where **spells are the keys
  *and* the chemistry set**. A gate isn't "needs the red key"; it's
  "the chasm is full of water, and you have Ray of Frost now". Most
  Metroidvanias gate with single-use abilities [mem]; here every gate
  should have more than one solution.
- **Learns:** Unity 2D (Tilemap, `Rigidbody2D`, the 2D URP renderer),
  game-feel basics.
- **Cost:** the lowest. Best first prototype for testing the
  *interactions* themselves.

### P2: 3D third-person platformer (Mario 64 / Odyssey direction)

- **Build:** a small "course" with ledges, a pond, a windmill, a
  greased slope, a few enemies, and one "star" goal reachable at least
  three different ways.
- **Hypothesis:** spells as *movement verbs* are the most joyful use.
  Levitate is a float, Misty Step a blink, Ray of Frost freezes the
  pond into stepping stones, Gust gives an updraft.
- **Fresh angle:** a 3D platformer where **you make your own
  platforms**. Odyssey's capture mechanic was a version of this [mem];
  a spellbook generalises it.
- **Learns:** the core of Unity 3D, a character controller, Cinemachine
  (camera *is* the hard part of 3D platformers), Rigidbody props.
- **Cost:** medium. Camera and controller feel take real tuning.

### P3: 3D first-person "immersive sim" room (Arx Fatalis / Dishonored / Prey direction)

- **Build:** P2's arena plus a small building, swapping in a
  first-person controller and camera. Add a locked vault with several
  ways in, guards, a fireplace, and a water tank.
- **Hypothesis:** spells as *world-solving tools* shine in first
  person: you look at a thing and do magic to it. The immersive-sim
  creed is that every problem has several answers.
- **Fresh angle:** immersive sims are rare and loved [mem]. One built
  on a 5E spell list with real chemistry is unusual.
- **Learns:** FPS controller, interaction raycasts, AI perception basics.
- **Cost:** low *after* P2, because it reuses the scene and props.

### P4: 3D top-down action (Magicka / Hades direction): deprioritised

- **Build:** P2's arena with a top-down camera and twin-stick or click
  controls.
- **Why it's low on the list:** the market is crowded (Diablo, Path of
  Exile, Hades [mem]). Magicka shows the one way it stands out: when
  *combining* is the whole game.
- **Keep it only as a cheap check.** It is a camera swap on P2's scene,
  so it costs about a weekend. If the chemistry is far more fun from
  above, that's worth knowing.

### P5: Physical turn-based tactics (party RPG, BG3 / Final Fantasy party direction)

Full concept: **`PHYSICAL_TACTICS.md`**.

- **Build:** P2's arena with a tactics camera, a party of three against
  goblins and an ogre.
- **Hypothesis:** turn-based play gets fresh when **the world resolves
  your intent physically**:
  - attacks hit whatever is really in the way;
  - charges leave you where you ended up;
  - throws depend on Strength against weight.
  - On top of that: reactions as the skill expression (Shield, readied
    Gust, interpose), and **flat HP**, so position and environment kill,
    not numbers.
- **Fresh angle:** no health sponges. A low-HP, high-lethality party
  RPG where the preview equals the outcome (sandbox physics scene, then
  replay).
- **Learns:** NavMesh, a tactics camera, Unity multi-scene physics
  (`PhysicsScene.Simulate`), turn and initiative state machines, UI
  Toolkit.
- **Cost:** medium after P2. The turn and reaction system is the new
  work, and it is mostly `Game.Core` (5E initiative, reactions,
  conditions), which Claude can build and test.

### P6: Third-person adventure with turn-based fights (Divinity/BG3 modes, BotW movement)

Full concept: **`EXPLORE_AND_FIGHT.md`**.

- **Build:** one valley and one goblin camp. Real-time third-person
  exploration with party-leader swapping and traversal verbs. Fights drop
  into P5 tactics in place.
- **Hypothesis:** the two modes *multiply* when exploration movement is
  used to **prepare the battlefield**: grease the bridge, take the high
  ground, park the newt by the oil.
- **Fresh angle:** BotW-grade field movement and chemistry plus
  BG3-grade physical turn-based combat. Also tests Expedition 33-style
  timed reactions against menu reactions.
- **Learns:** Cinemachine camera blends, a mode state machine,
  `NavMeshAgent` followers with off-mesh links, animation-event timing
  windows.
- **Cost:** low to medium *after* P2 and P5. The new work is the seam
  (engagement, followers, camera).

### What is *not* a prototype: open world

Open world is axis C, a structure, not a feel. A third-person (P2) or
first-person (P3) controller *is* the open-world controller. What open
world adds is **content scale**, which is the most expensive thing in
games and the worst fit for a solo learning project. If P2 or P3 wins,
test structure with one "valley" slice. See below.

## Progression on paper (axis C)

5E gives a natural progression spine: **you learn spells and gain spell
levels.**
- Cantrips are your always-on verbs.
- Slots are a limited resource per rest.
- New spell levels are new keys.

| Structure | How 5E spells drive it | Content cost | Fit |
|---|---|---|---|
| Discrete levels (Mario/Cuphead) | Each level is a curated puzzle-playground for the current spellbook; new spell, new world | Low to medium, and scales linearly | Good for P1 or P2. Easy to ship in slices |
| Hub plus levels (Mario 64/Odyssey) | The hub opens as spells arrive; levels are revisited with new spells for new stars | Medium | **The sweet spot for P2.** Exploration without open-world cost |
| Ability-gated map (Metroid) | Spell = key, with several solutions per gate | Medium; needs careful map design | **The sweet spot for P1** |
| Open world (BotW) | All core spells early, sandbox freedom | **Very high** | Only after everything else; probably never solo |

**Rule of thumb:** pick the structure whose content you could finish.
Hub-plus-levels and ability-gated maps both let you prove the fun in
one hub or region, then grow it.

## Scoring (fill in after each prototype)

Score each from 1 to 5. Record clips; they settle arguments.

| Criterion | P1 2D | P2 3P plat | P3 1P sim | P4 top-down | P5 phys. tactics | P6 explore+fight |
|---|---|---|---|---|---|---|
| Unscripted interaction moments (count clips) | | | | | | |
| Readability: could a friend see *why* it happened? | | | | | | |
| Movement feel before any spells (P5: does the preview read clearly?) | | | | | | |
| "Haven't played this before" factor | | | | | | |
| Content cost to reach a 30-minute game | | | | | | |
| What it taught me about Unity | | | | | | |
| Did I want to keep playing it? | | | | | | |

## Order

1. **Shared kit core** (`Game.Core` chemistry tables plus tests). Claude
   can build this in a cloud session, in parallel with you learning
   Unity basics.
2. **P2 (3D third-person)** first. It is the biggest Unity learning
   payoff, and it builds the 3D scene that P3 and P4 reuse.
3. **P1 (2D side-view).** The different physics and renderer show
   whether the shared core really is engine-agnostic.
4. **P5 (physical tactics)** on P2's arena. Its 5E turn, reaction and
   condition rules go into `Game.Core` first, which Claude can build
   ahead of time.
5. **P6 (explore + fight)**: stitch P2 and P5 together in one valley.
   It's cheap at this point, and it's the likely front-runner if both
   halves are fun.
6. **P3 (first person)** on P2's scene.
7. **P4 (top-down)**, a weekend, optional.
8. **Compare, pick, write D6.** Then one structure slice (a hub, a level
   or a region) for the winner.

**Alternative order** if you'd rather test the *interactions* first and
learn 3D second: start with P1. 2D is quicker to make expressive.
