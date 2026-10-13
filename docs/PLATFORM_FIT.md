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

## Fit by combat mode

| Mode | Fit | How |
|---|---|---|
| **Turn-based (BG3/Solasta-like)** | **Clean** | `ApplyMove` runs deterministic Rapier until settled (budget: well under the 150–300 ms the platform already spends). The client replays the same steps for animation. Previews run the same function (triple-triad's `preview.ts` pattern). Co-op and presence go through a hosted live room; persistent campaigns through Spaces. |
| **Real-time-with-pause** | Awkward | Too chatty for Moves, too authoritative for 30 signals/s. Avoid. |
| **Action / platforming** | **Poor as a platform game** | The client simulates locally. The platform supplies identity, saves, Spaces and maybe low-rate presence. Moves become checkpoints ("cleared room 3", loot), not gameplay. Multiplayer action would need new netcode the platform deliberately didn't build. |

**Hybrid that fits well.** Real-time *exploration and platforming*
runs client-side (single-player or ghosted co-op, like tactics'
overworld), and **turn-based combat** runs as server-authoritative
Moves. That is the same split Pocket Tactics landed on: live overworld,
turn-based battles. Spells use one sim core under both clocks.

## Where it lives

Its own repo, by boardgame-plugins' design rule ("a big, standalone
game with its own asset pipeline gets its own repo", as with
tactics-game-plugin). It would have the same two-runtime shape as
tactics:
- a standalone dev app or test bench;
- `plugin-backend/` as the platform runtime.

A port and a `registry.ts` entry come only once it is a plugin.
