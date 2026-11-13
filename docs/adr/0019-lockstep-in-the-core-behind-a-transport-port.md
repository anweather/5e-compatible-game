# D19. Lockstep in the core, behind a transport port (2026-11-12)

**Status:** Proposed

- **Context:** The chemistry is deterministic and our game already plays in lockstep over NGO (D7). But about 600-700
  lines of the lockstep logic live in the view's NGO layer (`Net/NetChem*.cs`, `CommandChannel.cs`):
  - tick stamping;
  - history;
  - catch-up;
  - hash windows;
  - joiners.

  The core is now a package (`chemistry-core-0.1.0`), and a second game (Punchy, Netcode for Entities) runs it. Andrew
  wants friends to play together in both.
- **Decision:** move the transport-free lockstep logic into the core as `Chem.Lockstep`:
  - a `LockstepSession` with host, client and solo roles;
  - a deterministic wire codec;
  - late join and desync recovery by `JoinSnapshot`;
  - game commands registered as named channels.

  It talks to the world only through ports: `ILockstepTransport` (reliable ordered bytes: to the host, to a peer, to
  everyone; peer joined and left) and the host's `Update(elapsed, over)`. Adapters stay outside the core: NGO in our
  game, NfE in Punchy, and a loopback in the core for solo play and tests. D7's choices stand (NGO, client-hosted,
  lockstep); this decision moves where the logic lives.
- **Why:**
  - One implementation of the protocol for every host game.
  - Two-peer, late-join and desync tests run under `dotnet test`, without the Editor.
  - The core stays engine-free (D3, D9).
  - Snapshot join is the only option for a world filled from another game's terrain.
  - It finally gives us desync recovery.
- **Reopen if:**
  - a host game needs server authority over the chemistry rather than lockstep;
  - the snapshot size makes joining too slow;
  - IL2CPP and Mono peers disagree on the doubles in the sim.
- **Sources:** `docs/design/core-package/multiplayer-ports.md`. The 2026-11-12 maps of `Scripts/Net` and of Punchy's
  NfE (`Net/GameBootstrap.cs`, `Items/Items.cs`, `docs/agents/multiplayer.md`).
