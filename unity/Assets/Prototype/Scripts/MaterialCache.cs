using System.Collections.Generic;
using UnityEngine;

// The grid's materials, one per name, made on first use and shared by every
// cell that asks for that name (the first request's colour wins; per-cell
// shades go through property blocks instead, see GridTileBuilder.Paint).
// Owns what it makes: Destroy() frees them all.
public sealed class MaterialCache
{
    const string WaterName = "Water";

    readonly Dictionary<string, Material> materials = new();
    Texture2D ripples;

    public Material Lit(string name, Color color, float smoothness, Color? emission = null)
    {
        if (materials.TryGetValue(name, out var m)) return m;
        m = UrpMaterials.NewLit(name, color, smoothness, emission);
        materials[name] = m;
        return m;
    }

    /// <summary>See-through water with a rippling normal map that <see cref="ScrollWater"/> drifts.</summary>
    public Material Water()
    {
        if (materials.TryGetValue(WaterName, out var m)) return m;
        m = Lit(WaterName, new Color(0.16f, 0.42f, 0.62f, 0.62f), 0.95f);
        ripples = ProceduralTextures.RippleNormals(128);
        UrpMaterials.SetNormalMap(m, ripples, 0.2f);
        UrpMaterials.MakeTransparent(m);
        return m;
    }

    /// <summary>Drifts the water's ripples; call once a frame.</summary>
    public void ScrollWater(float time)
    {
        if (materials.TryGetValue(WaterName, out var water))
            water.SetTextureOffset("_BaseMap", new Vector2(time * 0.03f, time * 0.02f));
    }

    public void Destroy()
    {
        foreach (var m in materials.Values) Object.Destroy(m);
        if (ripples != null) Object.Destroy(ripples);
    }
}
