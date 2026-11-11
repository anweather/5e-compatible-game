Needs: unity
Area: unity/Assets/Prototype/Scripts/ (TreeImpostors and the near-tree distance, MapTerrains' drawn tier and pop-in, Ocean.Tiles and CellWaterMesh, FarRing's haze and edge, CellDressing's bushes, the butterflies), docs/design/tree-impostors.md
Branch: playtest-looks

# What Andrew saw in the nine (#220), the looks

Go to each place with the probe's camera, take a still, then fix, each its own commit:
1. **Trees switch too close:** near SpeedTree meshes out to ~150 m (pooled, nearest first), impostors past; measure
   the walk's cost; in sparse forest the switch must not show.
2. **Pop-in against the fog:** from the mountain by the waterfall at the valley's edge, the dock pops in and out when
   walking back and forth: something switches at a distance the haze doesn't hide (dressing's 500 m cut, the
   small-mesh cull, the drawn tier's hysteresis); match every cut to the fog, with hysteresis.
3. **Water chunks vanish** while looked at (lake and sea), and an elevation jump between lake and sea: the sea tiles'
   and the water mesh's per-chunk blocks (ocean-drawn-214, view-map-arrays).
4. **Far view:** a blue haze band from the horizon down to the land; a big white cube (likely the nine's straight
   outer edge); the far ring past the map's edge.
5. **A chunk seam where the biome changes**, and **floating bushes**.
6. **Butterflies:** fewer, and by biome (Punchy's approach, which we may borrow from).

Stills before and after for each; the nine walk in a development player before and after; EditMode; smoke on both
maps. Push; the lead lands it.
