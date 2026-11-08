Needs: core
Area: unity/Assets/Prototype/Chemistry/WorldPlan*.cs, unity/Assets/Prototype/Chemistry/Tests/WorldPlanTests.cs,
world/squares/ and world/bands.json (new), docs/GLOSSARY.md (the new words first); no valley map files change
Branch: world-plan

# The world plan: bands, templates, matched edges (world squares phase 2; given to u2)

Read `docs/design/world-squares.md` (revised for J-080: sections on the two levels, the data shapes, the solve, and
phase 2) and J-079 and J-080 in `JUDGMENT_CALLS.md`. Andrew picked: the whole plan solved at load on every peer,
folders on disk as a cache, `sight` 4 and `know` 7.

1. **Glossary first:** add world square, world plan, square template, landmark intent, known and generated to
   `docs/GLOSSARY.md` ("square" alone is a spell shape).
2. **`world/bands.json`** (sea, low, hills, high, peaks; touching squares differ by at most one band) and **12 square
   templates** in `world/squares/` with bands, anchor biomes, one or two landmark intents with `where` rules, edge
   offers per side (road, river, range, canyon at one of 9 slots, `must` or `may`), a **coast** profile per side
   (which slots are sea and the shoreline's offset; Andrew: "make sure the coastlines are consistent"; see the plan's
   "Coasts are part of the puzzle"), `ends`, and `rotate`. Include a filler per band. Coasts must match across sides and
   at corners, like bands; the picture draws the shoreline.
3. **`Chem.WorldPlan`:** solve a W × H grid of squares from a seed, the valley pinned at the south-west (its edges read
   from its land at the borders): bands, then templates wave-function style with backtracking and fillers so it never
   fails, edges matched. Repeats weighted down (÷4 each time) and never within 3 squares (Chebyshev), none in a 3 × 3.
   Integer and seeded; the result depends only on the seed and the inputs. A plan hash.
4. **Wrap-ready:** neighbour lookups go through one function, so a torus later is a flag, not a rewrite. A test solves a
   12 × 12 torus and measures the time (Andrew: whole plan at load; it must stay well under a second).
5. **Pictures:** ten seeds as top-down diagrams (3 × 3, and one 12 × 12): squares coloured by band and biome, edge
   features drawn across sides, landmarks marked. PNGs from a core test, no Unity. Goldens: plan hashes for a few seeds.

Before landing: `dotnet build core -c Release -warnaserror`, `dotnet test core` twice in full in the foreground (the
runner closes a session that only waits), `dotnet format`. Push the branch only, remove this claim on the branch in the
last commit. The lead lands it.

Held (2026-11-08): Andrew asked for a paper prototype of the world squares first. Queue this again after he has
tried it, with what he changes.
