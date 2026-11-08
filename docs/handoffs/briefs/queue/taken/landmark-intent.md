Needs: core
Area: unity/Assets/Prototype/Chemistry (a new LandmarkIntent and its materializer, with tests), world/landmarks/
(new), a small core tool or test that renders top-down PNGs; reads stamps, villages and buildings rows; no valley map
files change
Branch: landmark-intent

# The first slice of the world squares: one landmark intent, three ways (J-080, phase 1; given to the Mac)

Read `docs/design/world-squares.md` (sections 1-2, "A landmark intent", and phase 1), J-079 and J-080 in
`JUDGMENT_CALLS.md`. The plan is being revised to generate on demand rather than bake (J-080), so keep this slice a
pure function: given an intent, a biome, a site and a seed, it returns the same rows on every machine.

1. **`world/landmarks/village-with-landmark.json`:** the intent "a village with a landmark", with a form per biome:
   - temperate (forest): a forest village by a great tree. No row makes a great tree yet: a stand-in (a big trunk and
     crown from existing tree or prop rows, scaled, with a ring of roots), per Andrew's pick;
   - mountains: a village at the foot of a volcano (cone and crater map stamps, stone houses);
   - a lake or foggy-marsh biome: a fishing village on a stamped lake, with docks;
   - `*` falls back to temperate.
2. **`Chem.LandmarkIntent`** reads it, and a materializer turns (intent, biome, site, seed) into map rows we already
   read: `stamps.json` stamps, a `villages.json` recipe and seed, `buildings.json` rows, `things.json` props per chunk,
   and a `places.json` entry with a name from a seeded list. Integer and seeded only (D4).
3. **Pictures:** a top-down PNG of each of the three on an otherwise flat test patch (land shaded, cells coloured,
   houses and the landmark marked), at 1024 px, from a core test or a small tool, with no Unity.
4. Goldens: the rows' hash for each form and two seeds, so the PC and u2 can confirm they agree.

Before landing: `dotnet build core -c Release -warnaserror -m:1`, `dotnet test core` twice, `dotnet format`. Send the
three PNGs to Andrew with SendUserFile. Push the branch only, remove this claim on the branch in the last commit. The
lead lands it.
