Brief from the PM to the Mac: maps phase 1, Folder (Andrew's maps work, #69). Frugal: one Unity job at a time, no sub-agents, no clips, a report of a few lines.

Build phase 1 of `docs/design/maps-as-data.md` ("Order of work", section 1 and `find_site` in section 4) on branch `maps-folder` off dev: `world/` to `world/maps/valley/`, `map.json`, `GameFolders` taking the map, `cells.txt`, the recursive `Pack`, `MapCheck`, `find_site`. The six questions for Andrew are open, but none should touch this phase: if one does, stop and say so.

The valley must be unchanged: no golden moves, stable thing ids, dev playable. Say which of the `world/` paths in docs and `CLAUDE.md` you updated. A player build must still find its data: `Pack` is what `second-map` couldn't do. Don't start phase 2.

Don't merge dev's `world/` changes by hand: tell the PM before pushing, so the move lands as one quick merge and the PC can be told. Report the commit, `dotnet build core -c Release -warnaserror` by its real exit code, `dotnet test core`, format, jscpd, EditMode, `tools/probe.sh smoke`, and anything left. Push `maps-folder` only.
