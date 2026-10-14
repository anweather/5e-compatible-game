# 5e-compatible-game

Working idea: a 3D RPG in **Unity** on the open 5E rules (SRD 5.2.1),
built partly to learn Unity, whose centrepiece is **deep, physics-based,
whimsical interactions between spells and abilities**.

- **Targets:** PC first. Native mobile and web are interesting but
  secondary.
- **Real-time**, plus one turn-based candidate, *physical tactics*
  (`docs/PHYSICAL_TACTICS.md`). No grid tactics (D5).
- **Still open:** the paradigm: 2D vs. 3D, first or third person,
  platformer vs. action vs. immersive sim vs. physical tactics vs.
  explore-plus-fight, and levels vs. hub vs.
  Metroid-style vs. open world. This is being answered by the
  prototypes in `docs/PROTOTYPES.md`.

The first runnable piece is the **Spell Chemistry Sandbox** (`sandbox/`): a
browser paper prototype of the spell interactions, driven by the rule
tables in `rules/` that the Unity build will load too.

| Folder | What it is |
|---|---|
| `rules/` | The interaction rules as JSON: elements, materials, reactions, spells, critters, machines |
| `sandbox/` | A top-down web sandbox that runs those rules, with tests (`npm test`) |

The docs:

| File | What it is |
|---|---|
| `README.md` | This page: the verdict and the shape of the answer |
| `docs/PROTOTYPES.md` | **Current phase:** the prototypes, the shared spell kit, the progression options and the scorecard |
| `docs/PHYSICAL_TACTICS.md` | Prototype P5's concept: turn-based play resolved physically, with reactions and no health sponges |
| `docs/EXPLORE_AND_FIGHT.md` | Prototype P6's concept: third-person adventure movement in exploration, physical turn-based fights in place, battlefield prep between them |
| `docs/TRAVERSAL.md` | Unique per-character traversal (by physical channel), collectible critters that change world state, machines that consume it |
| `docs/DECISIONS.md` | What has been decided, and why |
| `docs/RESEARCH.md` | What is true, with sources: licence, SRD contents, data sets, prior games, systemic-magic patterns, Unity, physics determinism |
| `docs/PLATFORM_FIT.md` | How (and whether) it fits web-boardgame-platform, from reading the code |
| `docs/PLAN.md` | Spikes and phases to go deeper, with the decisions each one unlocks |

## Verdict

**Feasible, and a good way to learn Unity, if the physics/magic sandbox
comes first and the paradigm is decided by playable prototypes, not up
front.**

1. **Rules/licence: clear.**
   - SRD 5.2.1 (the 2024 rules) is CC-BY-4.0 and irrevocable: 12
     classes, ~339 spells, ~330 monsters.
   - You can ship a game on it with one attribution paragraph.
   - You can't use "Dungeons & Dragons", Beholders, Mind Flayers or the
     Forgotten Realms. Say "5E-compatible".
2. **The SRD is full of physical spells:** Thunderwave, Grease, Gust of
   Wind, Levitate, Misty Step, Web, Telekinesis, Wall of
   Ice/Stone/Force, Reverse Gravity, Control Water, and Elementalism.
3. **The interaction design is a solved pattern.** BotW's chemistry
   engine, Divinity OS2 surfaces and Magicka all work the same way:
   - tag materials;
   - write the rules as *element × material* and *element × element*
     data tables;
   - keep chemistry (state) separate from physics (motion).
4. **Unity is a good fit** for PC-first.
   - **Version:** Unity 6.3 LTS with URP. The Personal tier is free
     under $200k a year, with an optional splash screen.
   - **The web is its weak target:** builds of tens of MB, and it is
     best embedded in an iframe.
5. **One hard constraint shapes the architecture.**
   - None of Unity's physics engines produce the same result on two
     different machines.
     - PhysX's "enhanced determinism" is same-machine only.
     - DOTS Unity Physics is same-architecture only.
     - Havok is now a paid add-on.
   - So **physics can't be the shared truth** between a server and a
     client, or between two players.
   - That is fine single-player, where PhysX can simply be authoritative.
   - For anything multiplayer or server-checked, the truth has to live
     in deterministic, engine-free C# (rules plus a grid). Physics then
     becomes presentation, or its results get snapshotted into state.
6. **Platform fit is a choice, not a given.** See `docs/PLATFORM_FIT.md`.
   The cheap, natural link is the platform as account, save and
   async-turn backend for a native client, plus an iframe web build.
   Shadow-DOM mounting is unverified and risky.

## The architecture that keeps every option open

```
 ┌──────────── Game.Core  (engine-free C#, netstandard2.1) ────────────┐
 │ 5E resolver: d20 tests, saves, slots, concentration, conditions     │
 │ chemistry: element × tag tables (JSON data)                         │
 │ field grid: fire / water / oil / ice / gas  (integer / fixed-point) │
 │ Apply(state, move) → state   · seeded RNG carried in state          │
 └──────────────▲───────────────────────────────────▲──────────────────┘
                │ same DLL                          │ same DLL
 ┌──────────────┴──────────────┐      ┌─────────────┴────────────────────┐
 │ Unity client (PC/mobile/web)│      │ ASP.NET Core plugin backend      │
 │ URP rendering, VFX, input,  │      │ (optional, later): platform RPC  │
 │ PhysX for bodies, ragdolls, │      │ InitializeGame / ValidateMove /  │
 │ debris — "feel" layer       │      │ ApplyMove over the same core     │
 └─────────────────────────────┘      └──────────────────────────────────┘
```

`Game.Core` uses an `.asmdef` with `noEngineReferences: true`, so the
compiler enforces the engine-free boundary. It is tested with `dotnet
test` outside Unity. That is also the part an AI agent in a cloud
container (no Unity Editor) can build and test end to end.
