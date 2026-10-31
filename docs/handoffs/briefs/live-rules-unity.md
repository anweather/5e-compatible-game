# Brief: live rule changes in the game (the Unity half)

For DesktopUnity, 2026-10-31. Andrew picked this as the PC's work while it waits on 0.1.10. The design
is `docs/RULES_MCP.md` ("Change modes", "Where it runs", phase 3); the core half landed with
`live-rules` (`Room.cs`, `World.Room.cs`, `RoomCommand`, `RoomPolicy.Refusal`, the MCP's `apply`,
`revert`, `room`). Read "Still to do, Unity half" there; this brief is that list.

**Branch:** `live-rules-unity` off dev. Lands after 0.1.10 is cut, so no rush to merge.

## Build

1. **Room creation menu:** the change mode (Open / Host only / Locked), default Open, calling
   `World.OpenRoom`. Solo play opens an Open room.
2. **`RoomIntent` in `Net/`:** client to host. The host may refuse early with
   `RoomPolicy.Refusal`, then stamps the `RoomCommand` into a tick batch and the late joiner's
   history. Every peer still decides at that tick (the core does it).
3. **The game serves the rules MCP on localhost** against its own live world, not the one in memory,
   so a player adds it to their Claude once. Reuse `core/Rules.Mcp`'s tools; `apply` and `revert`
   go out as `RoomIntent`. Say how a player connects (one line in the README or the HUD's help).
4. **HUD line** per change from the world log ("Ana switched on Big fireball (Open room)"; refusals
   too).
5. **The mods panel (M)** goes through the room when one is open, not `RuleSwitcher`.

## Prove it

- Online probes (in a new probe file, so they don't collide with `wip/punch-strike`'s
  `ProbeScenarios.cs` edits): in an Open room the client switches on Big fireball and both peers see
  it at the same tick with matching checksums; in Host only the client's change is refused with the
  sentence on both; Locked refuses everything; revert; a late joiner gets the mods in force.
- A real Claude, through the localhost MCP, switches on a mod in a running game.
- **For Andrew's board:** one short clip: Claude asks for a bigger fireball, the HUD line appears, the
  next fireball is bigger on both screens. Plus a still of the room menu.

## Rules of the road

- The punch online run and the 0.1.10 RC check come first when the PM asks: stop, run those, resume.
- Free the Editor and report when you stop; report after each step above.
- Don't touch `ItemHolder*`, the punch probes, or the cast sound (the Mac's).
- Anything Andrew must judge goes on his board as an image or clip.
