# Fit with web-boardgame-platform

This was read from the code on 2026-10-13. Paths are relative to each
repo.

## What the platform gives a plugin

- **The Move model.** A plugin implements `InitializeGame`,
  `ValidateMove`, `ApplyMove`, `GetPlayerView`, `SelectBotMove`, and so
  on (`plugin-protocol/model/game-plugin.smithy`).
  - Each move is durable and logged, and runs under a per-game lock.
  - Each move costs three sequential RPCs carrying the full
    `pluginState`, which is stored as one JSON row.
  - Measured cost is **~150–300 ms of server time per move**
    (`LIVE_CHANNELS_DESIGN.md` §1.1).
  - Clients learn about changes from an SSE "game updated" ping, then
    refetch.
- **Frontend mounting.** `ShadowMount.tsx` dynamic-imports
  `/plugins/<type>/plugin.mjs` into a shadow root. There is no iframe.
  - WebGL, pointer lock, fullscreen and keyboard all work.
  - The registry has the layout flags `fullScreenBoard`,
    `hostWidth: 'full'` and `hostOverflowX`. Pocket Tactics already uses
    all three.
  - The bundle is a single ES module. Big 3D assets have to be
    lazy-loaded from the plugin's own `/assets`. There's no CDN and no
    size budget today.
- **Live channels** are implemented, despite the design doc's header
  saying otherwise (`platform-rest/.../rest/live/`,
  `plugin-protocol/live/PROTOCOL.md`).
  - *Hosted* mode means the plugin's Node process is the authoritative
    room.
  - Scope is a game or a **Space** (a shared world with owner,
    membership and invites, whose content lives in plugin blobs).
  - Limits: JSON only, ≤16 KiB frames, **≤30 signals/s**, 32 members.
  - There's no prediction or rollback, and WebRTC was rejected.
- **Saves**: 3 slots per player per game type, plus a compare-and-set
  blob store.

## What Pocket Tactics already proves (and could donate)

- **Running a live world on the platform:**
  - co-op battles that stay turn-based, where the room re-validates
    each turn with the same core code plus an engine hash;
  - roaming enemies simulated at 200 ms steps;
  - party positions at ≤5/s;
  - Spaces as shared worlds.

  That is exactly the infrastructure a co-op D&D party needs.
- **Pieces worth copying as patterns, not code:**
  - abilities as JSON (`data/abilities.json`, with fields such as
    `element`, `terrainEffect`, `terrainSynergy` and `displaces`);
  - `SeededRandom`;
  - serializable commands, which give undo and replay;
  - the ports/adapters split, where `src/core` has no renderer.
- **What it doesn't have:** a general element-combo engine, continuous
  space, physics, or any d20 rules. Those are the new work. Its core is
  FFT-style, so expect to write a fresh 5E core rather than bend that
  one.

## Integration options with a Unity client

> Integration is not a priority while prototyping. For P1–P4
> (real-time), only options 0 and 1 (accounts and saves) are
> realistic. If P5 (physical tactics) wins, its preview-then-replay
> recording can be the Move, and option 1 with async turns comes back
> into play (D4, D5).

Updated for D1 (Unity) and D4 (physics isn't shared truth).

| Option | How | Cost | Verdict |
|---|---|---|---|
| **0. No integration** | Standalone PC game | None | Fine for the first months. The platform adds nothing to a single-player physics bench |
| **1. Platform as backend for a native client** | An ASP.NET Core plugin backend wraps `Game.Core` and implements the RPC contract (nothing requires a JVM; the Node SSDK and the Smithy model show the wire shape). The native Unity client calls the platform's HTTP API for account, saves, Spaces and async turns. The platform's web board shows a light 2D or summary view | A C# implementation of the RPC layer (no C# SSDK exists today); auth from a native app | **Natural fit** for async co-op or turn-based play |
| **2. Unity web build as the plugin's board** | The plugin's `plugin.mjs` creates an **iframe** inside its shadow root, which loads the Unity web build from the plugin's `/assets`. A `postMessage` bridge carries `gameView` in and `onMove` out (jslib ⇄ `SendMessage`) | Tens of MB to download; mobile-browser performance; no managed threads | Possible, but late. Do it after the game is fun. Avoid mounting `createUnityInstance` directly in the shadow root (undocumented, page-global input and memory) |
| **3. Real-time multiplayer through the platform** | Live channels: JSON only, ≤30 signals/s, no prediction | Doesn't fit action netcode | **No.** Real-time multiplayer would use Unity netcode (NGO, Mirror, FishNet) with its own server |

## Fit by combat mode (Unity version)

| Mode | Fit | How |
|---|---|---|
| **Turn-based** | **Good** | `Game.Core` `Apply(state, move)` is the ApplyMove. Physics is presentation, or quantised into the move (D4). Previews run the same core function |
| **Action / platforming** | Standalone | PhysX is authoritative locally. The platform is at most a save/account backend (option 1, without async turns) |
| **Hybrid** (real-time exploration, turn-based fights) | **Good** | Exploration is local. Fights are core-authoritative turns, which is the split Pocket Tactics already proved on the platform |

## Where it lives

`anweather/5e-compatible-game`, its own repo, following
boardgame-plugins' rule that "a big standalone game gets its own repo".
If option 1 happens, a `plugin-backend/` (ASP.NET Core) would sit next
to the Unity project, with a port claimed in boardgame-plugins' registry
and a `registry.ts` entry on the platform side.
