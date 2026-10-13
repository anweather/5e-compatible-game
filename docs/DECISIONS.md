# Decisions

What this project has *decided*, as distinct from what is *true*
(`RESEARCH.md`). Each entry says why, and what would reopen it.

## D1. Engine: Unity (2026-10-13)

- **Decision:** Unity. Start on **6.3 LTS** (supported to Dec 2027) with
  **URP**.
- **Why:** learning Unity is a goal in itself, not a means. URP suits a
  stylised, whimsical look, runs on every target, and is where Unity 7
  is consolidating.
- **What this supersedes:** the earlier TS/Three.js/Rapier recommendation
  in `RESEARCH.md` §8. That was optimised for the web platform, not for
  learning Unity.
- **Upgrade path:** 6.6 if WebGPU on the web matters. Plan a move to
  6.7 LTS or Unity 7 in 2027 (CoreCLR, .NET 10).
- **Reopen if:** never, for the engine itself. Revisit the version
  yearly.

## D2. Targets: PC first; mobile and web secondary (2026-10-13)

- **Decision:** PC (Windows/macOS/Linux) is the first-class target.
  Native mobile and a web build are kept *possible* but don't constrain
  early work.
- **Why:** PC has the best Unity tooling, the most performance headroom
  for a physics sandbox, and no download-size pressure.
- **What it costs later:** the web build is the weak target (tens of
  MB, no managed C# threads, needs an iframe). Mobile needs a
  touch-controls and performance pass.

## D3. Rules live in an engine-free C# core (2026-10-13)

- **Decision:** the 5E rules, the chemistry tables and the field grid
  are a `Game.Core` library:
  - `netstandard2.1`, using C# 9 features or below (that is what Unity
    supports until CoreCLR lands);
  - a Unity `.asmdef` with `noEngineReferences: true`;
  - xUnit tests run under `dotnet test`.
- **Why:**
  - It keeps one rules implementation that can serve the Unity client,
    a headless Unity server, or an ASP.NET Core platform plugin.
  - It can be tested fast, without the Editor.
  - It is the same "pure core, rules as data" shape as the platform's
    plugins and Pocket Tactics.
- **Rules for the core:**
  - no `UnityEngine`;
  - no mutable statics (Unity 6.6 skips domain reload by default, so
    statics leak between runs);
  - all randomness from a seeded PRNG stored in state;
  - integer or fixed-point maths for anything that must agree across
    machines.

## D4. Physics is authoritative only where nobody has to agree (2026-10-13)

- **Decision:**
  - **Single-player:** Unity PhysX may decide outcomes (barrels roll,
    enemies slide off ledges).
  - **Shared or server-checked outcomes** (multiplayer, async turns on
    the platform): the truth is `Game.Core`. Physics either:
    - animates outcomes the core already decided, or
    - is run once by the acting client, and its result is **quantised
      into the move** ("barrel came to rest in cell (4,7)"). The server
      then validates plausibility rather than re-simulating.
- **Why:** no Unity physics engine is deterministic across machines
  (see `RESEARCH.md` §9). Chasing fixed-point physics is a research
  project, not a learning project.
- **Reopen if:** the game turns out to need competitive multiplayer
  physics. That would mean evaluating a fixed-point engine, or Netcode
  for Entities prediction on a dedicated server.

## D5. No grid tactics; turn-based only in physical form (2026-10-13, amended same day)

- **Decision:**
  - P1–P4 are real-time: action, platforming, exploration.
  - Turn-based is in **only** as P5, physical tactics
    (`PHYSICAL_TACTICS.md`): continuous 3D space, intents resolved
    physically, reactions, flat HP.
  - Grid or FFT-style tactics is out.
- **Amended because** the physical, reaction-driven, low-HP version is
  a different game from Pocket Tactics. The original "real-time only"
  reason holds only for the grid version.
- **Why:** Pocket Tactics already covers turn-based tactics on the
  platform, and that genre is crowded. The interesting, less-trodden
  space is real-time 5E spell chemistry.
- **Consequences:**
  - Single-player PhysX is authoritative everywhere (D4's
    "nobody has to agree" case). P5 adds preview-then-replay:
    simulate in a sandbox `PhysicsScene`, then replay the recording on
    commit. That would also make P5 async-multiplayer-able later.
  - Platform integration drops to option 0 (standalone) or option 1
    for accounts and saves only (`PLATFORM_FIT.md`).
  - D3's core library is still worth it, for a different reason: one
    chemistry rule set shared by the 2D (`Rigidbody2D`) and 3D (PhysX)
    prototypes, which are separate physics systems in Unity.
- **Reopen if:** a prototype shows that the chemistry shines most when
  you can plan it out.

## Still open

- Paradigm: view, what spells are for, and progression structure
  (`PROTOTYPES.md`), to be decided as D6 after the prototypes.
- Whether, and how deeply, to integrate with web-boardgame-platform
  (`PLATFORM_FIT.md`).
- Locked vs. flexible abilities (class-locked, learnable, or a hybrid).
  The data model supports all three (`TRAVERSAL.md`).
- Critter acquisition: befriend or bond, not capture-device, pending
  a patent check (`TRAVERSAL.md`).
- Name. `5e-compatible-game` is the repo name, not a title.
