using System.Collections.Generic;
using UnityEngine;

// The grid's materials, one per name, made on first use and shared by every
// cell that asks for that name (the first request's colour wins). A per-cell
// shade is a copy of one of them in that shade, rounded so that cells of near
// shades share it (Shaded, for CellDressing.Paint): a property block per
// renderer would keep the dressing out of the SRP Batcher and the GPU Resident
// Drawer, one draw and one SetPass call each (#201). The Resident Drawer ignores
// the Default layer's 500 m cull distance (FarRing.NearMetres), so PC_RPAsset's
// small-mesh cull (0.4% of the screen) stands in for it: a 0.7 m brush sphere
// stops drawing about 500 m off, as before.
// Owns what it makes: Destroy() frees them all.
public sealed class MaterialCache
{
    const string WaterName = "Water";
    /// <summary>Shades are rounded to 1/64 a channel (half a step is 2 of 255: not seen).</summary>
    const float ShadeSteps = 64f;
    /// <summary>Past this many shaded copies a shade goes back to a property block.</summary>
    const int MostShades = 4096;
    static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

    readonly Dictionary<string, Material> materials = new();
    readonly Dictionary<(Material plain, int r, int g, int b, int a), Material> shades = new();
    Texture2D ripples;

    public Material Lit(string name, Color color, float smoothness, Color? emission = null)
    {
        if (materials.TryGetValue(name, out var m)) return m;
        m = UrpMaterials.NewLit(name, color, smoothness, emission);
        materials[name] = m;
        return m;
    }

    /// <summary>
    /// <paramref name="plain"/> in <paramref name="color"/> rounded to a shade step, one copy per shade, or null when there
    /// are too many shades already (the caller tints with a property block then).
    /// </summary>
    public Material Shaded(Material plain, Color color)
    {
        if (plain == null) return null;
        var key = (plain, Step(color.r), Step(color.g), Step(color.b), Step(color.a));
        if (shades.TryGetValue(key, out var m) && m != null) return m;
        if (shades.Count >= MostShades) return null;
        m = new Material(plain) { name = plain.name + " shade" };
        m.SetColor(BaseColorId, new Color(key.Item2 / ShadeSteps, key.Item3 / ShadeSteps, key.Item4 / ShadeSteps, key.Item5 / ShadeSteps));
        shades[key] = m;
        return m;
    }

    static int Step(float channel) => Mathf.RoundToInt(channel * ShadeSteps);

    /// <summary>How many shaded copies there are.</summary>
    public int ShadeCount => shades.Count;

    /// <summary>A metallic Lit material, one per name like Lit.</summary>
    public Material Metal(string name, Color color, float smoothness, float metallic)
    {
        if (materials.TryGetValue(name, out var m)) return m;
        m = Lit(name, color, smoothness);
        m.SetFloat("_Metallic", metallic);
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

    /// <summary>A glossy film coloured by its mesh's vertex colours (CellSlickMesh), if the scene has none.</summary>
    public Material Slick()
    {
        const string name = "Slick";
        if (materials.TryGetValue(name, out var m)) return m;
        m = new Material(Shader.Find(UrpMaterials.LitShader)) { name = name };
        UrpMaterials.ConfigureSoftSurface(m, Color.white, UrpMaterials.FilmSmoothness);
        materials[name] = m;
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
        foreach (var m in shades.Values) Object.Destroy(m);
        shades.Clear();
        if (ripples != null) Object.Destroy(ripples);
    }
}
