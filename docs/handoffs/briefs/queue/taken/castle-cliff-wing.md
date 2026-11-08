Needs: unity
Area: world/maps/valley/ (land.json, ground/, rocks.json, looks.json, stamps.json, buildings.json for the castle,
things.json appends only, places.json, hold.json only if the castle's joins must move), LandStamp* and land drawing
Branch: castle-cliff-canyon (continue it; merge origin/dev first)

# A level pad from the castle to its cliff (Andrew, 2026-11-07, after seeing castle-cliff-canyon 06ad830a)

Andrew: **the canyon is great, keep it as it is. The cliff face isn't.**

- **The castle stays where it is** (Andrew, correcting this brief the same evening): not on the cliff, and no wing.
- **Level the land from the castle to the cliff top.** The ground from the castle's door out to the cliff's edge is
  one flat pad at the castle's ground height: you walk from the door to the lip without going up or down. The cliff
  drops sheer from that pad's edge. Check it by walking it, and add a probe check that those cells are one height.
- **One outdoor training place on the pad,** outside the castle walls: an archery range, a stable or a training ground,
  from `buildings.json` and `things.json` primitives in the castle's style.
- The cliff face below stays sheer and unclimbable, with rock, rubble at the foot, and boulders jutting at the lip.
- The Hold must keep working: raiders still come up the road (the training place must not block the road or the gate), the four
  corner towers still attune (hold-batch, 584e7693), the crystal at the heart, and MapCheck. If the training place needs a join in
  `hold.json`, add one; don't move `heart` or the towers.

Rules as before: the map is data; new things are general primitives, not one-offs; `things.json` is append-only.

Before landing: merge origin/dev (hold-batch changed `buildings.json` and the Hold), `dotnet test core`, **EditMode N/N
rerun in full**, the probes (`castlecliff` with the new flat-walk check, `cliffclip`, `landshapes`, `stamplooks`,
`hold`, `crystal`, `towers`), and the frame cost of the 1400-rock cap measured from the town and on the walls.
Before/after shots at 1280 wide (check them at 480 px) from the town, along the walk from the door to the edge, and
from the canyon floor looking up. Plus a ≤30 s 720p clip with a poster (`castle-cliff-canyon.mp4`, replacing the old
one): the cliff and pad from the town, the walk from the door to the edge past the training place, and the view down. Push the
branch only. Remove this claim file on the branch in the last commit. The lead lands it.
