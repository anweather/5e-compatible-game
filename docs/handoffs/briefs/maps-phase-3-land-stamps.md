Brief from the PM to the Mac: maps phase 3, land stamps live (#69). Andrew's go, 2026-11-04; named acts come straight after, so keep the seams clean for them. Frugal: one Unity job at a time, no sub-agents, no clips, a report of a few lines.

Build `docs/design/maps-as-data.md` section 3 on branch `maps-stamps` off dev: the stamp row, `act`, `undo_act`, the third term in `CellHeightfield`, `budgets`, save, reset. Merge `origin/maps-export` when it lands; it's waiting on the PC and doesn't touch `act`.

Andrew's answers (J-073) bind this: a stamp is at most radius 32 cells, one at a time; no stamps across water and no live water level; a mountain stops nothing yet, so raiders, sight and spells pass as they do now; the dragon's reset undoes stamps. A stamp is a shape applied to height, a primitive: no mountain tool, no tool named after a landform. Lockstep: stamps are commands, integer maths, the world's order; `LandHash` moves only once land is stamped. No golden moves, or say why.

A late joiner and a save must keep the stamps, and an undo must put the ground back for every peer. The ground that's drawn and the cells must stay one thing: say how you checked that.

If the Mac's Editor is still stuck, use a batch clone and say what that leaves unproven. Leave exact steps for the PC: the cost per stamp at radius 32 on the GPU, and one real two-peer run with a stamp, an undo and a late join. Don't start named acts, phase 5 or `keep_land`.

Report by real exit code: `dotnet build core -c Release -warnaserror`, `dotnet test core`, format, jscpd, EditMode, and anything left. Merge dev before pushing. Push `maps-stamps` only.
