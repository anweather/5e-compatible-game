# Punchy

Code adapted from **Punchy** by Zachary Franklin
(<https://github.com/Skeleman/Punchy>), used with his permission and
credited in the game's Credits (`Scripts/Credits.cs`).

Each file here keeps a header naming what it came from and how it was
changed. Our own code that drives these files lives in `Scripts/` as
usual (`AmbientCritters.cs` for the critters, `CloudSky.cs` for the sky,
`ValleyFauna.cs` for the animals).

| File | From Punchy | Changes |
|---|---|---|
| `Critters.hlsl` | `Assets/Game/Shaders/Critters.hlsl` | Header only |
| `Fireflies.shader` | `Assets/Game/Shaders/Fireflies.shader` | Header only |
| `Butterflies.shader` | `Assets/Game/Shaders/Butterflies.shader` | Punchy's sky (cloud shadow, atmosphere) swapped for URP's fog |
| `Sky.hlsl` | `Assets/Game/Shaders/Sky.hlsl` | Header only |
| `Sky.shader` | `Assets/Game/Shaders/Sky.shader` | Header only (the skybox; `CloudSky.cs` drives it) |
| `CloudNoise.cs` | `Assets/Game/Scripts/Weather/CloudNoise.cs` | A parameter name and loop indents, for our analyzers |
| `Creature.shader` | `Assets/Game/Shaders/Creature.shader` | Punchy's atmosphere swapped for URP's fog |
| `CreatureSpecies.cs`, `LimbType.cs` | `Assets/Game/Scripts/Fauna/` | Header only (same script GUIDs, so the assets load) |
| `Fauna/` (species and `Limbs/`) | `Assets/Game/Fauna/` | Unchanged |
| `CreatureBody.cs` | `Assets/Game/Scripts/Fauna/CreatureData.cs` | Entities blob made a plain class with arrays |
| `CreatureAnimator.cs` | `Assets/Game/Scripts/Fauna/CreatureAnimator.cs` | Arrays and reused scratch for Burst lists; ground through `ICreatureGround` |
| `CreatureBrain.cs` | `Assets/Game/Scripts/Fauna/CreatureBrainSystem.cs` | One creature at a time; the nearest threat (a wizard or fire) for the player |
| `CreatureCapsule.cs` | `FaunaDirector.BuildCapsule` | Moved to its own file |

The creature files turn our style analyzers off (`#pragma warning disable`),
so they can stay close to Punchy's code.
| `Precipitation.shader` | `Assets/Game/Shaders/Precipitation.shader` | Header only (rain streaks and snowflakes for `WeatherFx`) |
| `ThunderSynth.cs` | `Assets/Game/Scripts/Weather/ThunderSynth.cs` | Header only (a new thunderclap for every strike) |
