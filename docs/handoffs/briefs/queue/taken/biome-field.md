Needs: core
Area: unity/Assets/Prototype/Chemistry (a new BiomeField and its tests), world/biomes.json (new); reads Punchy's
Assets/Game/Scripts/Terrain/Biomes/BiomeData.cs for the logic (https://github.com/Skeleman/Punchy, borrowed with
credit); no map files change, nothing in the valley moves
Branch: biome-field

# Punchy's biome logic in the core, and how fast it runs on demand (J-079, phase 2; given to u2)

Read `docs/design/world-nine-options.md` (the "Punchy's biome logic" part and phase 2) and J-079. Andrew picked:
anchors (he names an anchor biome per square and the climate draws the borders), a curated 7 biomes, and **left
"generate ahead of time or at start-up" open: "How quickly can we generate on demand? I'd love to explore that."**

1. **Port `BiomeSampling`** into `Chem.BiomeField`: domain-warped regions (scaled to about one valley square, ~860 m),
   the four climate channels (altitude, temperature falling with altitude, humidity, volcanism), the scored pick
   (Frequency × exp(-misfit²), here in fixed point), and the top-4 blend weights. Integers and our `WorldNoise` /
   `WorldHash` only, seeded; no floats in the state (Punchy's float simplex differs between machines).
2. **Anchors:** a 3 × 3 layout of anchor biomes per valley-sized square (the valley Temperate at the south-west by the
   ocean; the rest as data) biases the pick, so the named biome wins in its square and the climate shapes the edges.
3. **`world/biomes.json`:** the 13 Punchy rows as data, with the curated 7 on (Temperate, Grassland, Mountains, Tundra,
   SaltFlat or Sonoran, FoggyMarsh, Ashlands or Volcano) and the others off.
4. **Measure on demand,** to answer Andrew: the time to sample one 64 × 64 chunk's biome weights (and, roughly, what
   land from biomes would add), median of many, in core Release. From that, say whether chunks could be generated as a
   character's reach moves (a chunk ahead of the player within a frame budget) versus baking ahead, with numbers.
5. Goldens: the field's output hash for a few seeds and points, so the PC and the Mac can confirm they agree.

Credit Punchy in `ThirdParty/Punchy/README.md`. Before landing: `dotnet build core -c Release -warnaserror`, `dotnet
test core` twice in full, `dotnet format`. Push the branch only, remove this claim on the branch in the last commit.
The lead lands it (no Unity view needed yet).
