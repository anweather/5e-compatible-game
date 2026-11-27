using System.Collections.Generic;
using UnityEngine;

// Rain darkens the valley's ground and makes it shine, and it dries over a
// minute or two after (J-024). The chunk terrains draw with URP's TerrainLit,
// which reads each terrain layer's diffuse tint (diffuseRemapMax) and smoothness
// (from its mask map's remap, where it has one), so the wetness is those on
// runtime copies of the layers: nothing extra on the GPU, and the saved layers
// never change. Driven by the
// weather's rain (World.Weather), which every peer has the same; the view only
// reads it. ValleyChemistry makes one per world and steps it once a frame.
public sealed class WetGround
{
    const float DarkWet = 0.62f, Gloss = 0.6f, GrassGloss = 0.45f;
    const float Soaking = 60f;
    const float SoakSeconds = 8f, DrySeconds = 90f;
    const float Step = 0.02f;

    readonly Dictionary<TerrainLayer, (TerrainLayer wet, Vector4 tint, float smoothness, Vector4 maskMin, Vector4 maskMax, float gloss)> layers = new();
    readonly Dictionary<TerrainLayer, Color> tints = new();
    float shown, written = -1f;

    public float Wetness => shown;

    public WetGround(IEnumerable<TerrainData> chunks)
    {
        foreach (var data in chunks) Add(data);
    }

    public void Add(TerrainData data)
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
                float gloss = own[i].name.Contains("Grass") ? GrassGloss : Gloss;
                var (dryMin, dryMax) = Dry(own[i].maskMapRemapMin, own[i].maskMapRemapMax);
                wet.maskMapRemapMin = dryMin;
                wet.maskMapRemapMax = dryMax;
                copy = (wet, own[i].diffuseRemapMax, own[i].smoothness, dryMin, dryMax, gloss);
                layers[own[i]] = copy;
            }
            own[i] = copy.wet;
            changed = true;
        }
        if (changed) data.terrainLayers = own;
    }

    public static (Vector4 min, Vector4 max) Dry(Vector4 min, Vector4 max) =>
        (new Vector4(0f, min.y, min.z, Mathf.Min(min.w, DryShine)), new Vector4(0f, max.y, max.z, Mathf.Min(max.w, DryShine)));

    const float DryShine = 0.2f;

    public void Tint(System.Func<string, Color> tintOf)
    {
        tints.Clear();
        foreach (var (wet, _, _, _, _, _) in layers.Values) tints[wet] = tintOf(wet.name);
        written = -1f;
    }

    public void Advance(int rain, float dt)
    {
        float target = Mathf.Clamp01(rain / Soaking);
        float rate = target > shown ? 1f / SoakSeconds : 1f / DrySeconds;
        shown = Mathf.MoveTowards(shown, target, rate * dt);
        if (written >= 0f && Mathf.Abs(shown - written) < Step && !(shown == 0f && written > 0f)) return;
        written = shown;
        foreach (var (wet, tint, smoothness, maskMin, maskMax, gloss) in layers.Values)
        {
            float dark = Mathf.Lerp(1f, DarkWet, shown);
            var own = tints.TryGetValue(wet, out var looks) ? looks : Color.white;
            wet.diffuseRemapMax = new Vector4(tint.x * dark * own.r, tint.y * dark * own.g, tint.z * dark * own.b, tint.w);
            wet.smoothness = Mathf.Lerp(smoothness, Mathf.Max(smoothness, gloss), shown);
            wet.maskMapRemapMin = new Vector4(maskMin.x, maskMin.y, maskMin.z, Mathf.Lerp(maskMin.w, Mathf.Max(maskMin.w, gloss * 0.8f), shown));
            wet.maskMapRemapMax = new Vector4(maskMax.x, maskMax.y, maskMax.z, Mathf.Lerp(maskMax.w, Mathf.Max(maskMax.w, gloss), shown));
        }
    }

    public void Destroy()
    {
        foreach (var (wet, _, _, _, _, _) in layers.Values) Object.Destroy(wet);
        layers.Clear();
    }
}
