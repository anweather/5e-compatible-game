# Physics-magic 5E RPG — feasibility notes

Working idea: a 3D RPG on the open D&D 5E rules (SRD), built partly to
learn 3D, whose centrepiece is **deep, physics-based, whimsical
interactions between spells and abilities**. Combat mode (turn-based vs.
action) and genre mix (platforming?) are still open.

No code yet. This folder is the knowledge base:

| File | What it is |
|---|---|
| `README.md` | This page: the verdict and the shape of the answer |
| `docs/RESEARCH.md` | What is true, with sources: license, SRD contents, data sets, prior games, systemic-magic patterns, engines, determinism |
| `docs/PLATFORM_FIT.md` | How (and whether) it fits web-boardgame-platform, from reading the code |
| `docs/PLAN.md` | Spikes and phases to go deeper, with the decisions each one unlocks |

## Verdict

**Feasible, and a good learning project, if the physics/magic sandbox
comes first and the combat mode is decided by a playable bench, not up
front.**

1. **Rules/licence: clear.** SRD 5.2.1 (the 2024 rules) is CC-BY-4.0,
   irrevocable, with 12 classes, ~339 spells and ~330 monsters. You can
   ship a commercial or free game on it with one attribution paragraph.
   You cannot say "Dungeons & Dragons", or use Beholders, Mind Flayers or
   the Forgotten Realms. Market it as "5E-compatible".
2. **The SRD is full of physical spells.** Thunderwave, Grease, Gust of
   Wind, Levitate, Misty Step, Web, Telekinesis, Wall of Ice/Stone/Force,
   Reverse Gravity, Control Water, and Elementalism (the 2024 cantrip
   that replaces Shape Water/Mold Earth/Gust) are all in it. That is
   enough raw material for the whole idea.
3. **The interaction design problem is solved in principle.** Zelda
   BotW's "chemistry engine", Divinity OS2 surfaces and Magicka all avoid
   an N×N rule explosion the same way. Tag materials, write rules as
   *element × material* and *element × element* data tables, and let
   physics handle motion separately from chemistry (state). This matches
   how you already build engines: pure core, rules as JSON.
4. **Engine: TypeScript + Three.js (or react-three-fiber) + Rapier** is
   the best fit for you specifically. Rapier's JS/WASM build is
   **deterministic across machines**, so the same physics can run in
   the browser *and* inside a Node plugin backend's `ApplyMove`. No other
   option gives that. Godot 4.6 is the pick only if "learn a Unity-style
   editor" outweighs the platform. Unity has awkward web builds and uses C#.
5. **Platform fit depends entirely on the combat mode:**
   - *Turn-based*: fits cleanly. Each move runs a deterministic physics
     resolve server-side (step until the scene settles) and stores the
     settled state. Hosted live rooms (already built, used by Pocket
     Tactics co-op) cover presence and co-op.
   - *Real-time action / platforming*: doesn't fit the Move model.
     Live channels are JSON-only, ≤30 signals/s and have no prediction
     or rollback. It would be a mostly-standalone game that uses the
     platform for identity, saves and Spaces, with client-authoritative
     play and Moves as checkpoints.

   Either way, per boardgame-plugins' own design rule ("a big,
   standalone game gets its own repo"), this is a new repo like
   tactics-game-plugin, not a directory in boardgame-plugins.

## The one architectural idea that keeps the mode open

Build the simulation as a **pure core with two clocks**:

```
            ┌───────────── sim core (pure TS) ─────────────┐
 intent ──▶ │ 5E resolver (d20, saves, slots, conditions)  │
            │ chemistry: element×tag rules (JSON)          │
            │ field grid: fire / water / oil / ice / gas   │
            │ Rapier world: bodies, impulses, joints       │
            └──────────────────────────────────────────────┘
                 ▲                              ▲
   TurnClock: step until settled      RealtimeClock: step every frame
   (plugin ApplyMove, previews)       (action / platforming bench)
```

The spells, rules tables and physics are identical under both clocks.
Choosing the mode is choosing which clock drives the sim, plus UI and
netcode. That is why the plan starts with a real-time bench (most
fun-revealing, fastest feedback) without committing to real-time
combat.
