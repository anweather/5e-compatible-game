Needs: unity
Area: a throwaway branch only: world/maps/valley/ (stamps.json, villages.json, buildings.json, things.json appends,
places.json) written by LandmarkIntent; view fixes for how the stand-ins draw may go on a separate branch
Branch: landmark-valley-look (never merged)

# Phase 1b: the three villages, walked up to in the valley (world squares; given to the Mac)

`landmark-intent` landed (133a3692): `Chem.LandmarkIntent` makes a forest village by a stand-in great tree, a village at
a volcano's foot, and a fishing village with docks. Nobody has seen them drawn.

1. On a branch that is never merged, write the three into the valley at three open sites with room (away from the
   Hollowmere, the castle, the roads and the Hold's routes; the marsh form beside or as a new small lake). Rebuild the
   yard.
2. Walk up to each and look: does the great tree read as a huge tree (a 14 m trunk of log walls under a crown of tree
   towers)? Does the volcano read as a volcano with its crater? Do the boathouses' docks sit over water? List what looks
   wrong and why.
3. Three clips, ≤30 s each at 720p with posters (`landmark-forest.mp4`, `landmark-volcano.mp4`,
   `landmark-fishing.mp4`), walking up to each. Send them to Andrew with SendUserFile.
4. If a fix is in the intent's rows (`world/landmarks/village-with-landmark.json` or the materializer), make it on a
   separate branch `landmark-intent-fixes` off origin/dev with its goldens moved on purpose, and push that for landing.
   View-side fixes (how a tree material tower draws) also go there.

Push `landmark-valley-look` for reference only. Remove this claim on `landmark-intent-fixes` if you make one, or tell
the lead to remove it. The lead lands only the fixes branch.
