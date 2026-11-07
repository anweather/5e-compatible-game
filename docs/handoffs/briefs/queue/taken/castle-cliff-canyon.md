Needs: unity
Area: world/maps/valley/ (land.json, ground/, rocks.json, looks.json, things.json appends only, places.json), unity/Assets/Prototype/Scripts/World/LandStamp*, Land rock and cliff drawing
Branch: castle-cliff-canyon

# The castle's cliff and a real canyon (Andrew, 2026-11-07; given to the PC)

Where: stand in the town and look at the castle on its hill (the Hold, west of Hollowmere). **To the right of the
road** is a bare slope of rock and mud today. Further right, the slopes make a natural valley. Take a 480 px shot from
the town first and say which cells you mean before changing anything.

1. **A sheer cliff.** Turn the bare slope right of the road into a sheer cliff face from there to the castle, with no
   way to walk up it, and running a bit further than the slope does today. A little overhang at the top if the land
   allows it. Land is a heightfield, so an overhang will likely be rock things leaning out over the lip, not land.
   Say which you did. It should look really good: rock face, strata or crags, rubble at the foot.
2. **The canyon.** Deepen the valley to the right of the cliff into a real canyon. It has forest along the top, rock on
   the walls, a rock floor, and a winding path along the bottom with at least one dog-leg bend, not a straight cut.
   The early canyon test (`land-shapes`, 43658bcb) has the shapes. This one is part of the map, not a test.
3. Leave the road, the castle pad and the Hold's routes as they are. Raiders must still come up the road, and the
   `hold` probes must pass. The valley's `MapCheck` must pass.

Rules: the map is data (`docs/design/maps-as-data.md`). Change it through the land stamps and map files, not by hand
in a scene. `things.json` is append-only. New places go in `places.json` with names. If a land-stamp tool is missing
something the cliff or canyon needs (an overhang, a winding floor), add it as a general primitive (J-055, J-057), not
a one-off.

Before landing: `dotnet test core`, EditMode N/N, the land probes (`landshapes`, `stamplooks`) and the Hold probes,
before/after shots at 1280 wide from the town and from the canyon floor (check them yourself at 480 px), and a
≤30 s 720p clip with a poster (`unity/Builds/demo-videos/castle-cliff-canyon.mp4`): the cliff from the town, then a
walk down the canyon's path round the bend. Commit and push the branch only. Remove this claim file on the branch in
the last commit. The lead lands it.
