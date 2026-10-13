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

## Still open

- Combat mode: turn-based, action, or hybrid (`PLAN.md` Gate 1).
- Whether, and how deeply, to integrate with web-boardgame-platform
  (`PLATFORM_FIT.md`).
- Name. `5e-compatible-game` is the repo name, not a title.
