# Plan to go deeper

Principle: **each step answers one open question with something
playable or measurable.** The engine is decided (Unity, D1). No grid
tactics; turn-based only in its physical form (D5). The paradigm (view, what spells are for, and
progression) is decided at Gate 1, from prototypes.

## Who does what

The work splits by where it can run.

| Work | Where it happens |
|---|---|
| Scenes, prefabs, materials, VFX, feel-tuning | **You, in the Unity Editor** on your PC. This is the learning |
| `Game.Core` (rules, chemistry tables, grid), its xUnit tests, data import scripts, C# MonoBehaviours, CI workflows | **Claude**, in a cloud session (`dotnet test` runs there), or alongside you |
| Live Editor help (inspect scene, run tests, build) | Claude Code on your PC with the Unity CLI MCP bridge (`unity mcp configure claude`) |

## Open questions (in the order the plan answers them)

1. Which view and focus make the spell chemistry most fun and most
   original? (`PROTOTYPES.md`)
2. Does the chemistry-as-data + grid model feel rich enough, with
   physics only as "feel"? (D4 tests this.)
3. How much of 5E survives contact with physics: to-hit vs. aim, saves
   vs. dodging, push distances vs. impulses?
4. Is platforming a genre or a seasoning? Likely seasoning: traversal
   puzzles using Jump, Levitate and Misty Step between fights.
5. Does the platform add enough to be worth integrating, and how
   (`PLATFORM_FIT.md` options 0–2)?

## Phase 0 — Learn Unity while building the bench

### Step 0: Unity basics (about 1–2 weeks of evenings)

- Install Unity Hub and **6.3 LTS** with a **URP** 3D template.
- Do *Unity Essentials* and the first half of *Junior Programmer*
  (Unity Learn).
- Import Starter Assets (Third Person) and walk around a greybox.
- Learn the Input System and Cinemachine basics.
- Commit the project with a Unity `.gitignore` and Git LFS for binary
  assets.

### Spell Chemistry Sandbox (done; answers Q2 early)

A browser paper prototype of the chemistry (`sandbox/`), running on the
shared rule tables in `rules/`. Play it and log which chains surprise
you and which rules feel missing, before any Unity time goes in.

### Prototypes P1–P4 (answer Q1, Q4)

See **`PROTOTYPES.md`**. Six greybox prototypes share one 8-spell kit:
- P1: 2D side-view
- P2: 3D third-person platformer
- P3: first-person immersive sim
- P4: top-down (optional)
- P5: physical turn-based tactics (`PHYSICAL_TACTICS.md`)
- P6: third-person exploration plus P5 fights (`EXPLORE_AND_FIGHT.md`)

Each is timeboxed to 1–2 weeks and scored on the same rubric. PhysX
and `Rigidbody2D` are authoritative, since everything is single-player
(D4, D5).

### Spike B: `Game.Core` skeleton (answers Q2, in parallel with A; Claude can do this)

- A `netstandard2.1` library, an xUnit test project, and a GitHub
  Actions workflow running `dotnet test`.
- A data-driven chemistry table with elements, tags, element×tag and
  element×element rules, and DOS-style orthogonal modifiers
  (electrified/frozen, blessed/cursed).
- A field grid as an integer cellular automaton (fire spread, liquid,
  oil, ice, gas), with golden tests for each interaction.
- Seeded RNG in state, and a pure `Apply(state, move)`.
- Bring it into Unity as a source folder with an `.asmdef`
  (`noEngineReferences: true`). Every prototype uses it for its chemistry.

### Spike C: Web build, cheap probe (answers part of Q5; optional)

- Export a prototype (P1 or P2) to Web (WebGL2, then WebGPU on 6.6), and record its
  size and load time.
- Host it in an iframe inside a stub `plugin.mjs` on a local platform
  stack.
- Check input capture, fullscreen and phone performance.

## Gate 1 — choose the paradigm

Compare the prototype scorecards (`PROTOTYPES.md`), pick the view and
what spells are *for*, then choose a progression structure on paper.
Record it in `DECISIONS.md` as D6. Before Phase 1, build one structure
slice of the winner: a level, a hub or a region.

## Phase 1 — 5E layer in `Game.Core`

- **Import:** a build script parses SRD 5.2.1 (from the Markdown or the
  Foundry packs) into typed JSON for the subset implemented. Validate it
  against a second statement in the source. The CC-BY attribution goes
  in the README and in the game's credits.
- **Rules:**
  - ability scores and proficiency;
  - d20 tests with advantage and disadvantage;
  - saves and DCs;
  - spell slots and concentration;
  - conditions;
  - seeded dice.
- **Physics-to-5E mapping**, written down as decisions:

  | 5E rule | In the game |
  |---|---|
  | push "10 ft" | an impulse calibrated to about 3 m |
  | fall damage | from the physical fall height |
  | prone | from ice or grease |
  | high ground | gives advantage |
  | shove, jump, throw | cheap verbs, which is BG3's lesson |

- **Slice:** Wizard (Evoker) and Fighter (Champion), levels 1–5, about
  15 spells, about 6 monsters.

## Phase 2 — Vertical slice in the chosen mode

- One dungeon, one boss, and one traversal puzzle that needs two spells
  combined.
- **Turn-based or hybrid:** a turn UI, previews driven by the core,
  initiative, and a camera that frames the action.
- **Action:** lock-on, hit-stop and i-frames come first (the lesson from
  Dark Alliance). Map AC and saves to real time per `RESEARCH.md` §4.
- Build for PC. Try a mobile build once as a reality check.

## Phase 3 — Platform integration (only if Gate 1 and the slice justify it)

- **Option 1:** an ASP.NET Core `plugin-backend/` wrapping `Game.Core`,
  implementing the platform RPC contract, with a port in
  boardgame-plugins and a `registry.ts` entry. The native client handles
  async turns and saves.
- **Option 2:** a Unity web build in an iframe, as the plugin's board.

## Phase 4 — Breadth (only after the slice is fun)

- More classes and the SRD monster pipeline.
- Art direction: whimsical, stylised low-poly, URP toon shading, with
  juicy VFX Graph effects. Use CC0 kits (Kenney, Quaternius) through
  Phase 2.
- Procedural rooms.
- Netcode, only if co-op action is wanted (Mirror or FishNet).

## Learning track (alongside)

| Phase | Learn |
|---|---|
| Step 0 / A | GameObjects and components, prefabs, Rigidbody and colliders, `PhysicsMaterial`, raycasts and overlaps, Input System, Cinemachine |
| B | C# for a TS/Kotlin dev: structs vs. classes, `Span`, records (C# 9), nullable refs; asmdefs; xUnit |
| 1–2 | ScriptableObjects as data, Animator and blend trees (Mixamo rigs), Shader Graph, VFX Graph, UI Toolkit, the Unity Test Framework (EditMode/PlayMode) |
| 3+ | Build pipelines, Addressables, GameCI, profiling on mobile |

## Risks

| Risk | Mitigation |
|---|---|
| Scope: a 3D RPG is the classic over-scoped hobby project | Small spikes, an honest Gate 1, slice before breadth |
| Physics chaos vs. tactical predictability | Previews from the core; physics only where it's the joke or the point |
| Wanting multiplayer physics later | D4 already keeps shared truth out of PhysX; netcode is a Phase 4 add-on |
| Unity churn (CoreCLR, Unity 7 in 2027) | Core at C# 9 or below, on netstandard2.1; stay on LTS |
| Art cost | Greybox through Phase 2; CC0 kits; stylised URP |
| Licence slip (using "D&D" or non-SRD names) | A name check in CI; only SRD 5.2.1-sourced text |
