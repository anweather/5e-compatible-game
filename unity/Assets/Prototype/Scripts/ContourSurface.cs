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
    public interface IPoints
    {
        bool Flows { get; }

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

    static readonly Unity.Profiling.ProfilerMarker TileOutline = new("Contour.Tile.Outline"), TilePoints = new("Contour.Tile.Points"), TileUpload = new("Contour.Tile.Upload"),
        FilmOutline = new("Contour.Film.Outline"), FilmPoints = new("Contour.Film.Points"), FilmUpload = new("Contour.Film.Upload");

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

    public bool Empty => shape.Triangles.Count == 0;

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

    public void Build(RectInt cells, System.Func<int, int, float> value, IPoints points, System.Func<int, int, float> wobble = null)
    {
        Outline(cells, value, wobble);
        long from = System.Diagnostics.Stopwatch.GetTimestamp();
        using ((tiled ? TilePoints : FilmPoints).Auto()) Attributes(points);
        PointsTicks = System.Diagnostics.Stopwatch.GetTimestamp() - from;
        Upload(points.Flows);
    }

    public long PointsTicks { get; private set; }

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
        var origin = grid.CellCenterUnwrapped(0, 0);
        float size = grid.cellSize;
        bool withFlow = points.Flows;
        for (int n = 0; n < shape.Points.Count; n++)
        {
            var (gx, gy) = shape.Points[n];
            var at = ViewWrap.Near(Flat(origin, size, gx, gy));
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

    public Vector3 Flat(float gx, float gy) => FlatOn(grid, gx, gy);

    public static Vector3 FlatOn(ChemGrid grid, float gx, float gy) => ViewWrap.Near(Flat(grid.CellCenterUnwrapped(0, 0), grid.cellSize, gx, gy));

    public static Vector3 Flat(Vector3 origin, float cellSize, float gx, float gy) =>
        new(origin.x + (gx - 0.5f) * cellSize, 0f, origin.z - (gy - 0.5f) * cellSize);

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

    public static float BlendKept(float[] kept, int x0, int y0, int width, float gx, float gy, float fallback) =>
        BlendKept(kept, (x0, y0, width), (gx, gy), fallback, (null, null), out _);

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
