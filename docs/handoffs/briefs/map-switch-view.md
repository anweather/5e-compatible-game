Needs: unity
Area: unity/Assets/Prototype/Scripts/ (ChemGrid, MapTerrains, NetSession, NetChem, FarRing, the scene roots it rebuilds, probes), tools/probe.sh
Branch: map-switch-view

# Maps phase 8, the view half: hosting another map rebuilds the world in play

`docs/design/maps-as-data.md`, the Phase 8 section (issue #140). The Drowned Chain is now a map folder on dev
(`world/maps/drowned-chain/`, landed 9cb66049), and the Esc menu's "Map when you host" picker (`NetSession`,
`MapPicker`, `HostMap`) offers it. Today a map is chosen only at start (`-map`, `Temp/map`, `ChemGrid.Maps()`);
nothing tears down and rebuilds the terrain in play.

## What to do

- When the host picks a map in play, tear down and rebuild what the map owns: the core world from the new folder,
  the chunk terrains (`MapTerrains`), the far ring, the sea/ocean, ambient life, things, the spawn. No leaked
  objects or meshes after a switch and back (count GameObjects and meshes before and after).
- A client follows the host's map (`NetChem` `hostMap`): it loads the same folder before joining the world, and a
  peer without that map (or with a different `MapIdentity.Hash`) is refused with a plain message
  (`MapIdentity.Refusal`).
- The Chain's sea level, spawn and layout come from its `map.json`.
- Probes: `mapswitch` (valley -> Chain -> valley in one session; the land hash after each equals a fresh start on
  that map; frame spike of the switch reported), and `online` with the host on the Chain (the joiner loads it and
  checksums agree).

Build the yard on the valley. Don't hand-edit `.unity`/`.prefab` YAML.

## Done

EditMode passes, smoke passes on a fresh valley yard, `mapswitch` and `online` (host on the Chain) pass. A <= 30 s
720p clip of switching to the Chain and walking a little (demo-clip-per-feature). Push the branch; the lead lands it.

After: a player build with two peers on the Chain (phase 8's last step).
