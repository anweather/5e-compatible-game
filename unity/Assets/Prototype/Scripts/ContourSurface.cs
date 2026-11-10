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
    /// <summary>
    /// What an owner gives each point of a surface all at once, from cells it read once for the build
    /// (CellWaterMesh's tiles), in place of a delegate per attribute and per cell (#70).
    /// </summary>
    public interface IPoints
    {
        /// <summary>Whether the points have a flow and foam (uv1, uv2).</summary>
        bool Flows { get; }

        /// <summary>A point's height, colour and flow, as the delegates Build takes would give them.</summary>
        void Point(float gx, float gy, float value, out float height, out Color color, out Vector4 flow);
    }

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

    // Profiler markers: one set for a tile of a tiled surface (water, ice, slicks, glow, lava), one for a surface drawn
    // whole ("Film": none is now). The 865-1040 ms Contour.Film.Outline frames on the nine's forestload (#197) were the
    // lava's one mesh round every lava and ember cell, before #188 tiled it; the slicks were in tiles already.
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
    /// <paramref name="flow"/> optionally gives each point the water's flow (x, z in m/s, into uv1),
    /// its foam (z, into uv2.x) and how much it wears its colour's rgb (w, into uv2.y; 0 for water),
    /// from its blended value too.
    /// </summary>
    public void Build(RectInt cells, System.Func<int, int, float> value, System.Func<float, float, float> height,
        System.Func<float, float, float, Color> color, System.Func<int, int, float> wobble = null,
        System.Func<float, float, float, Vector4> flow = null)
    {
        Outline(cells, value, wobble);
        long from = System.Diagnostics.Stopwatch.GetTimestamp();
        using ((tiled ? TilePoints : FilmPoints).Auto()) Attributes(height, color, flow);
        PointsTicks = System.Diagnostics.Stopwatch.GetTimestamp() - from;
        Upload(flow != null);
    }

    /// <summary>The same, with each point's height, colour and flow from <paramref name="points"/>.</summary>
    public void Build(RectInt cells, System.Func<int, int, float> value, IPoints points, System.Func<int, int, float> wobble = null)
    {
        Outline(cells, value, wobble);
        long from = System.Diagnostics.Stopwatch.GetTimestamp();
        using ((tiled ? TilePoints : FilmPoints).Auto()) Attributes(points);
        PointsTicks = System.Diagnostics.Stopwatch.GetTimestamp() - from;
        Upload(points.Flows);
    }

    /// <summary>How long the last build took over its points, in Stopwatch ticks (for a check's timing).</summary>
    public long PointsTicks { get; private set; }

    /// <summary>The last build's vertices.</summary>
    public int Vertices => vertices.Count;

    string Count => tiled ? "tile" : "film";

    void Outline(RectInt cells, System.Func<int, int, float> value, System.Func<int, int, float> wobble)
    {
        vertices.Clear();
        colors.Clear();
        uvs.Clear();
        flows.Clear();
        foams.Clear();
        using ((tiled ? TileOutline : FilmOutline).Auto())
            contour.Build(cells.xMin, cells.yMin, cells.xMax - 1, cells.yMax - 1, value, shape, wobble);
        if (PerfCounts.On)
        {
            PerfCounts.Add(Count + ".box_cells", cells.width * cells.height);
            PerfCounts.Add(Count + ".verts", shape.Points.Count);
            PerfCounts.Add(Count + ".tris", shape.Triangles.Count / 3);
        }
    }

    void Upload(bool withFlow)
    {
        using var upload = (tiled ? TileUpload : FilmUpload).Auto();
        mesh.Clear();
        mesh.SetVertices(vertices);
        mesh.SetColors(colors);
        mesh.SetUVs(0, uvs);
        if (withFlow)
        {
            mesh.SetUVs(1, flows);
            mesh.SetUVs(2, foams);
        }
        mesh.SetTriangles(shape.Triangles, 0);
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        if (PerfCounts.On) CountSame(Count);
    }

    /// <summary>
    /// For a check: whether the last build's points (positions, colours, uvs, flows, foams) and
    /// triangles are, bit for bit, those of <paramref name="other"/>'s last build.
    /// </summary>
    public bool SamePointsAs(ContourSurface other)
    {
        if (vertices.Count != other.vertices.Count || flows.Count != other.flows.Count || shape.Triangles.Count != other.shape.Triangles.Count) return false;
        for (int n = 0; n < vertices.Count; n++)
            if (!SamePoint(n, other)) return false;
        for (int n = 0; n < shape.Triangles.Count; n++)
            if (shape.Triangles[n] != other.shape.Triangles[n]) return false;
        return true;
    }

    bool SamePoint(int n, ContourSurface other)
    {
        Vector3 a = vertices[n], b = other.vertices[n];
        Color ca = colors[n], cb = other.colors[n];
        bool same = Bits(a.x, b.x) && Bits(a.y, b.y) && Bits(a.z, b.z) && Bits(ca.r, cb.r) && Bits(ca.g, cb.g) && Bits(ca.b, cb.b) && Bits(ca.a, cb.a) &&
                    Bits(uvs[n].x, other.uvs[n].x) && Bits(uvs[n].y, other.uvs[n].y);
        return same && (n >= flows.Count || (Bits(flows[n].x, other.flows[n].x) && Bits(flows[n].y, other.flows[n].y) && Bits(foams[n].x, other.foams[n].x) && Bits(foams[n].y, other.foams[n].y)));
    }

    static bool Bits(float a, float b) => System.BitConverter.SingleToInt32Bits(a) == System.BitConverter.SingleToInt32Bits(b);

    // For a performance pass: whether this build drew what the last did (a redraw that changed nothing).
    long lastPicture;

    void CountSame(string count)
    {
        unchecked
        {
            long h = 1469598103934665603L;
            void Mix(float f) => h = (h ^ System.BitConverter.SingleToInt32Bits(f)) * 1099511628211L;
            for (int n = 0; n < vertices.Count; n++)
            {
                Mix(vertices[n].x); Mix(vertices[n].y); Mix(vertices[n].z); Mix(colors[n].a);
            }
            foreach (int t in shape.Triangles) h = (h ^ t) * 1099511628211L;
            PerfCounts.Add(count + (h == lastPicture ? ".unchanged" : ".changed"));
            PerfCounts.Add(count + ".noise_points", contour.NoisePoints);
            lastPicture = h;
        }
    }

    void Attributes(IPoints points)
    {
        var origin = grid.CellCenter(0, 0);
        float size = grid.cellSize;
        bool withFlow = points.Flows;
        for (int n = 0; n < shape.Points.Count; n++)
        {
            var (gx, gy) = shape.Points[n];
            var at = Flat(origin, size, gx, gy);
            points.Point(gx, gy, shape.Values[n], out at.y, out var color, out var f);
            vertices.Add(at);
            colors.Add(color);
            uvs.Add(new Vector2(at.x / uvMetres, at.z / uvMetres));
            if (!withFlow) continue;
            flows.Add(f);
            foams.Add(new Vector2(f.z, f.w));
        }
    }

    void Attributes(System.Func<float, float, float> height, System.Func<float, float, float, Color> color, System.Func<float, float, float, Vector4> flow)
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
            foams.Add(new Vector2(f.z, f.w));
        }
    }

    /// <summary>A point in grid units (cell (x, y) spans x..x+1, y..y+1) on the world's ground plane (y 0).</summary>
    public Vector3 Flat(float gx, float gy) => FlatOn(grid, gx, gy);

    /// <summary>The same, for a caller with no surface to hand.</summary>
    public static Vector3 FlatOn(ChemGrid grid, float gx, float gy) => Flat(grid.CellCenter(0, 0), grid.cellSize, gx, gy);

    /// <summary>The same, from cell (0, 0)'s centre and the cells' size, for a caller placing many points.</summary>
    public static Vector3 Flat(Vector3 origin, float cellSize, float gx, float gy) =>
        new(origin.x + (gx - 0.5f) * cellSize, 0f, origin.z - (gy - 0.5f) * cellSize);

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

    /// <summary>
    /// BlendAround over values read once: cell (x, y)'s at kept[(y - y0) * width + x - x0], NaN for
    /// none. The same sums in the same order, so the same value bit for bit; the four cells round the
    /// point must be in the kept box (InKept).
    /// </summary>
    public static float BlendKept(float[] kept, int x0, int y0, int width, float gx, float gy, float fallback) =>
        BlendKept(kept, (x0, y0, width), (gx, gy), fallback, (null, null), out _);

    /// <summary>
    /// The same, and with <paramref name="alsoX"/> and <paramref name="alsoY"/> (kept the same way,
    /// every cell having one) blended at the same point too, into <paramref name="also"/> (0 without
    /// them): one pass for a point's top and flow, each sum as BlendAround makes it.
    /// </summary>
    // Its sums stay in one body, as BlendAround's are: split into calls, Mono could round a step that it keeps here.
