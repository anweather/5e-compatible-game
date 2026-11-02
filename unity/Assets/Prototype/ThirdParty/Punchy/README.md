# Punchy

Code adapted from **Punchy** by Zachary Franklin
(<https://github.com/Skeleman/Punchy>), used with his permission and
credited in the game's Credits (`Scripts/Credits.cs`).

Vendored from upstream commit **b183507**; upstream's later work is ported by
hand, a change at a time, and listed against the file below (from `7ee4511`:
cornered and leashed animals mill about). Creatures are going to data rather
than Editor assets (J-058), so ours diverges from Punchy's code and keeps its
style.

Each file here keeps a header naming what it came from and how it was
changed. Our own code that drives these files lives in `Scripts/` as
usual (`AmbientCritters.cs` for the critters, `CloudSky.cs` for the sky,
`ValleyFauna.cs` for the animals).

| File | From Punchy | Changes |
|---|---|---|
| `Critters.hlsl` | `Assets/Game/Shaders/Critters.hlsl` | Header only |
| `Fireflies.shader` | `Assets/Game/Shaders/Fireflies.shader` | Header only |
| `Butterflies.shader` | `Assets/Game/Shaders/Butterflies.shader` | Punchy's sky (cloud shadow, atmosphere) swapped for URP's fog |
| `Sky.hlsl` | `Assets/Game/Shaders/Sky.hlsl` | Stars: a second, fainter and denser layer, both scaled by `_PunchyStarBright` (set by `CloudSky` from the rules' light level, `Nightfall`) |
| `Sky.shader` | `Assets/Game/Shaders/Sky.shader` | Header only (the skybox; `CloudSky.cs` drives it) |
| `CloudNoise.cs` | `Assets/Game/Scripts/Weather/CloudNoise.cs` | A parameter name and loop indents, for our analyzers |
| `Creature.shader` | `Assets/Game/Shaders/Creature.shader` | Punchy's atmosphere swapped for URP's fog; a crack web on a segment; a segment's pad under 0 draws it for its shadow alone (`CreatureDrawer.HideAtLens`: the lens inside a creature, issue #47) |
| `CreatureSpecies.cs`, `LimbType.cs` | `Assets/Game/Scripts/Fauna/` | Header only (same script GUIDs, so the assets load). Read only by the export and its proof now; the enums `CreatureBody` uses live here |
| `Fauna/` (species and `Limbs/`) | `Assets/Game/Fauna/` | Unchanged, and no longer read by the game: **Prototype > Export Creature Rows** wrote them out as `rules/fauna.json` and `rules/parts.json`, and `CreatureRowTests` proves the bodies equal. They stay until the rest of the port has gone through the same export |
| `CreatureBody.cs` | `Assets/Game/Scripts/Fauna/CreatureData.cs` | Entities blob made a plain class with arrays; `SegmentInstance.Pad` used (crack web, shadow only); built from rows (`Chem.FaunaTables`), and the row's id, group and spawn kept on the body |
| `CreatureAnimator.cs` | `Assets/Game/Scripts/Fauna/CreatureAnimator.cs` | Arrays and reused scratch for Burst lists; ground through `ICreatureGround` |
| `CreatureBrain.cs` | `Assets/Game/Scripts/Fauna/CreatureBrainSystem.cs` | One creature at a time; the nearest threat (a wizard or fire) for the player. From `7ee4511`: a leash holds even when fleeing, and an animal at its end mills about. Ours: the same for any corner (`Veer` keeps to a side until it has turned; `Mill`, `Clear`), and milling takes steps and looks at the danger, where Punchy's only slows to a fifth of a run |
| `CreatureCapsule.cs` | `FaunaDirector.BuildCapsule` | Moved to its own file; wound outside out; `readable` keeps the mesh for tests |

The creature files turn our style analyzers off (`#pragma warning disable`),
so they can stay close to Punchy's code.
| `Precipitation.shader` | `Assets/Game/Shaders/Precipitation.shader` | Header only (rain streaks and snowflakes for `WeatherFx`) |
| `ThunderSynth.cs` | `Assets/Game/Scripts/Weather/ThunderSynth.cs` | Header only (a new thunderclap for every strike) |
| `Terrain/Editor/BiomeTerrain.cs` | `Assets/Game/Scripts/Terrain/Biomes/BiomeData.cs` (BiomeSampling), `BiomeBaking.cs`, `TerrainSource.ShapeHeight` | Blobs made plain classes; surface stacks give per-surface weights for Unity terrain layers; a regional bias on biome picks. Editor-only (`ValleySurroundings.cs` bakes the land around the valley with it) |
