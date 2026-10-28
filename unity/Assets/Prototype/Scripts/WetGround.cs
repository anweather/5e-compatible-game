using System.Collections.Generic;
using UnityEngine;

// Rain darkens the valley's ground and makes it shine, and it dries over a
// minute or two after (J-024). The chunk terrains draw with URP's TerrainLit,
// which reads each terrain layer's diffuse tint (diffuseRemapMax) and
// smoothness, so the wetness is those two on runtime copies of the layers:
// nothing extra on the GPU, and the saved layers never change. Driven by the
// weather's rain (World.Weather), which every peer has the same; the view only
// reads it. ValleyChemistry makes one per world and steps it once a frame.
public sealed class WetGround
{
    // How dark and how glossy fully wet ground is: its tint times DarkWet, its smoothness up to Gloss.
    const float DarkWet = 0.62f, Gloss = 0.72f;
    // Rain this strong (0..100) soaks the ground fully.
    const float Soaking = 60f;
    // Seconds to soak through in heavy rain, and to dry after.
    const float SoakSeconds = 8f, DrySeconds = 90f;
    // Only rewrite the layers when the wetness has moved this much (each write re-sets the terrains' materials).
    const float Step = 0.02f;

    readonly Dictionary<TerrainLayer, (TerrainLayer wet, Vector4 tint, float smoothness)> layers = new();
    float shown, written = -1f;

    /// <summary>0 dry to 1 soaked, as drawn now.</summary>
    public float Wetness => shown;

    /// <summary>Gives each of these runtime terrain data copies its own copies of their layers, to wet.</summary>
    public WetGround(IEnumerable<TerrainData> chunks)
    {
        foreach (var data in chunks)
        {
            var own = data.terrainLayers;
            bool changed = false;
            for (int i = 0; i < own.Length; i++)
            {
                if (own[i] == null) continue;
                if (!layers.TryGetValue(own[i], out var copy))
                {
                    var wet = Object.Instantiate(own[i]);
                    wet.name = own[i].name;
                    copy = (wet, own[i].diffuseRemapMax, own[i].smoothness);
                    layers[own[i]] = copy;
                }
                own[i] = copy.wet;
                changed = true;
            }
            if (changed) data.terrainLayers = own;
        }
    }

    /// <summary>
    /// Once a frame: the ground soaks toward how hard it's raining (<paramref name="rain"/>, 0..100)
    /// and dries slowly once it stops.
    /// </summary>
    public void Advance(int rain, float dt)
    {
        float target = Mathf.Clamp01(rain / Soaking);
        float rate = target > shown ? 1f / SoakSeconds : 1f / DrySeconds;
        shown = Mathf.MoveTowards(shown, target, rate * dt);
        if (Mathf.Abs(shown - written) < Step && !(shown == 0f && written > 0f)) return;
        written = shown;
        foreach (var (wet, tint, smoothness) in layers.Values)
        {
            float dark = Mathf.Lerp(1f, DarkWet, shown);
            wet.diffuseRemapMax = new Vector4(tint.x * dark, tint.y * dark, tint.z * dark, tint.w);
            wet.smoothness = Mathf.Lerp(smoothness, Mathf.Max(smoothness, Gloss), shown);
        }
    }

    /// <summary>Frees the layer copies (the terrain data copies they're on are freed by ValleyGround).</summary>
    public void Destroy()
    {
        foreach (var (wet, _, _) in layers.Values) Object.Destroy(wet);
        layers.Clear();
    }
}
