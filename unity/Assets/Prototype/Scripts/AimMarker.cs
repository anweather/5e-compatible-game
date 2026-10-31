using System.Collections.Generic;
using Chem;
using UnityEngine;

/// <summary>The kinds of aim marker (GLOSSARY), each from a spell's shape: <see cref="AimMarker.KindOf"/>.</summary>
public enum AimKind
{
    /// <summary>Nothing on the ground to mark: the sky spells.</summary>
    None,
    /// <summary>An arrow along the aim, at any angle: a line from the caster.</summary>
    Lane,
    /// <summary>A fan along the aim: a cone.</summary>
    Wedge,
    /// <summary>A ring that grows with the charge: what strikes all at once, and a single spot.</summary>
    Ring,
    /// <summary>A glowing fence round the cells: what rises from below.</summary>
    Rising,
    /// <summary>Marching ants round the cells: what settles over an area and stays.</summary>
    Area,
    /// <summary>Brackets round one target.</summary>
    Lock,
}

// Where a spell will land, on the ground (SpellCaster): one mesh, redrawn only
// when the aim changes, drawn by Prototype/AimMarker, whose stripes, marching
// dashes and pulse are all the GPU's. Everything is ribbons laid over the drawn
// ground and over water, so it lies on hills and ponds as it does on flat land;
// hidden parts show faintly through what covers them. Only the show.
public sealed class AimMarker
{
    const float Lift = 0.08f, Spacing = 0.35f, HalfWidth = 0.09f, Dash = 0.9f;
    // A little wider far off, so a thin line still reads across a field.
    const float FarFrom = 12f, MostWidening = 2.5f;
    const float FenceHeight = 0.6f, FenceAlpha = 0.35f;
    const float GrowRate = 6f;
    const int RingPoints = 56, ArcPoints = 18;
    const float MostMitre = 2.5f;

    readonly ChemGrid grid;
    readonly Material material;
    readonly Mesh mesh;
    readonly GameObject shown;

    readonly List<Vector3> vertices = new();
    readonly List<Color> colours = new();
    readonly List<Vector3> uvs = new();
    readonly List<int> triangles = new();
    readonly List<Vector2> points = new();
    readonly List<Vector2> line = new();
    readonly List<float> heights = new();
    readonly HashSet<(int x, int y)> inside = new();

    (string spell, int x, int y, int dir, int aimX, int aimY, int charge, int tick, int count, int widening) drawn;
    float ringAim, half;
    Color tint;

    /// <summary>The kind on the ground now (None when hidden), for the probe.</summary>
    public AimKind Kind { get; private set; }
    /// <summary>The ring's radius as drawn (m), easing to its size, for the probe.</summary>
    public float Radius { get; private set; }
    /// <summary>How many vertices the one mesh has, for the probe.</summary>
    public int Vertices => vertices.Count;
    /// <summary>The drawing object, for the probe's check that one mesh is all there is.</summary>
    public GameObject Shown => shown;

    public AimMarker(ChemGrid grid, Shader shader)
    {
        this.grid = grid;
        if (shader == null) return;
        material = new Material(shader);
        mesh = new Mesh { name = "Aim marker" };
        mesh.MarkDynamic();
        shown = new GameObject("Aim marker", typeof(MeshFilter), typeof(MeshRenderer));
        shown.GetComponent<MeshFilter>().sharedMesh = mesh;
        var r = shown.GetComponent<MeshRenderer>();
        r.sharedMaterial = material;
        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        r.receiveShadows = false;
        shown.SetActive(false);
    }

    /// <summary>
    /// The marker for a spell, from its shape: a line is a lane, unless it raises the ground where it's
    /// aimed; a burst that strikes at once (it hurts or shoves) is a ring, like a bolt's spot; earth
    /// shaping and a spell that needs what's underneath (Geyser) rise; any other patch is an area.
    /// </summary>
    public static AimKind KindOf(SpellDef s)
    {
        if (s == null || World.IsSkySpell(s)) return AimKind.None;
        bool shaping = s.Effect == "terraform";
        switch (s.Shape)
        {
            case "target": return AimKind.Lock;
            case "cone": return AimKind.Wedge;
            case "line": return shaping && s.Origin != "self" ? AimKind.Rising : AimKind.Lane;
            case "point": return AimKind.Ring;
        }
        if (shaping || s.Needs != null) return AimKind.Rising;
        return s.Shape == "burst" && (s.Damage is { Length: > 0 } || s.Push != 0) ? AimKind.Ring : AimKind.Area;
    }

