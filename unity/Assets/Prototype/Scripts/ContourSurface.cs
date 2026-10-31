using System.Collections.Generic;
using Chem;
using UnityEngine;
using UnityEngine.Rendering;

// One mesh drawn over a patch of cells with a rounded, wobbly outline
// (CellContour): the cells' water, their ice and the oil and grease slicks
// (CellWaterMesh, CellSlickMesh). The owner says which cells are in, how high
// the surface stands and what colour (with alpha, for soft edges) each point
// gets; this turns the contour into a Unity mesh under the grid.
public sealed class ContourSurface
{
    public readonly Mesh mesh;
    public readonly GameObject go;
    readonly ChemGrid grid;
    readonly CellContour contour;
    readonly ContourMesh shape = new();
    readonly List<Vector3> vertices = new();
    readonly List<Color> colors = new();
    readonly List<Vector2> uvs = new();
    readonly List<Vector2> flows = new(), foams = new();
    readonly float uvMetres;
    readonly bool tiled;

    // Profiler markers: one set for water and ice (tiled), one for the slicks' one mesh.
    static readonly Unity.Profiling.ProfilerMarker TileOutline = new("Contour.Tile.Outline"), TilePoints = new("Contour.Tile.Points"), TileUpload = new("Contour.Tile.Upload"),
        FilmOutline = new("Contour.Film.Outline"), FilmPoints = new("Contour.Film.Points"), FilmUpload = new("Contour.Film.Upload");

    /// <param name="uvMetres">Metres per texture repeat (UVs are world x, z over this).</param>
    /// <param name="tiled">Draw one tile of a bigger surface (CellContour.Tiled): from the centre of the box's first cell to the centre of the cell past its last.</param>
    public ContourSurface(ChemGrid grid, string name, Material material, ContourSpec spec, float uvMetres, bool tiled = false)
    {
        this.grid = grid;
        this.uvMetres = uvMetres;
        this.tiled = tiled;
        contour = new CellContour(spec) { Tiled = tiled };
        mesh = new Mesh { name = name };
        mesh.MarkDynamic();
        go = new GameObject(name);
        go.transform.SetParent(grid.transform, false);
        go.AddComponent<MeshFilter>().sharedMesh = mesh;
        var r = go.AddComponent<MeshRenderer>();
        r.sharedMaterial = material;
        r.shadowCastingMode = ShadowCastingMode.Off;
    }

    /// <summary>True when the last Build drew nothing.</summary>
    public bool Empty => shape.Triangles.Count == 0;

    /// <summary>
    /// Rebuilds the mesh around cells (x0, y0)..(x1, y1): <paramref name="value"/> per cell (1 in,
    /// 0 out, see CellContour), <paramref name="height"/> and <paramref name="color"/> per point
    /// (grid units; the colour also gets the point's blended value, for fading the edge);
    /// <paramref name="wobble"/> optionally calms the outline's wobble per cell (see CellContour.Build);
    /// <paramref name="flow"/> optionally gives each point the water's flow (x, z in m/s, into uv1)
    /// and its foam (z, into uv2.x), from its blended value too.
    /// </summary>
    public void Build(RectInt cells, System.Func<int, int, float> value, System.Func<float, float, float> height,
        System.Func<float, float, float, Color> color, System.Func<int, int, float> wobble = null,
        System.Func<float, float, float, Vector3> flow = null)
    {
        vertices.Clear();
        colors.Clear();
        uvs.Clear();
        flows.Clear();
        foams.Clear();
        using ((tiled ? TileOutline : FilmOutline).Auto())
            contour.Build(cells.xMin, cells.yMin, cells.xMax - 1, cells.yMax - 1, value, shape, wobble);
        string count = tiled ? "tile" : "film";
        if (PerfCounts.On)
        {
            PerfCounts.Add(count + ".box_cells", cells.width * cells.height);
            PerfCounts.Add(count + ".verts", shape.Points.Count);
            PerfCounts.Add(count + ".tris", shape.Triangles.Count / 3);
        }
        using ((tiled ? TilePoints : FilmPoints).Auto()) Attributes(height, color, flow);
        using var upload = (tiled ? TileUpload : FilmUpload).Auto();
        mesh.Clear();
        mesh.SetVertices(vertices);
        mesh.SetColors(colors);
        mesh.SetUVs(0, uvs);
        if (flow != null)
        {
            mesh.SetUVs(1, flows);
            mesh.SetUVs(2, foams);
        }
        mesh.SetTriangles(shape.Triangles, 0);
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
    }

    void Attributes(System.Func<float, float, float> height, System.Func<float, float, float, Color> color, System.Func<float, float, float, Vector3> flow)
    {
        for (int n = 0; n < shape.Points.Count; n++)
        {
            var (gx, gy) = shape.Points[n];
            var at = Flat(gx, gy);
            at.y = height(gx, gy);
            vertices.Add(at);
            colors.Add(color(gx, gy, shape.Values[n]));
            uvs.Add(new Vector2(at.x / uvMetres, at.z / uvMetres));
            if (flow == null) continue;
            var f = flow(gx, gy, shape.Values[n]);
            flows.Add(f);
            foams.Add(new Vector2(f.z, 0f));
        }
    }

    /// <summary>A point in grid units (cell (x, y) spans x..x+1, y..y+1) on the world's ground plane (y 0).</summary>
    public Vector3 Flat(float gx, float gy)
    {
        var origin = grid.CellCenter(0, 0);
        return new Vector3(origin.x + (gx - 0.5f) * grid.cellSize, 0f, origin.z - (gy - 0.5f) * grid.cellSize);
    }

    /// <summary>
    /// A value blended between cell centres around a point (grid units), from the cells that
    /// have one; <paramref name="fallback"/> if none of the four around it does.
    /// </summary>
    public static float BlendAround(float gx, float gy, System.Func<int, int, float?> at, float fallback)
    {
        float bx = gx - 0.5f, by = gy - 0.5f;
        int x = Mathf.FloorToInt(bx), y = Mathf.FloorToInt(by);
        float fx = bx - x, fy = by - y, sum = 0f, weight = 0f;
        for (int dy = 0; dy <= 1; dy++)
        {
            for (int dx = 0; dx <= 1; dx++)
            {
                var v = at(x + dx, y + dy);
                if (!v.HasValue) continue;
                float w = Mathf.Max(1e-4f, (dx == 1 ? fx : 1f - fx) * (dy == 1 ? fy : 1f - fy));
                sum += w * v.Value;
                weight += w;
            }
        }
        return weight > 0f ? sum / weight : fallback;
    }

    public void Destroy()
    {
        Object.Destroy(mesh);
        Object.Destroy(go);
    }
}
