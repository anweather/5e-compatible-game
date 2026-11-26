using UnityEngine;
using UnityEngine.Rendering;

// Materials on URP's Lit shader made from code, shared by the runtime views
// (the grid, the aim markers, the valley's charred stumps) and the editor
// builders (which save them as assets). One place for the shader's property
// names and for the steps that turn Lit into a transparent surface.
public static class UrpMaterials
{
    public const string LitShader = "Universal Render Pipeline/Lit";
    public const string VertexLitShader = "Universal Render Pipeline/Particles/Lit";

    public static Material NewLit(string name, Color color, float? smoothness = null, Color? emission = null)
    {
        var m = new Material(Shader.Find(LitShader)) { name = name };
        Configure(m, color, smoothness, emission);
        return m;
    }

    public static void Configure(Material m, Color color, float? smoothness = null, Color? emission = null)
    {
        m.SetColor("_BaseColor", color);
        if (smoothness.HasValue) m.SetFloat("_Smoothness", smoothness.Value);
        if (!emission.HasValue) return;
        m.EnableKeyword("_EMISSION");
        m.SetColor("_EmissionColor", emission.Value);
        m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
    }

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

    public static void ConfigureSoftSurface(Material m, Color color, float smoothness)
    {
        var shader = Shader.Find(VertexLitShader);
        m.shader = shader != null ? shader : Shader.Find(LitShader);
        Configure(m, color, smoothness);
        MakeTransparent(m);
        m.SetFloat("_SrcBlendAlpha", (float)BlendMode.One);
        m.SetFloat("_DstBlendAlpha", (float)BlendMode.OneMinusSrcAlpha);
    }

    public const float FilmSmoothness = 0.7f;

    public const string UnlitParticlesShader = "Universal Render Pipeline/Particles/Unlit";

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

    public static void SetColor(Renderer r, MaterialPropertyBlock block, Color color)
    {
        r.GetPropertyBlock(block);
        block.SetColor("_BaseColor", color);
        r.SetPropertyBlock(block);
    }
}
