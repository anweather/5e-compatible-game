Needs: unity
Area: world/maps/valley/ (land.json, ground/, rocks.json, looks.json, stamps.json, buildings.json for the castle,
things.json appends only, places.json, hold.json only if the castle's joins must move), LandStamp* and land drawing
Branch: castle-cliff-canyon (continue it; merge origin/dev first)

# The castle on its cliff (Andrew, 2026-11-07, after seeing castle-cliff-canyon 06ad830a)

Andrew: **the canyon is great, keep it as it is. The cliff face isn't.**

- **The castle stands on the cliff.** The cliff's top is the castle's ground height, and the cliff blends into the
  castle's hill. It isn't a separate rock wall beside it. You must be able to **walk from the castle's door to the
  cliff's edge without going up or down**: one flat ground from the keep to the lip. Check this by walking it, and add a
  probe check that the cells from the door to the edge are one height.
- **A new wing on the castle reaches out to the cliff,** making the castle bigger. It's walls and a tower or hall in the
  castle's style, from `buildings.json` primitives, standing at or near the edge, so the castle looks out over the drop.
- The cliff face below stays sheer and unclimbable, with rock, rubble at the foot, and boulders jutting at the lip.
- The Hold must keep working: raiders still come up the road (the wing must not block the road or the gate), the four
  corner towers still attune (hold-batch, 584e7693), the crystal at the heart, and MapCheck. If the wing needs a join in
  `hold.json`, add one; don't move `heart` or the towers.

Rules as before: the map is data; new things are general primitives, not one-offs; `things.json` is append-only.

Before landing: merge origin/dev (hold-batch changed `buildings.json` and the Hold), `dotnet test core`, **EditMode N/N
rerun in full**, the probes (`castlecliff` with the new flat-walk check, `cliffclip`, `landshapes`, `stamplooks`,
`hold`, `crystal`, `towers`), and the frame cost of the 1400-rock cap measured from the town and on the walls.
Before/after shots at 1280 wide (check them at 480 px) from the town, along the walk from the door to the edge, and
from the canyon floor looking up. Plus a ≤30 s 720p clip with a poster (`castle-cliff-canyon.mp4`, replacing the old
one): the castle on its cliff from the town, then the walk from the door to the edge and the view down. Push the
branch only. Remove this claim file on the branch in the last commit. The lead lands it.
