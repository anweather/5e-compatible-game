# Punchy

Code adapted from **Punchy** by Zachary Franklin
(<https://github.com/Skeleman/Punchy>), used with his permission and
credited in the game's Credits (`Scripts/Credits.cs`).

Each file here keeps a header naming what it came from and how it was
changed. Our own code that drives these files lives in `Scripts/` as
usual (`AmbientCritters.cs` for the critters, `CloudSky.cs` for the sky).

| File | From Punchy | Changes |
|---|---|---|
| `Critters.hlsl` | `Assets/Game/Shaders/Critters.hlsl` | Header only |
| `Fireflies.shader` | `Assets/Game/Shaders/Fireflies.shader` | Header only |
| `Butterflies.shader` | `Assets/Game/Shaders/Butterflies.shader` | Punchy's sky (cloud shadow, atmosphere) swapped for URP's fog |
| `Sky.hlsl` | `Assets/Game/Shaders/Sky.hlsl` | Header only |
| `Sky.shader` | `Assets/Game/Shaders/Sky.shader` | Header only (the skybox; `CloudSky.cs` drives it) |
| `CloudNoise.cs` | `Assets/Game/Scripts/Weather/CloudNoise.cs` | A parameter name and loop indents, for our analyzers |