    static readonly Unity.Profiling.ProfilerMarker RedrawMarker = new("AimMarker.Redraw");

    public void Hide()
    {
        if (shown != null) shown.SetActive(false);
        Kind = AimKind.None;
        Radius = 0f;
    }

    /// <summary>
    /// Shows a cast's marker: the spell, the cells it would touch, where it's aimed (the core's
    /// (x, y), its way and its aim off the grid's four ways) and how charged it is.
    /// </summary>
    public void Show(SpellDef s, List<(int x, int y)> cells, CastCommand cast, Color colour)
    {
        var kind = KindOf(s);
        if (shown == null || kind == AimKind.None || cells == null || cells.Count == 0)
        {
            Hide();
            return;
        }
        half = grid.cellSize / 2f;
        ringAim = kind == AimKind.Ring ? RingRadius(s, cast.Charge) : 0f;
        Radius = Kind == AimKind.Ring ? Mathf.MoveTowards(Radius, ringAim, GrowRate * Time.deltaTime) : ringAim;
        int widening = Mathf.RoundToInt(Widening(cast) * 4f);
        var now = (s.Id, cast.X, cast.Y, cast.Dir, cast.AimX, cast.AimY, cast.Charge, grid.World.Tick, cells.Count, widening);
        if (kind == Kind && now == drawn && Mathf.Approximately(Radius, ringAim)) return;
        drawn = now;
        Kind = kind;
        using var marked = RedrawMarker.Auto();
        PerfCounts.Add("aim.redraws");
        tint = Bright(colour);
        Clear();
        float width = HalfWidth * widening / 4f;
        switch (kind)
        {
            case AimKind.Lane: Lane(s, cells, cast, width); break;
            case AimKind.Wedge: Wedge(cells, cast, width); break;
            case AimKind.Ring: Ring(Middle(cast), width); break;
            case AimKind.Rising: Outline(cells, width, 0f, FenceHeight); break;
            case AimKind.Area: Outline(cells, width * 1.3f, Dash, 0f); break;
            case AimKind.Lock: Brackets(Middle(cast), width); break;
        }
        Upload();
        shown.SetActive(true);
    }

    public void Destroy()
    {
        Object.Destroy(shown);
        Object.Destroy(mesh);
        Object.Destroy(material);
    }

    float RingRadius(SpellDef s, int charge) => (s.Radius + World.ChargedGrowth(s, charge) + 0.5f) * grid.cellSize;

    float Widening(CastCommand cast)
    {
        var camera = Camera.main;
        if (camera == null) return 1f;
        float distance = Vector3.Distance(camera.transform.position, grid.CellCenter(cast.X, cast.Y));
        return Mathf.Clamp(distance / FarFrom, 1f, MostWidening);
    }

    // The spell's colour at full brightness, softened towards white so it reads on anything.
    static Color Bright(Color c)
    {
        float most = Mathf.Max(c.r, Mathf.Max(c.g, c.b), 0.001f);
        return Color.Lerp(new Color(c.r / most, c.g / most, c.b / most), Color.white, 0.2f).linear;
    }

    Vector2 Middle(CastCommand cast) => Flat(grid.CellCenter(cast.X, cast.Y));

    static Vector2 Flat(Vector3 p) => new(p.x, p.z);

    // Which way a cast points on the ground: the aim, or else its grid way (x east, y north on the ground).
    static Vector2 Heading(CastCommand cast)
    {
        var (x, y) = World.SnapAim(cast.AimX, cast.AimY);
        if (x != 0 || y != 0) return new Vector2(x, -y).normalized;
        return cast.Dir switch { 0 => Vector2.up, 1 => Vector2.right, 2 => Vector2.down, _ => Vector2.left };
    }

