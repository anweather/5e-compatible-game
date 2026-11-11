Needs: unity
Area: unity/Assets/Prototype/Scripts/ (ChemGrid.MapWorld's wrap, NetChem's StandKeeper, the camera and origin re-centre, DetailChunks, FarRing's known meshes and landmarks, view distances), docs/design/world-12x8-scope.md (a new section only)
Branch: seam-view
After: generate-switch (u2), for a wrapped map that loads; items 1-2 and the camera can start before

# The view across the east-west seam (the 12 x 10)

The core wraps (sim-across-the-seam, 802f38e7). u2's list for the view (`docs/design/world-12x8-scope.md`, "What the
view needs"): (1) `ChemGrid.MapWorld` passes `wrapX: MapFile.WrapX` (~1 h); (2) `NetChem`'s StandKeeper takes
`grid.World.DeltaX` (one line); (3) the camera and the origin re-centre across the seam (2-3 days): positions stay
continuous in the view, the origin re-centres on the player, each chunk is laid at its copy nearest the eye
(`DeltaChunkX`); (4) `DetailChunks` passes `wrapCells: world.W` to `Within`, and a thing's transform goes at the copy
nearest the eye; (5) `FarRing`'s known meshes and landmarks at the copy within half a world of the eye, moved on a
crossing or a re-centre; (6) view distances use `DeltaX` (wizard to raider, the dragon's line, cast aim).

Test with a small wrapped test map (a few world squares, bakeable) if the 12 x 10 can't load yet: walk across the seam
both ways with no pop, no hole, no jump; a fire lit across it; two peers. Stills at the seam; EditMode; smoke on the
valley and the nine; `online`. Push the branch; the lead lands it.
