using UnityEngine;
using UnityEngine.Rendering;

// Materials on URP's Lit shader made from code, shared by the runtime views
// (the grid, the aim markers, the valley's charred stumps) and the editor
// builders (which save them as assets). One place for the shader's property
// names and for the steps that turn Lit into a transparent surface.
public static class UrpMaterials
{
    public const string LitShader = "Universal Render Pipeline/Lit";

    /// <summary>A new Lit material. <paramref name="smoothness"/> null keeps the shader's default.</summary>
    public static Material NewLit(string name, Color color, float? smoothness = null, Color? emission = null)
    {
        var m = new Material(Shader.Find(LitShader)) { name = name };
        Configure(m, color, smoothness, emission);
        return m;
    }

    /// <summary>Sets a Lit material's colour, and optionally its smoothness and emission.</summary>
    public static void Configure(Material m, Color color, float? smoothness = null, Color? emission = null)
    {
        m.SetColor("_BaseColor", color);
        if (smoothness.HasValue) m.SetFloat("_Smoothness", smoothness.Value);
        if (!emission.HasValue) return;
        m.EnableKeyword("_EMISSION");
        m.SetColor("_EmissionColor", emission.Value);
    }

    /// <summary>Switches a Lit material to a transparent, alpha-blended surface (as the inspector's Surface Type does).</summary>
    public static void MakeTransparent(Material m)
    {
        m.SetFloat("_Surface", 1f);
        m.SetFloat("_Blend", 0f);
        m.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
        m.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
        m.SetFloat("_ZWrite", 0f);
        m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        m.renderQueue = (int)RenderQueue.Transparent;
        m.SetOverrideTag("RenderType", "Transparent");
    }

    public static void SetNormalMap(Material m, Texture normals, float scale)
    {
        m.SetTexture("_BumpMap", normals);
        m.SetFloat("_BumpScale", scale);
        m.EnableKeyword("_NORMALMAP");
    }

    /// <summary>Per-renderer colour through a property block, so many renderers can share one material.</summary>
    public static void SetColor(Renderer r, MaterialPropertyBlock block, Color color)
    {
        r.GetPropertyBlock(block);
        block.SetColor("_BaseColor", color);
        r.SetPropertyBlock(block);
    }
}