    // An arrow from the first cell to the last along the aim, as wide as the spell, with a dashed middle.
    void Lane(SpellDef s, List<(int x, int y)> cells, CastCommand cast, float width)
    {
        var from = Middle(cast);
        var way = Heading(cast);
        var side = Vector2.Perpendicular(way);
        var (near, far) = (float.MaxValue, float.MinValue);
        foreach (var (x, y) in cells)
        {
            float along = Vector2.Dot(Flat(grid.CellCenter(x, y)) - from, way);
            near = Mathf.Min(near, along - half);
            far = Mathf.Max(far, along + half);
        }
        // A wave's front is its radius each side, and grows with the charge (World.Waves).
        float wide = (s.Radius + (s.Radius > 0 ? World.ChargedGrowth(s, cast.Charge) : 0) + 0.5f) * grid.cellSize;
        float head = Mathf.Min(grid.cellSize * 1.2f, (far - near) * 0.45f);
        var (start, tip) = (from + way * near, from + way * far);
        var neck = tip - way * head;
        Begin(start + side * wide, neck + side * wide, neck + side * (wide + half), tip, neck - side * (wide + half), neck - side * wide, start - side * wide);
        Path(true, width, 0f);
        Begin(start, neck);
        Path(false, width * 0.7f, Dash);
    }

    // A fan from the first cell, widening a cell each side per cell out, as the core's cone does.
    void Wedge(List<(int x, int y)> cells, CastCommand cast, float width)
    {
        var from = Middle(cast);
        var way = Heading(cast);
        float reach = 0f;
        foreach (var (x, y) in cells) reach = Mathf.Max(reach, Vector2.Dot(Flat(grid.CellCenter(x, y)) - from, way) + half);
        points.Clear();
        points.Add(from);
        float start = Mathf.Atan2(way.y, way.x);
        for (int i = 0; i <= ArcPoints; i++)
        {
            float a = start + Mathf.Lerp(-Mathf.PI / 4f, Mathf.PI / 4f, i / (float)ArcPoints);
            points.Add(from + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * reach);
        }
        Path(true, width, 0f);
        Begin(from, from + way * reach);
        Path(false, width * 0.7f, Dash);
    }

    // The ring, with a dashed one inside it running the other way round if there's room.
    void Ring(Vector2 at, float width)
    {
        Circle(at, Radius);
        Path(true, width * 1.3f, 0f);
        if (Radius < grid.cellSize) return;
        Circle(at, Radius * 0.6f);
        Path(true, width * 0.7f, -Dash);
    }

    void Circle(Vector2 at, float radius)
    {
        points.Clear();
        for (int i = 0; i < RingPoints; i++)
        {
            float a = i * Mathf.PI * 2f / RingPoints;
            points.Add(at + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * radius);
        }
    }

    // The cells' outer edges, clockwise from above so dashes march round, each edge a ribbon of its own.
    void Outline(List<(int x, int y)> cells, float width, float dash, float fence)
    {
        inside.Clear();
        foreach (var cell in cells) inside.Add(cell);
        foreach (var (x, y) in cells)
        {
            var c = Flat(grid.CellCenter(x, y));
            Vector2 nw = c + new Vector2(-half, half), ne = c + new Vector2(half, half), se = c + new Vector2(half, -half), sw = c + new Vector2(-half, -half);
            if (!inside.Contains((x, y - 1))) Edge(nw, ne, width, dash, fence);
            if (!inside.Contains((x + 1, y))) Edge(ne, se, width, dash, fence);
            if (!inside.Contains((x, y + 1))) Edge(se, sw, width, dash, fence);
            if (!inside.Contains((x - 1, y))) Edge(sw, nw, width, dash, fence);
        }
    }

    void Edge(Vector2 from, Vector2 to, float width, float dash, float fence)
    {
        var way = (to - from).normalized;
        // Run on by the ribbon's half width, so corners meet; each edge's dashes go by where it lies, so they join up.
        Begin(from - way * width, to + way * width);
        Path(false, width, dash, Vector2.Dot(from, way) - width);
        if (fence > 0f) Fence(fence, from, to);
    }

    // A short curtain up from the last path, fading out as it rises; it stops at the edge's corners.
    void Fence(float height, Vector2 from, Vector2 to)
    {
        int first = vertices.Count, last = line.Count - 1;
        for (int i = 0; i <= last; i++)
        {
            var foot = line[i];
            if (i == 0) foot = from;
            if (i == last) foot = to;
            var at = new Vector3(foot.x, heights[i], foot.y);
            Put(at, 0f, 0f, 0f, FenceAlpha);
            Put(at + Vector3.up * height, 0f, 0f, 0f, 0f);
            if (i > 0) Quad(first + (i - 1) * 2);
        }
    }

    // Four corners, round the target and a cell and a half across.
    void Brackets(Vector2 at, float width)
    {
        float r = grid.cellSize * 0.75f, leg = grid.cellSize * 0.32f;
        for (int i = 0; i < 4; i++)
        {
            var (a, b) = (i is 0 or 3 ? 1f : -1f, i < 2 ? 1f : -1f);
            var corner = at + new Vector2(a, b) * r;
            Begin(corner - new Vector2(a, 0f) * leg, corner, corner - new Vector2(0f, b) * leg);
            Path(false, width * 1.3f, 0f);
        }
    }

