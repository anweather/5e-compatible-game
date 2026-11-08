Needs: unity
Area: rules/hold.json and rules/entities.json (raider kinds, through the rules MCP), world/maps/valley/hold.json,
unity/Assets/Prototype/Scripts (raider bodies, ValleyFauna.Folk.cs for villagers), Hold probes
Branch: hold-bodies

# More raider bodies, and villagers off the road during the Hold (Andrew, 2026-11-07; given to the Mac)

From `docs/playtests/2026-11-06-human-results.md`:

1. **More raider bodies.** "Reuse the deer and other animals as wave raiders." Today the waves are mostly the same
   few bodies. Give the waves at least three more raider kinds built from the existing ambient-life bodies (deer and
   others in the valley's fauna), each with a clear role, for example fast and frail, slow and tough, or a charger.
   Raider kinds belong in the rules (`rules/hold.json`, `rules/entities.json`), not in code. Mix them into the
   waves of the 10-minute timed run from hold-batch (584e7693). Each one must attack the crystal like the others do.
2. **Villagers walk up and down the road during the Hold.** Andrew doesn't see why. While a Hold run is on, keep the
   villagers off the raiders' road and the castle approach: home in Hollowmere, or away from the route. Free roam stays
   as it is.

Lockstep (D4): raider kinds, their stats and their spawns are core state, deterministic, with golden tests where the
core changes. Bodies and animation are view only.

Before landing: merge origin/dev (castle-cliff-canyon just changed the castle pad and the start of the road),
`dotnet test core`, EditMode N/N in full, the `hold`, `crystal`, `towers` and `villagers` probes, and the Hold
lockstep test. Make a ≤30 s 720p clip with a poster (`hold-bodies.mp4`): a mixed wave coming up the road at the
crystal, and the empty road. Send it to Andrew with SendUserFile. Push the branch only. Remove this claim file on the
branch in the last commit. The lead lands it.
