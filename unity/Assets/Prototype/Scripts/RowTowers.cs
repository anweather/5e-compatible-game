using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using UnityEngine;

// A row's round towers (docs/design/builders.md section 4). The cells are the core's (BuildingRaster.LayTowers); this draws a
// drum from the footing to its height, its top a rim a wizard on the wall walk passes through: an arch where a wall meets
// it, and a floor at the walk's height, which is the blockers' top. "roof": "cone" puts one on it; none, a crown of merlons.
public static class RowTowers
{
    const float HalfCell = 0.75f, Rim = 0.6f, ArchHeight = 2.6f, ConeRise = 1.15f, Eaves = 0.5f, MerlonHeight = 1.1f;
    const int Sides = 28;
    static readonly Color Cover = new(0.27f, 0.29f, 0.34f);

    /// <summary>Draws the row's towers. Each wall is its line's two ends and a point a thickness to its right, all at y 0.</summary>
    public static void Draw(MeshKit body, MeshKit walk, JObject row, Dictionary<string, Vector2> joins, List<(Vector3 from, Vector3 to, Vector3 right)> walls, float walkTop, float footing, float thickness, Color color)
    {
        if (!(row["towers"] is JObject towers)) return;
        foreach (var t in towers.Properties().OrderBy(p => p.Name, System.StringComparer.Ordinal))
        {
            var at = joins[(string)t.Value["at"]];
            float radius = (float)t.Value["radius"] * HalfCell, top = (float?)t.Value["height"] ?? walkTop + 4f;
            var middle = new Vector3(at.x, 0f, at.y);
            body.Cylinder(middle + Vector3.down * footing, middle + Vector3.up * walkTop, radius, Sides, color);
            Drum(body, walk, middle, radius, walkTop, top, walls, color);
            if ((string)t.Value["roof"] == "cone") body.Cone(middle + Vector3.up * top, radius + Eaves, radius * ConeRise, Sides, Cover);
            else Crown(body, middle, radius, top, color);
        }
    }

    // The wall above the walk, a piece a side: where a wall comes in, only what's over the arch.
    static void Drum(MeshKit body, MeshKit walk, Vector3 middle, float radius, float walkTop, float top, List<(Vector3 from, Vector3 to, Vector3 right)> walls, Color color)
    {
        float side = 2f * Mathf.PI * radius / Sides * 1.08f;
        for (int i = 0; i < Sides; i++)
        {
            float a = (i + 0.5f) * 2f * Mathf.PI / Sides;
            var out_ = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
            var at = middle + out_ * (radius - Rim / 2f);
            float from = walls.Any(w => OnWall(at, w)) ? walkTop + ArchHeight : walkTop;
            var turn = Quaternion.LookRotation(out_);
            var centre = at + Vector3.up * (from + top) / 2f;
            var size = new Vector3(side, top - from, Rim);
            body.Box(centre, size, color, turn);
            walk.Box(centre, size, Color.clear, turn);
        }
    }

    static bool OnWall(Vector3 p, (Vector3 from, Vector3 to, Vector3 right) w)
    {
        var along = w.to - w.from;
        var across = w.right - w.from;
        float u = Vector3.Dot(p - w.from, along.normalized), v = Vector3.Dot(p - w.from, across.normalized);
        return u >= -0.01f && u <= along.magnitude + 0.01f && v >= -0.01f && v <= across.magnitude + 0.01f;
    }

    static void Crown(MeshKit body, Vector3 middle, float radius, float top, Color color)
    {
        for (int i = 0; i < Sides; i += 2)
        {
            float a = (i + 0.5f) * 2f * Mathf.PI / Sides;
            var out_ = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
            body.Box(middle + out_ * (radius - Rim / 2f) + Vector3.up * (top + MerlonHeight / 2f), new Vector3(2f * Mathf.PI * radius / Sides, MerlonHeight, Rim), color, Quaternion.LookRotation(out_));
        }
    }
}