    void Begin(params Vector2[] path)
    {
        points.Clear();
        points.AddRange(path);
    }

    // A ribbon along the points, cut every little way so it follows the ground. A negative dash marches backwards.
    void Path(bool closed, float width, float dash, float start = 0f)
    {
        Resample(closed);
        int first = vertices.Count, last = line.Count - 1;
        float along = start;
        heights.Clear();
        for (int i = 0; i <= last; i++)
        {
            var side = Side(i, closed) * width;
            float y = Top(line[i]);
            heights.Add(y);
            var (left, right) = (line[i] + side, line[i] - side);
            Put(new Vector3(left.x, y, left.y), along, 1f, dash, 1f);
            Put(new Vector3(right.x, y, right.y), along, -1f, dash, 1f);
            if (i > 0) Quad(first + (i - 1) * 2);
            if (i < last) along += Vector2.Distance(line[i], line[i + 1]);
        }
    }

    // From a point of the line to the ribbon's side, a unit wide, mitred so a corner keeps its width.
    Vector2 Side(int i, bool closed)
    {
        int last = line.Count - 1, before = Mathf.Max(i - 1, 0), after = Mathf.Min(i + 1, last);
        // A closed line's two ends are one point: it runs on from the other end.
        if (closed && i == 0) before = last - 1;
        if (closed && i == last) after = 1;
        var into = (line[i] - line[before]).normalized;
        var outOf = (line[after] - line[i]).normalized;
        if (into == Vector2.zero) into = outOf;
        if (outOf == Vector2.zero) outOf = into;
        var side = Vector2.Perpendicular((into + outOf).normalized);
        return side * Mathf.Min(MostMitre, 1f / Mathf.Max(0.2f, Vector2.Dot(side, Vector2.Perpendicular(into))));
    }

    void Resample(bool closed)
    {
        line.Clear();
        int n = points.Count, spans = closed ? n : n - 1;
        for (int i = 0; i < spans; i++)
        {
            var (a, b) = (points[i], points[(i + 1) % n]);
            int steps = Mathf.Max(1, Mathf.CeilToInt(Vector2.Distance(a, b) / Spacing));
            for (int k = 0; k < steps; k++) line.Add(Vector2.Lerp(a, b, k / (float)steps));
        }
        line.Add(closed ? points[0] : points[n - 1]);
    }

    // The top of the ground as drawn, or of the water on it.
    float Top(Vector2 p)
    {
        var at = new Vector3(p.x, 0f, p.y);
        float ground = grid.GroundAt(at);
        var cell = grid.CellAt(at);
        var c = grid.World.At(cell.x, cell.y);
        if (c == null) return ground + Lift;
        var middle = grid.CellCenter(cell.x, cell.y);
        float gx = cell.x + 0.5f + (p.x - middle.x) / grid.cellSize, gy = cell.y + 0.5f - (p.y - middle.z) / grid.cellSize;
        return Mathf.Max(ground, grid.DrawnWaterTopAt(gx, gy, ground), grid.LakeLevelAt(cell.x, cell.y, c)) + Lift;
    }

    void Put(Vector3 at, float along, float across, float dash, float alpha)
    {
        vertices.Add(at);
        uvs.Add(new Vector3(along, across, dash));
        colours.Add(new Color(tint.r, tint.g, tint.b, alpha));
    }

    void Quad(int a)
    {
        triangles.Add(a);
        triangles.Add(a + 2);
        triangles.Add(a + 1);
        triangles.Add(a + 1);
        triangles.Add(a + 2);
        triangles.Add(a + 3);
    }

    void Clear()
    {
        vertices.Clear();
        colours.Clear();
        uvs.Clear();
        triangles.Clear();
    }

    void Upload()
    {
        mesh.Clear();
        mesh.indexFormat = vertices.Count > 60000 ? UnityEngine.Rendering.IndexFormat.UInt32 : UnityEngine.Rendering.IndexFormat.UInt16;
        mesh.SetVertices(vertices);
        mesh.SetColors(colours);
        mesh.SetUVs(0, uvs);
        mesh.SetTriangles(triangles, 0);
        mesh.RecalculateBounds();
    }
}
