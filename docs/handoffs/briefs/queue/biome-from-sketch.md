Needs: core
Area: unity/Assets/Prototype/Chemistry/ (WorldPlan*, BiomeField, ChunkGen*, GeneratedMap) and their tests, world/maps/*/map.json, world/squares/
Branch: biome-from-sketch
After: world-12x10-plan (same files; don't start until it has landed)

# The biome comes from the world, not the square template (J-085)

Andrew (J-085): big forests, deserts and lakes should span many world squares instead of a patchwork. Today the plan
picks one square template per world square and the land's biome follows that template's anchor biome; the spacing
rule (J-080: no two copies within 3 world squares, each repeat weighed /4) then keeps any one biome from filling a
region, since there are only 13 templates. The world sketch paper (`tools/world-paper/`, World sketch panel;
`docs/design/world-squares.md` "Draw first") measured it: on the 12 x 10 only 41% of drawn world squares got the
drawn biome.

## What to do

- The land's biome comes from a world-wide field: the world sketch where there is one (port the paper's sketch JSON
  as a `map.json` field, its shapes, warp and blend), else latitude and the plan's bands. It runs smoothly across
  world squares and across the east-west seam.
- A square template keeps its village, landmarks, relief and layout, and its biome becomes a preference (weighting
  which template fits where), not what the land is. The spacing rule stays exactly as J-080 set it, for templates.
- The nine and the valley: the valley stays byte-identical. The nine may change; if it does, change its goldens in
  the same commit and say what moved, with a before and after picture.
- Tests: a sketch with one big forest gives forest land on every world square it covers (say 90%+ of its cells),
  whatever templates the plan picked; no template repeats within 3; the seam is continuous; plan goldens.
- Pictures for the report: the 12 x 10 default sketch, before and after (biome map), small PNGs.

## Done

`dotnet build core -c Release -warnaserror`, `dotnet test core`, `dotnet format core --verify-no-changes` pass. Push;
land with `land.sh` if only core and data changed, else stop for the PC to compile. Mind `Chemistry/CLAUDE.md`'s
notes on Unity's NUnit and fixture categories.
