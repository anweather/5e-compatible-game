using UnityEngine;

// The shore map (docs/GLOSSARY.md): over the sea and out past the terrain, the
// way to the nearest land and how far off it is. ValleyBuilder bakes it
// (ShoreMapBaker) into a texture, red and green the way to land (a metre per
// metre, shrinking toward nothing where two shores are about as near), blue the
// signed distance to the shore in metres (negative on land). The Ocean shader
// reads it for the waves, WaterCurrents and Ocean.WaveOffset through At, so the
// push and the waves a swimmer rides run along the waves that are drawn. A view
// of the land: nothing here touches the chemistry (D4).
public class ShoreMap : ScriptableObject
{
    /// <summary>How far in from the map's edges the waves die away (m), so they don't end at a line.</summary>
    public const float FadeMetres = 300f;

    public Texture2D texture;
    [Tooltip("The map's south-west corner (x, z).")]
    public Vector2 origin;
    [Tooltip("The side of the square it covers (m).")]
    public float size = 1f;

    /// <summary>For the shader: the corner in xy, one over the side in z, how many fades fit the side in w.</summary>
    public Vector4 Rect => new(origin.x, origin.y, 1f / size, size / FadeMetres);

    /// <summary>
    /// A point's way to the nearest land (level, at most 1 long), its signed distance from the shore
    /// (m, negative on land), and how much of the waves reach it (1 to 0 toward the map's edge):
    /// what the shader samples, with the texture's own filtering.
    /// </summary>
    public (Vector2 toLand, float distance, float reach) At(Vector2 xz)
    {
        var uv = (xz - origin) / size;
        // GetPixelBilinear has pixel i at i / width; a GPU's filtering at (i + 0.5) / width.
        float halfTexel = 0.5f / texture.width;
        var c = texture.GetPixelBilinear(uv.x - halfTexel, uv.y - halfTexel);
        float edge = Mathf.Clamp01(Mathf.Min(Mathf.Min(uv.x, 1f - uv.x), Mathf.Min(uv.y, 1f - uv.y)) * size / FadeMetres);
        return (new Vector2(c.r, c.g), c.b, edge * edge * (3f - 2f * edge));
    }
}
