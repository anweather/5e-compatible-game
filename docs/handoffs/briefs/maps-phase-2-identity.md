Brief from the PM to the Mac: maps phase 2, Identity (#69). Frugal: one Unity job at a time, no sub-agents, no clips, a report of a few lines.

Build `docs/design/maps-as-data.md` section 2 on branch `maps-identity` off dev (5638da4d or later): the map id and hash, approval refusing a peer without the host's map, the map as a `NetworkVariable` read before `grid.ResetWorld()`, and `LandHash` in `Others`, "" until land is stamped. The host picks the map (J-073), the valley by default. No golden moves; say so if one must.

The Mac's open Editor is stuck on a dialog until Andrew clicks it: use a batch run in a clone, as in phase 1. Leave a build and exact steps for one real mismatched two-peer run: the PC does that run, so say what it must show. Don't start phase 3.

Report by real exit code: `dotnet build core -c Release -warnaserror`, `dotnet test core`, format, jscpd, EditMode, and anything left. Merge dev before pushing. Push `maps-identity` only.
