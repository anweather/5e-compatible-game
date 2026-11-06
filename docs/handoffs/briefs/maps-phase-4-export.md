Brief from the PM to the Mac: maps phase 4, Export (#69). Andrew's go, 2026-11-04. Frugal: one Unity job at a time, no sub-agents, no clips, a report of a few lines.

Build phase 4 of `docs/design/maps-as-data.md` ("Order of work", and section 1 for `ground/`, section 5 for the checks) on branch `maps-export` off dev (8e295cfc or later): the valley's exported `ground/`, its tests, and the `fell` and `Bind` checks. One bake on the Mac is fine: the PC's is no better. The baked terrains stay in use: the builder is phase 5. Say how big `ground/` is, and whether it belongs in git, before pushing it if it's over about 20 MB. The map hash covers `ground/`, so say what that does to a peer on an older copy.

The valley stays unchanged; no golden moves, or say why. If the Mac's Editor is still stuck, use a batch clone as before. Don't start phase 3 or 5.

Report by real exit code: `dotnet build core -c Release -warnaserror`, `dotnet test core`, format, jscpd, EditMode, and anything left. Merge dev before pushing. Push `maps-export` only.
