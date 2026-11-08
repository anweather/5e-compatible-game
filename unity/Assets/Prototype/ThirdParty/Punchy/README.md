# Punchy

Code adapted from **Punchy** by Zachary Franklin
(<https://github.com/Skeleman/Punchy>), used with his permission and
credited in the game's Credits (`Scripts/Credits.cs`).

Vendored from upstream commit **b183507**; upstream's later work is ported by
hand, a change at a time, and listed against the file below (from `7ee4511`:
cornered and leashed animals mill about; from `f3141f6`: individuals, hairstyles
and workers at their posts). Creatures are going to data rather
than Editor assets (J-058; none are left here), so ours diverges from Punchy's code and keeps its
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
| `rules/fauna.json`, `rules/parts.json` (repo root) | `Assets/Game/Fauna/`, synced to the commit in `tools/punchy-sync/synced.json` (`4601262`, 2026-11-08) | The species and limbs as rows: exported at `c52d9ff` with the same numbers, and proved equal body by body (commit `11692bd` and before), then the assets and `CreatureSpecies.cs` and `LimbType.cs` went. Their enums are in `CreatureBody.cs`. Since then `tools/punchy-sync` brings upstream's new and changed rows in, field by field, never over our own edits (its README). Lily pads and reeds are ours, as still floating rows, where upstream has flora |
| `CreatureBody.cs` | `Assets/Game/Scripts/Fauna/CreatureData.cs` | Entities blob made a plain class with arrays; `SegmentInstance.Pad` used (crack web, shadow only); built from rows (`Chem.FaunaTables`), and the row's id, group and spawn kept on the body. From `f3141f6`: `Trait`, `Style`, hair colours, style options on parts |
| `CreatureAnimator.cs` | `Assets/Game/Scripts/Fauna/CreatureAnimator.cs` | Arrays and reused scratch for Burst lists; ground through `ICreatureGround`. From `f3141f6`: traits, style options, hair, `TaskPose` (read from `Task`, which our folk set at their posts, as there's no `CreatureMode.Work`). Ours: traits kept per animal (`CreatureTraits`), and unchosen options neither posed nor written to the buffer, where Punchy writes them at zero size, so `Evaluate` returns how many it wrote |
| `CreatureBrain.cs` | `Assets/Game/Scripts/Fauna/CreatureBrainSystem.cs` | From `c52d9ff`: habitats, depth through `ICreatureGround.WaterDepth`, leaps. One creature at a time; the nearest threat (a wizard or fire) for the player. From `7ee4511`: a leash holds even when fleeing, and an animal at its end mills about. Ours: the same for any corner (`Veer` keeps to a side until it has turned; `Mill`, `Clear`), and milling takes steps and looks at the danger, where Punchy's only slows to a fifth of a run. From `f3141f6`: pace, on the walk only (on the run it overshoots a corner). Not `CreatureMode.Work`: our workers are `ValleyFauna.Folk`'s puppets |
| `CreatureCapsule.cs` | `FaunaDirector.BuildCapsule` | Moved to its own file; wound outside out; `readable` keeps the mesh for tests |

The creature files turn our style analyzers off (`#pragma warning disable`),
so they can stay close to Punchy's code.
| `Precipitation.shader` | `Assets/Game/Shaders/Precipitation.shader` | Header only (rain streaks and snowflakes for `WeatherFx`) |
| `ThunderSynth.cs` | `Assets/Game/Scripts/Weather/ThunderSynth.cs` | Header only (a new thunderclap for every strike) |
| `Terrain/Editor/BiomeTerrain.cs` | `Assets/Game/Scripts/Terrain/Biomes/BiomeData.cs` (BiomeSampling), `BiomeBaking.cs`, `TerrainSource.ShapeHeight` | Blobs made plain classes; surface stacks give per-surface weights for Unity terrain layers; a regional bias on biome picks. Editor-only (`ValleySurroundings.cs` bakes the land around the valley with it) |

## Creatures waiting on code

`tools/punchy-sync` leaves out a species that needs code we haven't ported, and its report lists them. At
`4601262` (2026-11-08) those are:

- **Flight and wings** (`Habitat.Flies`, `LimbRole.Wing`, upstream's flight in `CreatureBrainSystem` and
  `CreatureAnimator`): bat, bat folk, canyon wren, condor, crow, gull, osprey, owl, pigeon, roc, smoke crow,
  songbird, storm hawk, swallow, woodpecker; with cave lights, glass, glow and plain moths; hawking over water,
  the dragonfly; breaching out of the water, the sky serpent.
- **A variant's own head, thickness or legs** (`VariantHead`, `VariantThickness`, `VariantLegs` in
  `CreatureData`): hermit, spore toad. Our human child is in, drawn without its bigger head and shorter legs.
- **Tunnelling** (`Tunnels`): sand worm, snow worm.
- **Rooted** (`Rooted`): snapping flytrap. **Passing for a plant** (`Disguised`): ocotillo mimic, shuffling shrub.
- **A ghost's drift** (`Haunts`): ghost. **Living in lava** (`LavaDweller`): bone lizard.

What the species we took do upstream beyond our rows (calls, idle habits, gallop styles, climbing, webs,
burrows, nests, guards' kit, folk regard) is in the report too: they work without it.
