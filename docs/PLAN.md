# Plan to go deeper

Principle: **each step answers one open question with something
playable or measurable.** The combat-mode decision is made at Gate 1,
from evidence, not up front.

## Open questions (in the order the plan answers them)

1. Is the spell sandbox *fun* in 3D, and which mode does it want?
2. Can deterministic physics resolve a turn fast and small enough for
   `ApplyMove`?
3. Does a 3D canvas live happily in ShadowMount (pointer lock, asset
   loading, phone performance)?
4. How much of 5E survives contact with physics (to-hit vs. aim, saves
   vs. dodging, push distances vs. impulses)?
5. Is platforming a genre or a seasoning? (Likely seasoning: traversal
   puzzles using Jump/Levitate/Misty Step between fights.)

## Phase 0 — Spikes (each a small, throwaway-friendly bench)

**Spike A: Spell bench** (answers Q1, Q5). Modelled on boardgame-plugins'
`ship-lab/`: one thing, isolated, judged on feel.

- **Stack:** Vite, TypeScript, Three.js or R3F, and `@dimforge/rapier3d-compat`.
- **Scene:** one wizard on a kinematic controller, a greybox arena with
  ledges and a pool, crates, an oil barrel, a wooden bridge, three
  dummy enemies on dynamic bodies.
- **Eight spells, all SRD:**
  - Fire Bolt
  - Ray of Frost
  - Thunderwave
  - Grease
  - Gust of Wind
  - Create Water
  - Levitate
  - Misty Step
- **Chemistry v0:** a JSON table over tags. Fire + flammable gives
  burning. Water + fire gives steam. Cold + water gives ice. Oil + fire
  gives a spreading blaze. Lightning + wet gives a chain.
- **Done when:** you've recorded 3 clips of an interaction you didn't
  explicitly script (for example, grease → push → slide into the pool →
  freeze).

**Spike B: Deterministic turn resolve** (answers Q2). This is a Node
script, not a game.

1. Build the same Rapier scene from JSON.
2. Apply an impulse.
3. Step at a fixed dt until asleep.
4. Hash the result.
5. Run it in Node and in Chromium (Playwright) and compare the hashes.

Measure:
- milliseconds per resolve;
- ticks to settle;
- serialized state size, against the per-move RPC and storage budget.

Done when the hashes match across both, and you have a number.

**Spike C: 3D in ShadowMount** (answers Q3).
- Mount Spike A's canvas as a `plugin.mjs` behind a stub plugin locally.
- Check pointer lock, resize, `fullScreenBoard`, lazy GLTF loading from
  `/assets`, and frame rate on a phone.

## Gate 1 — choose the mode

Decide from Spike A's feel and Spike B's numbers.

| If… | Then |
|---|---|
| The best moments came from *aiming and timing* | Action game. Standalone-first; the platform is used for saves and Spaces only |
| The best moments came from *setting up chains* | Turn-based. Plugin-first, with server-resolved physics |
| Both | **Hybrid**: real-time exploration and traversal, turn-based combat (the Pocket Tactics split). This is the current best guess |

Write the result into a `docs/DECISIONS.md` in the new repo. That
mirrors tetra-master's RESEARCH vs. DECISIONS split.

## Phase 1 — Sim core (mode-agnostic)

- **Repo:** create a new one, for example `anweather/<name>-rpg`, with
  the tactics-style layout: `src/core` (pure), `src/adapters/three`,
  `data/`, `tests-unit/`.
- **Chemistry engine:** elements, material tags, an element×tag table,
  an element×element table, and DOS-style orthogonal modifiers
  (electrified/frozen, blessed/cursed).
- **Field grid:** a coarse voxel/heightfield cellular automaton for fire
  spread, liquid depth, oil, ice and gas. It must be deterministic and
  unit-tested.
- **Body ↔ cell coupling:** objects ignite from the cell they stand on,
  wet objects conduct, frozen surfaces lower friction.
- **Spell = data plus a small effect vocabulary:**
  - emit element,
  - impulse (cone, sphere or line),
  - spawn body or wall,
  - modify gravity,
  - teleport,
  - spawn volume,
  - grab.

  Around 10 primitives should express most physical SRD spells.
- **Tests:** a golden test for every documented interaction, plus a
  "surprise log" of emergent combos found in play.

## Phase 2 — 5E layer

- **Import:** `data/build-srd.mjs` parses SRD 5.2.1 (Markdown, or
  Foundry packs) into typed JSON, for the subset implemented. It checks
  against a second statement in the source. The attribution text goes
  in the README and credits.
- **Core rules:**
  - ability scores and modifiers, proficiency;
  - d20 tests with advantage and disadvantage;
  - saves and DCs;
  - spell slots and concentration;
  - conditions (prone, restrained, grappled…);
  - all randomness through a seeded cursor in state.
- **Physics ↔ 5E mapping** (write it down as decisions):

  | 5E rule | Physical version |
  |---|---|
  | Push "10 ft" | Impulse calibrated to 3 m on a Medium creature |
  | Fall damage | Measured from physical fall height |
  | Prone | Triggered by ice or grease physics |
  | High ground | Gives advantage |
  | Shove / Jump / Throw | Cheap verbs (BG3's lesson) |

- **Slice:** two SRD classes (Wizard/Evoker and Fighter/Champion),
  levels 1–5, about 15 spells, about 6 monsters.

## Phase 3 — the chosen mode, as a vertical slice

- **Turn-based or hybrid:**
  - `plugin-backend/` on the TS SSDK, with `servePlugin` and
    `engineContract` conformance;
  - `ApplyMove` runs the TurnClock;
  - previews use the same function;
  - a hosted live room for co-op;
  - Spaces for a persistent campaign.
- **Action:**
  - feel first (Dark Alliance's lesson): lock-on, hit-stop, i-frames;
  - map AC and saves to real-time per RESEARCH §4;
  - the platform is used for saves.
- **Deliverable:** one dungeon, one boss and one traversal puzzle that
  requires combining two spells.

## Phase 4 — breadth (only after the slice is fun)

More classes, an SRD monster pipeline, art direction (whimsical:
stylised low-poly plus juicy VFX keeps the art budget sane), and
procedural rooms (tactics' chunk generator as reference).

## Learning track (runs alongside)

| Phase | Learn |
|---|---|
| Spike A | Three.js fundamentals: scene graph, cameras, lights, materials, GLTF; Rapier bodies, colliders, the character controller |
| Phase 1 | Cellular automata; instanced rendering; particles and VFX (three.quarks or custom shaders) |
| Phase 3 | Animation (mixamo-style rigs, blend trees); camera feel; netcode basics (fixed timestep, deterministic lockstep vs. authoritative state) |
| Optional | A weekend in Godot 4.6 to feel a full editor, then decide whether to miss it |

## Risks

| Risk | Mitigation |
|---|---|
| Scope (3D RPG is the canonical over-scoped hobby project) | Spikes are small; Gate 1 is honest; slice before breadth |
| Physics chaos versus tactical predictability | Previews from the same function; physics only where it's the joke or the point |
| Determinism drift on a Rapier upgrade | Pin the version; hash goldens in CI; re-seed and migrate like risk-remote's cursor change |
| Art cost | Greybox through Phase 3; stylised low-poly; CC0 kits (Kenney, Quaternius) |
| Licence slip (using "D&D", or non-SRD names) | Name list check in CI; only SRD 5.2.1-sourced text |
