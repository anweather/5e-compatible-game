# D9. ECS as a pattern in the core, not Unity Entities (2026-10-25)

**Status:** Accepted

- **Context:** a day into the Unity prototype, switching to Unity's
  Entities (DOTS) would be as cheap as it will ever be. The question was
  whether effects between things (fire on a barrel, a hammer on ice) and
  behaviours attached to them should move to an Entity Component System
  now, before more is built on GameObjects.
- **Decision:**
  - **No Unity Entities, Netcode for Entities or Unity Physics for the
    game now.** The view stays GameObjects; networking stays Netcode for
    GameObjects carrying intents in lockstep (`NetChem`; D4 and D7).
  - **The Chem core follows the ECS pattern:** state is plain data,
    behaviour is ordered systems over it (`World.Sim.cs`,
    `World.React.cs`), and what things do is data (`rules/`).
  - **Keep the core ECS-ready.** New core code follows the seams below,
    so a later move of hot loops to Burst jobs, or of the core to Entities,
    replaces storage and scheduling, not the game's logic.
  - Entities may be used for view-only crowds (grass, debris, particles)
    that never affect the shared world.
- **ECS-ready seams** (the rules for new core code; existing code moves
  toward them when it is touched):
  1. **Data and systems apart.** No behaviour on state classes; a system
     is a pass over state with a fixed place in the tick order, and its
     comment says what it reads and writes.
  2. **Integer ids in hot state.** Terrain, cover, gas and so on will be
     rule ids interned to small integers at load (`RuleTables`), not
     strings in each cell. Strings stay in the rules and the log. Not done
     yet: cells still hold strings (`Cell.T`, `Cover`, `Gas`); the step is
     in `BACKLOG.md` §10.
  3. **Fields, not objects, per cell.** Reach cells through accessors
     (`World.At` and friends), not by holding `Cell` objects, so a chunk
     can become one array per field (struct of arrays) behind them.
  4. **Components as tags in the rules.** Entities and materials carry
     tags (`flammable`, `conducts`, `brittle`, `floats`...) and reactions
     match on tags, not on ids.
  5. **Commands are the only way in.** Everything that changes shared
     state is an intent or `EffectCommand` applied at a tick boundary, as
     today (D4).
  6. **The view is an adapter.** MonoBehaviours read the core's state
     and send commands; they hold no rules logic, like an Entities
     authoring and presentation layer.
  7. **Deterministic by construction:** integer or fixed-point maths in
     the core, the world's seeded RNG, no unordered iteration (already
     required by D3).
- **Why:**
  - **Our network model is lockstep, which Unity's ECS doesn't serve.**
    Netcode for Entities is server-authoritative snapshots with client
    prediction, built for shooters. Developers report it breaks down for
    physics-heavy, reactive worlds ("N4E breaks down once the environment
    simulation reaches a certain threshold of complexity"), which is what
    a chemistry world is.
  - **Lockstep is the proven model for big reactive worlds.** Factorio
    sends only inputs and runs every tick identically on every machine;
    its developers say that's what lets it handle hundreds of thousands
    of active entities online. Our cast intents work the same way.
  - **Burst and Unity Physics aren't deterministic across CPU
    architectures**, only on the same one. A Mac (Apple silicon) and a
    Windows PC playing together are two architectures, which breaks
    lockstep with floating-point maths.
  - **Shipped prior art is thin and custom.** V Rising (about 1,600 ECS
    systems, 60-player servers) shipped on DOTS but its netcode isn't
    public. No shipped commercial game was found that names Netcode for
    Entities; the large examples are Unity's samples (Megacity Metro).
    The shipped deterministic ECS for multiplayer is Photon Quantum
    (Stumble Guys, LEGO Brawls), its own framework with fixed-point maths
    and rollback.
  - **Unity is bringing ECS to GameObjects.** All ECS packages are core
    packages from Unity 6.6; "ECS for All" makes every GameObject an
    entity (expected around Unity 7), and Netcode for Entities is getting
    a GameObject layer. Staying on GameObjects doesn't strand the project.
  - Rewriting the view, Starter Assets, Animator, Cinemachine and the
    packs for Entities would cost more than anything it buys at four
    players.
- **Reopen if:**
  - the D4 trigger: competitive multiplayer that needs server-side
    prediction;
  - thousands of networked physics objects, or networked NPC crowds the
    lockstep core can't carry;
  - profiling shows the core tick is the bottleneck and Burst jobs over
    the seams above aren't enough; or
  - Unity 7's GameObject-entities make the switch nearly free.
- **Sources:** the research of 2026-10-25: Unity's V Rising case study;
  the Netcode for Entities forums (co-op suitability, April 2026 review,
  prediction with GameObjects); the "2026 ECS for all" thread and roadmap;
  Burst determinism threads; Photon Quantum; Factorio Friday Facts #76
  and #188.
