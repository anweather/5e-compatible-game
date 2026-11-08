Needs: unity
Area: world/maps/valley/ (land.json, stamps.json, hold.json, places.json, things.json appends only), raider routes,
the player's speed in shallow water
Branch: hold-east-path

# A second raider road from the east, and water that doesn't drag (Andrew, 2026-11-08)

From `docs/playtests/2026-11-08-human-results.md`, C3, B4 and the first part of W1.

1. **A second raider path** into the castle from the east, the side opposite the stable. Some waves (or part of each)
   come that way, so you defend two sides. A route the raiders can really walk, joined in `hold.json`, MapCheck clean.
2. **Shallow water slows you far too much** when you aren't swimming: at the water's edge and in the thin strip of
   water by the castle. Wading should slow a little, not a lot.
3. **The thin strip by the castle:** say what it is. If it's a leftover, take it out; if it's meant as a river, make it
   read as one (a real channel down toward the lake). The bigger river work comes with the world plan (W1, W2).

Before landing: `dotnet test core`, EditMode N/N, the `hold`, `crystal`, `towers` probes and the Hold lockstep test,
and a ≤30 s clip with a poster (`hold-east-path.mp4`): raiders on both roads and a wade through the shallows. Push the
branch only, remove this claim on the branch. The lead lands it.
