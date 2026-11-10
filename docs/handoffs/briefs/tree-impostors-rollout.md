Needs: unity
Area: unity/Assets/Prototype/Scripts/ (the tree impostor renderer, MapTerrains' trees, ValleyBuilder's tree prototypes), Scripts/Editor/ (the baker), the impostor shader, docs/design/tree-impostors.md
Branch: tree-impostors-rollout
After: tree-impostors-trial (landed d3d4ca21)

# Tree impostors for every species, on by default (J-090, J-091)

The trial (d3d4ca21, off by default, `docs/design/tree-impostors.md`) proved it on Conifer: the nine walk 9.63/15.22
-> 8.63/14.58, draws 3,294 -> 973, triangles 17.8 -> 10.9 M, trees drawn to 500 m. Andrew's answers (J-091):

- **Near trees (within ~60 m) as SpeedTree GameObjects with LOD groups**, so SpeedTree's wind is back. Pool them as
  the camera moves (no destroy-and-recreate per tree), nearest first, within a frame budget; the instanced near
  meshes go.
- **Impostors cast shadows within the drawn tier only** (~256 m), none beyond.

Also:
- Every species the maps use (the valley's and the generated land's), baked by the menu; atlases stay under
  `Generated/` (ignored), rebuilt when missing as the yard is.
- The 60 m seam: impostors were ~10% brighter and yellower than the mesh; match them (the bake's lighting, the
  shader's ambient and gamma) so the forest view shows no seam.
- #213 before turning it on: a packed chunk shows trees as they started (burnt ones still standing far off), and the
  pinned valley's far trees shift when their terrain is made. Trees burned since start must not show.
- On by default once the board matches; a flag to turn it off.

## Check

A stills board like the trial's (every species, the forest view at the 60 m join, morning and noon), the circle at 30 m
with wind moving; the nine walk, standing and the valley walk before and after in a development player; EditMode in
full; smoke on the valley and the nine (with impostors on, the default now); `online`. Push the branch; the lead sends
Andrew the board and lands it.
