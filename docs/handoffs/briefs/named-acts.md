Brief from the PM to the Mac: named acts (backlog 30, `docs/design/world-acts.md` "Next"). Andrew's order: straight after land stamps. Frugal: one Unity job at a time, no sub-agents, no clips, a report of a few lines.

Build on branch `named-acts` off `origin/maps-stamps` (it isn't landed yet: the PC is checking it; if it needs a fix, that comes first). Merge dev once it lands.

A named act is a one-off defined once and fired again by name at a place: "my fireworks", a campsite, a watchtower hill. The design's shape: a trigger with `on: "called"`, and a tool that fires it at a place. Read `Rules.Triggers.cs`, `World.Triggers.cs` and the `holed` trigger first, and say if that shape doesn't fit. Reuse `act`'s `ask` JSON, so anything `act` can do, land stamps included, can be in one. It should go through the room's change mode as `act` does, every peer at the same tick, and `undo_act` should take back one firing whole where the parts can be undone. Primitives only: no firework or campsite in code (J-055).

Say where a named act lives: a mod that's on is my assumption, so a saved mod carries it. No golden moves, or say why.

Report by real exit code: `dotnet build core -c Release -warnaserror`, `dotnet test core`, format, jscpd, EditMode, and anything left. Push `named-acts` only.
