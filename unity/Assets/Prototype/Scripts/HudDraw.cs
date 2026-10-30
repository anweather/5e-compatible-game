using UnityEngine;

// Round shapes for the HUD, in HUD units (HudScale), drawn as a few vertices
// instead of textures, so they stay sharp at any resolution: rings and discs
// with a soft edge, and the elements' icons. Only draws during a repaint.
public static class HudDraw
{
    // Vertices round a full circle, and how soft an edge is (screen pixels).
    const int Segments = 72;
    const float Feather = 1.5f;

    static Material material;

    static bool Begin()
    {
        if (Event.current.type != EventType.Repaint) return false;
        if (material == null)
        {
            var shader = Shader.Find("Hidden/Internal-Colored");
            if (shader == null) return false;
            material = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
            material.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
            material.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            material.SetInt("_Cull", (int)UnityEngine.Rendering.CullMode.Off);
            material.SetInt("_ZWrite", 0);
        }
        material.SetPass(0);
        GL.PushMatrix();
        GL.LoadPixelMatrix(0f, Screen.width, Screen.height, 0f);
        return true;
    }

    static void End() => GL.PopMatrix();

    /// <summary>A ring from the top clockwise, <paramref name="share"/> of the way round (1 for all of it).</summary>
    public static void Ring(Vector2 centre, float inner, float outer, float share, Color color)
    {
        if (share <= 0f || !Begin()) return;
        float s = HudScale.Factor;
        var c = centre * s;
        var (rIn, rOut) = (inner * s, outer * s);
        var clear = new Color(color.r, color.g, color.b, 0f);
        int n = Mathf.Max(1, Mathf.CeilToInt(Segments * Mathf.Clamp01(share)));
        GL.Begin(GL.QUADS);
        for (int i = 0; i < n; i++)
        {
            float a = Mathf.Clamp01(share) * i / n * Mathf.PI * 2f, b = Mathf.Clamp01(share) * (i + 1) / n * Mathf.PI * 2f;
            if (rIn > 0f)
            {
                Band(c, a, b, rIn - Feather, rIn, clear, color);
                Band(c, a, b, rIn, rOut, color, color);
            }
            else Band(c, a, b, 0f, rOut, color, color);
            Band(c, a, b, rOut, rOut + Feather, color, clear);
        }
        GL.End();
        End();
    }

    /// <summary>A filled circle.</summary>
    public static void Disc(Vector2 centre, float radius, Color color) => Ring(centre, 0f, radius, 1f, color);

    /// <summary>A filled triangle.</summary>
    public static void Triangle(Vector2 a, Vector2 b, Vector2 c, Color color)
    {
        if (!Begin()) return;
        float s = HudScale.Factor;
        GL.Begin(GL.TRIANGLES);
        GL.Color(color);
        GL.Vertex3(a.x * s, a.y * s, 0f);
        GL.Vertex3(b.x * s, b.y * s, 0f);
        GL.Vertex3(c.x * s, c.y * s, 0f);
        GL.End();
        End();
    }

    // One quad of a ring, between two angles (clockwise from the top) and two radii.
    static void Band(Vector2 c, float a, float b, float from, float to, Color inner, Color outer)
    {
        GL.Color(inner);
        Point(c, a, from);
        Point(c, b, from);
        GL.Color(outer);
        Point(c, b, to);
        Point(c, a, to);
    }

    static void Point(Vector2 c, float angle, float radius) =>
        GL.Vertex3(c.x + Mathf.Sin(angle) * radius, c.y - Mathf.Cos(angle) * radius, 0f);

    /// <summary>An element's colour, for its orb on the HUD and in the hands.</summary>
    public static Color ElementColor(string element) => element switch
    {
        ElementIds.Fire => new Color(0.95f, 0.42f, 0.12f),
        ElementIds.Water => new Color(0.2f, 0.52f, 0.95f),
        ElementIds.Earth => new Color(0.5f, 0.68f, 0.26f),
        ElementIds.Air => new Color(0.72f, 0.86f, 0.92f),
        _ => Color.gray,
    };

    /// <summary>A loaded element: its coloured circle with its icon on it.</summary>
    public static void ElementOrb(Vector2 at, float r, string element, float alpha = 1f)
    {
        Disc(at, r + 3f, new Color(0f, 0f, 0f, 0.6f * alpha));
        var color = ElementColor(element);
        color.a = alpha;
        Disc(at, r, color);
        var mark = new Color(1f, 1f, 1f, 0.95f * alpha);
        switch (element)
        {
            case ElementIds.Fire: Flame(at, r, mark); break;
            case ElementIds.Water: Drop(at, r, mark); break;
            case ElementIds.Earth: Peaks(at, r, mark); break;
            case ElementIds.Air: Gusts(at, r, mark); break;
        }
    }

    static void Flame(Vector2 at, float r, Color mark)
    {
        Disc(at + new Vector2(0f, r * 0.2f), r * 0.38f, mark);
        Triangle(at + new Vector2(-r * 0.36f, r * 0.13f), at + new Vector2(r * 0.36f, r * 0.13f), at + new Vector2(r * 0.08f, -r * 0.68f), mark);
    }

    static void Drop(Vector2 at, float r, Color mark)
    {
        Disc(at + new Vector2(0f, r * 0.2f), r * 0.38f, mark);
        Triangle(at + new Vector2(-r * 0.34f, r * 0.05f), at + new Vector2(r * 0.34f, r * 0.05f), at + new Vector2(0f, -r * 0.66f), mark);
    }

    static void Peaks(Vector2 at, float r, Color mark)
    {
        Triangle(at + new Vector2(-r * 0.68f, r * 0.45f), at + new Vector2(r * 0.25f, r * 0.45f), at + new Vector2(-r * 0.2f, -r * 0.55f), mark);
        Triangle(at + new Vector2(-r * 0.05f, r * 0.45f), at + new Vector2(r * 0.7f, r * 0.45f), at + new Vector2(r * 0.32f, -r * 0.2f), mark);
    }

    static void Gusts(Vector2 at, float r, Color mark)
    {
        for (int i = -1; i <= 1; i++)
        {
            var c = at + new Vector2(i * r * 0.08f, i * r * 0.42f - r * 0.2f);
            Ring(c, r * 0.3f, r * 0.42f, 0.5f, mark);
        }
    }
}
