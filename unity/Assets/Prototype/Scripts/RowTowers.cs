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
    const float StumpShare = 0.3f, Broken = 0.8f;
    const int Sides = 28;
    static readonly Color Cover = new(0.27f, 0.29f, 0.34f);

    /// <summary>
    /// Draws the row's towers, a stretch each that stands on all its cells (RowRuin takes it down when enough of them have gone).
    /// Each wall is its line's two ends and a point a thickness to its right, all at y 0.
    /// </summary>
    public static List<RowMesher.Stretch> Draw(MeshKit body, MeshKit walk, JObject row, Dictionary<string, Vector2> joins, List<(Vector3 from, Vector3 to, Vector3 right)> walls, float walkTop, float footing, Color color)
    {
        var made = new List<RowMesher.Stretch>();
        if (!(row["towers"] is JObject towers)) return made;
        foreach (var t in towers.Properties().OrderBy(p => p.Name, System.StringComparer.Ordinal))
        {
            var at = joins[(string)t.Value["at"]];
            int half = (int)t.Value["radius"];
            float radius = half * HalfCell, top = (float?)t.Value["height"] ?? walkTop + 4f;
            var middle = new Vector3(at.x, 0f, at.y);
            var s = new RowMesher.Stretch { Group = CellsOf((int)Mathf.Round(at.x / HalfCell), (int)Mathf.Round(-at.y / HalfCell), half), Middle = middle };
            s.Place = s.Group[s.Group.Count / 2];
            s.Whole.Add(RowMesher.Record(body, () =>
            {
                body.Cylinder(middle + Vector3.down * footing, middle + Vector3.up * walkTop, radius, Sides, color);
                if ((string)t.Value["roof"] == "cone") body.Cone(middle + Vector3.up * top, radius + Eaves, radius * ConeRise, Sides, Cover);
                else Crown(body, middle, radius, top, color);
            }));
            s.Whole.Add(RowMesher.Record(body, () => Drum(body, middle, radius, walkTop, top, walls, color)));
            s.Walk.Add(RowMesher.Record(walk, () => Drum(walk, middle, radius, walkTop, top, walls, Color.clear)));
            s.Left = RowMesher.Record(body, () => body.Cylinder(middle + Vector3.down * footing, middle + Vector3.up * walkTop * StumpShare, radius, Sides, color * Broken));
            made.Add(s);
        }
        return made;
    }

    // The cells BuildingRaster.LayTowers takes, from the row's at cell: a cell's middle within the radius of the join, in half cells.
    static List<Vector2Int> CellsOf(int x, int y, int radius)
    {
        var cells = new List<Vector2Int>();
        for (int cy = (y - radius - 1) / 2 - 1; cy <= (y + radius) / 2 + 1; cy++)
            for (int cx = (x - radius - 1) / 2 - 1; cx <= (x + radius) / 2 + 1; cx++)
            {
                int dx = 2 * cx + 1 - x, dy = 2 * cy + 1 - y;
                if (dx * dx + dy * dy <= radius * radius) cells.Add(new Vector2Int(cx, cy));
            }
        return cells;
    }

    // The wall above the walk, a piece a side: where a wall comes in, only what's over the arch.
    static void Drum(MeshKit kit, Vector3 middle, float radius, float walkTop, float top, List<(Vector3 from, Vector3 to, Vector3 right)> walls, Color color)
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
            kit.Box(centre, size, color, turn);
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