#pragma warning disable S3776
    public static float BlendKept(float[] kept, (int x0, int y0, int width) box, (float x, float y) at, float fallback,
        (float[] x, float[] y) flows, out Vector2 also)
    {
        float gx = at.x, gy = at.y;
        float[] alsoX = flows.x, alsoY = flows.y;
        float bx = gx - 0.5f, by = gy - 0.5f;
        int x = Mathf.FloorToInt(bx), y = Mathf.FloorToInt(by);
        float fx = bx - x, fy = by - y, sum = 0f, weight = 0f, sumX = 0f, sumY = 0f, weightXY = 0f;
        for (int dy = 0; dy <= 1; dy++)
        {
            int row = (y + dy - box.y0) * box.width + x - box.x0;
            for (int dx = 0; dx <= 1; dx++)
            {
                float w = Mathf.Max(1e-4f, (dx == 1 ? fx : 1f - fx) * (dy == 1 ? fy : 1f - fy));
                if (alsoX != null)
                {
                    sumX += w * alsoX[row + dx];
                    sumY += w * alsoY[row + dx];
                    weightXY += w;
                }
                float v = kept[row + dx];
                if (float.IsNaN(v)) continue;
                sum += w * v;
                weight += w;
            }
        }
        also = weightXY > 0f ? new Vector2(sumX / weightXY, sumY / weightXY) : Vector2.zero;
        return weight > 0f ? sum / weight : fallback;
    }
#pragma warning restore S3776

    /// <summary>Whether the four cells BlendKept reads round a point are in a kept box of <paramref name="width"/> × <paramref name="height"/> cells from (x0, y0).</summary>
    public static bool InKept(int x0, int y0, int width, int height, float gx, float gy)
    {
        int x = Mathf.FloorToInt(gx - 0.5f), y = Mathf.FloorToInt(gy - 0.5f);
        return x >= x0 && y >= y0 && x + 1 < x0 + width && y + 1 < y0 + height;
    }

    public void Destroy()
    {
        Object.Destroy(mesh);
        Object.Destroy(go);
    }
}
