using UnityEngine;
using UnityEngine.Rendering;

// Materials on URP's Lit shader made from code, shared by the runtime views
// (the grid, the aim markers, the valley's charred stumps) and the editor
// builders (which save them as assets). One place for the shader's property
// names and for the steps that turn Lit into a transparent surface.
public static class UrpMaterials
{
    public const string LitShader = "Universal Render Pipeline/Lit";
    // Lit like LitShader, but multiplied by the mesh's vertex colours (alpha too): soft-edged films.
    public const string VertexLitShader = "Universal Render Pipeline/Particles/Lit";

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
        // Without this flag URP's material validation drops _EMISSION again when a saved
        // material is imported (the flag starts as EmissiveIsBlack), and the glow is lost.
        m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
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

    /// <summary>
    /// Makes a material a see-through lit surface tinted by its mesh's vertex colours, alpha
    /// included, so a mesh can fade its own edges (water, slicks). Falls back to plain Lit (no
    /// vertex colours) if the shader isn't in the build.
    /// </summary>
    public static void ConfigureSoftSurface(Material m, Color color, float smoothness)
    {
        var shader = Shader.Find(VertexLitShader);
        m.shader = shader != null ? shader : Shader.Find(LitShader);
        Configure(m, color, smoothness);
        MakeTransparent(m);
        m.SetFloat("_SrcBlendAlpha", (float)BlendMode.One);
        m.SetFloat("_DstBlendAlpha", (float)BlendMode.OneMinusSrcAlpha);
    }

    /// <summary>
    /// How glossy a film on the ground is (oil, grease, mud, glass: CellSlickMesh). Near a mirror its
    /// sky reflection stays at full strength however see-through the film is, and hides its colour.
    /// </summary>
    public const float FilmSmoothness = 0.7f;

    public const string UnlitParticlesShader = "Universal Render Pipeline/Particles/Unlit";

    /// <summary>
    /// An unlit see-through material for particles and lines, tinted by their vertex colours:
    /// alpha-blended (rain) or added on top (a lightning bolt; a colour over 1 blooms).
    /// </summary>
    public static Material NewUnlitParticles(string name, Color color, bool additive)
    {
        var m = new Material(Shader.Find(UnlitParticlesShader)) { name = name };
        m.SetColor("_BaseColor", color);
        MakeTransparent(m);
        if (!additive) return m;
        m.SetFloat("_Blend", 2f);
        m.SetFloat("_DstBlend", (float)BlendMode.One);
        return m;
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
