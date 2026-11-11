Needs: core
Area: unity/Assets/Prototype/Chemistry/ (GeneratedMap, BakedSource, the World's source for a generated map, ChunkGen), its tests
Branch: generate-switch

# A generated map that loads without a bake (the 12 x 10, D17's "generate switch")

`world/maps/world-12x10/` is `"bake": false` (too big to bake whole when it was planned), so it can't load in the game.
With the sparse World (D17, steps 1-3 and the guard on dev) and the wrap (802f38e7), a generated map's source can be
its generator: a chunk's cells are made by `ChunkGen` when it's first unpacked (deterministic, the same on every
peer), and packed again to "untouched" when nobody changed it. Build that as the World's source for a `"bake": false`
map; keep baked maps as they are. Generation time a chunk must fit unpacking at a chunk edge (5 at once) inside the
step budget, or be done ahead on the World's own thread from the planner's next chunks; measure it. The map's hash is
already its inputs (`GeneratedMap.InputHash`). Tests: a chunk made on demand equals the bake's; two peers and a late
joiner agree; the 12 x 10 builds its World and walks a character across the seam under dotnet. The core gates pass;
push; the PC compiles it and loads the 12 x 10.
