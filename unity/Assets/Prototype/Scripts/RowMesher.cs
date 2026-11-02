using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using UnityEngine;

// A building from its parts (docs/design/builders.md section 4): the row's joins, walls with their
// openings, roofs and details as meshes, in metres from the north-west corner of the row's at cell,
// x east, z north, y up from the ground. The cells are the core's (BuildingRaster); this is only what
// they look like, so nothing here reaches the world. Walls run clockwise and stand on the right of
// their line, at the cells' outer edge. Where two meet they overlap rather than mitre: a wall reaches
// past a join only when the next turns away outward, so a corner is always closed.
// Roofs are one height field on a quarter-cell grid: a hip is each point's distance to the outline,
// counted in squares, which for walls on the axes is the true straight skeleton. So eaves overhang
// one grid step (0.375 m), and a 45° wall steps on the roof's edge.
public static class RowMesher
{
    const float HalfCell = 0.75f, Step = 0.375f, FloorSkirt = 0.5f, DoorDepth = 0.05f, PaneDepth = 0.08f, FlatRise = 0.04f;
    const float VigaRadius = 0.09f, VigaLength = 0.55f, CanaleLength = 0.7f, CanaleSize = 0.18f, ChimneySize = 0.6f, ChimneyAbove = 0.9f;
    const float PorchHeight = 2.3f, PorchPost = 0.12f, PorchRoof = 0.12f, BeamSize = 0.12f, ShutterWidth = 0.35f, PlanterDepth = 0.25f, PlanterHeight = 0.22f;
    const float MinRoofTiltAboveEaves = 0.01f, DefaultRun = 12f, TileShade = 0.86f, WornShade = 0.8f;
    const int ArchSlices = 20, VigaSides = 6;
    static readonly Color DefaultCover = new(0.32f, 0.3f, 0.32f), DoorColor = new(0.36f, 0.25f, 0.16f), Stone = new(0.45f, 0.44f, 0.42f), Timber = new(0.3f, 0.2f, 0.13f);

    /// <summary>What a row's drawing needs from the rules in force.</summary>
    public sealed class Tables
    {
        /// <summary>An opening kind's view numbers (width, height, sill); null if there's none.</summary>
        public Func<string, JToken> OpeningLook;
        /// <summary>A terrain's colour.</summary>
        public Func<string, Color> TerrainColor;
        /// <summary>Whether a roof cover's look says barrel tiles.</summary>
        public Func<string, bool> Tiled;
    }

    /// <summary>The meshes of one building.</summary>
    public struct Result
    {
        public Mesh body, roof;
        public WindowPanes.Pane[] panes;
    }

    sealed class Wall
    {
        public string Id;
        public Vector2 From, To, Along, Right;
        public float Length, Thickness, Height, Bottom, Past0, Past1;
        public Color Color;
        public JArray Openings;

        public Vector3 At(float u, float side, float y)
        {
            var p = From + Along * u + Right * side;
            return new Vector3(p.x, y, p.y);
        }

        public Quaternion Turn => Quaternion.LookRotation(new Vector3(Right.x, 0f, Right.y));
    }

    /// <summary>Everything the row draws; a row of no walls draws no walls.</summary>
    public static Result Make(JObject row, Tables tables)
    {
        var joins = Joins(row);
        var body = new MeshKit();
        var roof = new MeshKit();
        var panes = new List<WindowPanes.Pane>();
        float floor = (float?)row["floor"]?["height"] ?? 0f;
        float wallTop = 0f;
        var walls = Walls(row, joins, tables, floor);
        foreach (var w in walls)
        {
            float parapet = ParapetOver(row);
            w.Height += parapet;
            DrawWall(body, w, tables, panes);
            wallTop = Mathf.Max(wallTop, w.Bottom + w.Height - parapet);
        }
        var outline = Outline(row, joins, walls);
        if (outline.Count >= 3) DrawFloor(body, outline, floor);
        float ridge = wallTop;
        if (row["roofs"] is JObject roofs && walls.Count > 0)
            foreach (var r in roofs.Properties().OrderBy(p => p.Name, StringComparer.Ordinal))
                ridge = Mathf.Max(ridge, RoofOf(roof, body, (JObject)r.Value, joins, walls, wallTop, tables));
        DrawDetails(body, row, walls, ridge);
        return new Result { body = body.ToMesh("Row body"), roof = roof.ToMesh("Row roof"), panes = panes.ToArray() };
    }

    static float ParapetOver(JObject row) =>
        (row["roofs"] as JObject)?.Properties().Max(p => (string)p.Value["shape"] == "flat" ? (float?)p.Value["parapet"] ?? 0f : 0f) ?? 0f;

    static Dictionary<string, Vector2> Joins(JObject row)
    {
        var joins = new Dictionary<string, Vector2>(StringComparer.Ordinal);
        if (row["joins"] is JObject all)
            foreach (var j in all.Properties())
                joins[j.Name] = new Vector2((int)j.Value[0] * HalfCell, -(int)j.Value[1] * HalfCell);
        return joins;
    }

    static List<Wall> Walls(JObject row, Dictionary<string, Vector2> joins, Tables tables, float floor)
    {
        var walls = new List<Wall>();
        if (!(row["walls"] is JObject all)) return walls;
        var defaults = row["wall"];
        foreach (var p in all.Properties().OrderBy(p => p.Name, StringComparer.Ordinal))
        {
            var from = joins[(string)p.Value["from"]];
            var to = joins[(string)p.Value["to"]];
            var along = (to - from).normalized;
            float height = (float?)p.Value["height"] ?? (float?)defaults?["height"] ?? 2.7f;
            walls.Add(new Wall
            {
                Id = p.Name,
                From = from,
                To = to,
                Along = along,
                Right = RightOf(along),
                Length = (to - from).magnitude,
                Thickness = (float?)p.Value["thickness"] ?? (float?)defaults?["thickness"] ?? 0.3f,
                Height = height,
                Bottom = floor + height * ((int?)p.Value["storey"] ?? 0),
                Color = Finished(p.Value["finish"] ?? defaults?["finish"], tables.TerrainColor((string)p.Value["material"] ?? (string)defaults?["material"])),
                Openings = p.Value["openings"] as JArray,
            });
        }
        foreach (var w in walls)
            foreach (var other in walls.Where(o => o != w)) Reach(w, other);
        return walls;
    }

    static Vector2 RightOf(Vector2 along)
    {
        float east = along.y, north = -along.x;
        return new Vector2(east, north);
    }

    static void Reach(Wall w, Wall other)
    {
        if (Near(other.From, w.To)) w.Past1 = Mathf.Max(w.Past1, Outward(w, other.Along));
        if (Near(other.To, w.To)) w.Past1 = Mathf.Max(w.Past1, Outward(w, -other.Along));
        if (Near(other.From, w.From)) w.Past0 = Mathf.Max(w.Past0, Outward(w, other.Along));
        if (Near(other.To, w.From)) w.Past0 = Mathf.Max(w.Past0, Outward(w, -other.Along));
    }

    static bool Near(Vector2 a, Vector2 b) => (a - b).sqrMagnitude < 0.01f;

    // How far a wall reaches past a join: its own thickness if the other leaves on its outer side.
    static float Outward(Wall w, Vector2 leaving) => Vector2.Dot(leaving, w.Right) < -0.01f ? w.Thickness : 0f;

    static Color Finished(JToken finish, Color fallback)
    {
        var color = fallback;
        if ((string)finish?["color"] is { } hex && ColorUtility.TryParseHtmlString(hex, out var given)) color = given;
        if ((string)finish?["pattern"] == "worn") color *= WornShade;
        color.a = 1f;
        return color;
    }

    static void DrawWall(MeshKit kit, Wall w, Tables tables, List<WindowPanes.Pane> panes)
    {
        float cursor = -w.Past0, end = w.Length + w.Past1, top = w.Height;
        var openings = (w.Openings ?? new JArray()).OfType<JObject>().Select(o => Hole(o, tables)).OrderBy(h => h.from).ToList();
        foreach (var hole in openings)
        {
            if (hole.from < cursor || hole.to > end) continue;
            Piece(kit, w, cursor, hole.from, 0f, top);
            Piece(kit, w, hole.from, hole.to, 0f, hole.sill);
            if (hole.round) ArchHead(kit, w, hole, top);
            else Piece(kit, w, hole.from, hole.to, hole.sill + hole.height, top);
            Infill(kit, w, hole, panes);
            cursor = hole.to;
        }
        Piece(kit, w, cursor, end, 0f, top);
    }

    struct HoleShape
    {
        public float from, to, sill, height;
        public bool round;
        public string kind;
        public JObject row;
    }

    static HoleShape Hole(JObject o, Tables tables)
    {
        string kind = (string)o["kind"];
        var look = tables.OpeningLook(kind);
        float centreFrom = ((float?)o["at"] ?? 0f) * HalfCell;
        float width = (float?)o["width"] ?? (float?)look?["width"] ?? HalfCell * 2f;
        float span = (float?)o["width"] != null ? width : Mathf.Max(width, Mathf.Ceil(width / (HalfCell * 2f)) * HalfCell * 2f);
        float middle = centreFrom + span / 2f;
        return new HoleShape
        {
            from = middle - width / 2f,
            to = middle + width / 2f,
            sill = (float?)look?["sill"] ?? 0f,
            height = (float?)look?["height"] ?? 2.1f,
            round = (string)o["shape"] == "round",
            kind = kind,
            row = o,
        };
    }

    // A stretch of wall from u0 to u1 along it, y0 to y1 up it, as a box in its thickness.
    static void Piece(MeshKit kit, Wall w, float u0, float u1, float y0, float y1)
    {
        if (u1 - u0 < 0.001f || y1 - y0 < 0.001f) return;
        var centre = w.At((u0 + u1) / 2f, w.Thickness / 2f, w.Bottom + (y0 + y1) / 2f);
        kit.Box(centre, new Vector3(u1 - u0, y1 - y0, w.Thickness), w.Color, Quaternion.LookRotation(new Vector3(w.Right.x, 0f, w.Right.y)));
    }

    static void ArchHead(MeshKit kit, Wall w, HoleShape h, float top)
    {
        float width = h.to - h.from, spring = h.sill + h.height - width / 2f;
        for (int i = 0; i < ArchSlices; i++)
        {
            float a = h.from + width * i / ArchSlices, b = h.from + width * (i + 1) / ArchSlices, mid = ((a + b) / 2f - (h.from + h.to) / 2f) / (width / 2f);
            Piece(kit, w, a, b, spring + width / 2f * Mathf.Sqrt(1f - mid * mid), top);
        }
    }

    // What fills a hole: a door's panel or a window's pane; an arch is open.
    static void Infill(MeshKit kit, Wall w, HoleShape h, List<WindowPanes.Pane> panes)
    {
        if (h.kind == "arch") return;
        float mid = (h.from + h.to) / 2f;
        if (h.sill > 0f)
        {
            var at = w.At(mid, PaneDepth, w.Bottom + h.sill + h.height / 2f);
            var along = new Vector3(w.Along.x, 0f, w.Along.y) * (h.to - h.from) / 2f;
            panes.Add(new WindowPanes.Pane { at = at, along = along, up = Vector3.up * h.height / 2f, facing = -new Vector3(w.Right.x, 0f, w.Right.y) });
            return;
        }
        var color = Finished(h.row["finish"], DoorColor);
        kit.Box(w.At(mid, DoorDepth, w.Bottom + h.height / 2f), new Vector3(h.to - h.from, h.height, DoorDepth), color, w.Turn);
    }

    // The roof's outline if it names one, else the walls' corners round their middle; both clockwise.
    static List<Vector2> Outline(JObject row, Dictionary<string, Vector2> joins, List<Wall> walls)
    {
        var over = (row["roofs"] as JObject)?.Properties().Select(p => p.Value["over"] as JArray).FirstOrDefault(a => a != null);
        if (over != null) return over.Select(j => joins[(string)j]).ToList();
        if (walls.Count == 0) return new List<Vector2>();
        var points = walls.SelectMany(w => new[] { w.From, w.To }).Distinct().ToList();
        var middle = new Vector2(points.Average(p => p.x), points.Average(p => p.y));
        return points.OrderByDescending(p => Mathf.Atan2(p.y - middle.y, p.x - middle.x)).ToList();
    }

    // Which squares of the quarter-cell grid lie in the outline, some squares of margin all round.
    sealed class Grid
    {
        public readonly float X0, Z0;
        public readonly int W, H;
        readonly bool[,] inside;

        public Grid(List<Vector2> outline, int margin)
        {
            X0 = Mathf.Floor(outline.Min(p => p.x) / Step) * Step - margin * Step;
            Z0 = Mathf.Floor(outline.Min(p => p.y) / Step) * Step - margin * Step;
            W = Mathf.CeilToInt((outline.Max(p => p.x) - X0) / Step) + margin;
            H = Mathf.CeilToInt((outline.Max(p => p.y) - Z0) / Step) + margin;
            inside = new bool[W, H];
            for (int i = 0; i < W; i++)
                for (int j = 0; j < H; j++)
                    inside[i, j] = Inside(outline, new Vector2(X0 + (i + 0.5f) * Step, Z0 + (j + 0.5f) * Step));
        }

        static bool Inside(List<Vector2> outline, Vector2 p)
        {
            bool inside = false;
            for (int i = 0, j = outline.Count - 1; i < outline.Count; j = i++)
            {
                var a = outline[i];
                var b = outline[j];
                if ((a.y > p.y) != (b.y > p.y) && p.x < (b.x - a.x) * (p.y - a.y) / (b.y - a.y) + a.x) inside = !inside;
            }
            return inside;
        }

        public bool Square(int i, int j) => i >= 0 && j >= 0 && i < W && j < H && inside[i, j];
        public Vector2 Point(int i, int j) => new(X0 + i * Step, Z0 + j * Step);
        public bool Touches(int i, int j) => Square(i - 1, j - 1) || Square(i, j - 1) || Square(i - 1, j) || Square(i, j);
        public bool Surrounded(int i, int j) => Square(i - 1, j - 1) && Square(i, j - 1) && Square(i - 1, j) && Square(i, j);

        public bool NextTo(int i, int j)
        {
            for (int a = -1; a <= 1; a++)
                for (int b = -1; b <= 1; b++)
                    if (Touches(i + a, j + b)) return true;
            return false;
        }
    }

    static void DrawFloor(MeshKit kit, List<Vector2> outline, float height)
    {
        if (height <= 0f) return;
        var grid = new Grid(outline, 0);
        for (int j = 0; j < grid.H; j++)
            for (int i = 0; i < grid.W; i++)
                if (grid.Square(i, j) && !grid.Square(i - 1, j)) FloorRun(kit, grid, i, j, height);
    }

    // One row of floor squares, from its west end, as one box.
    static void FloorRun(MeshKit kit, Grid grid, int start, int j, float height)
    {
        int end = start;
        while (grid.Square(end + 1, j)) end++;
        float x = grid.X0 + (start + end + 1) * Step / 2f, z = grid.Z0 + (j + 0.5f) * Step, wide = (end + 1 - start) * Step;
        kit.Box(new Vector3(x, (height - FloorSkirt) / 2f, z), new Vector3(wide, height + FloorSkirt, Step), Stone);
    }

    // One roof: the height of each grid point, then its squares. Returns its highest point.
    static float RoofOf(MeshKit roof, MeshKit body, JObject r, Dictionary<string, Vector2> joins, List<Wall> walls, float top, Tables tables)
    {
        var outline = r["over"] is JArray over ? over.Select(j => joins[(string)j]).ToList() : Outline(new JObject(), joins, walls);
        if (outline.Count < 3) return top;
        string shape = (string)r["shape"] ?? "gable";
        bool eaves = shape != "flat" && ((float?)r["overhang"] ?? 0f) > Step / 3f;
        var heights = new RoofField(new Grid(outline, eaves ? 2 : 1), top, Slope(r), eaves);
        switch (shape)
        {
            case "flat":
                heights.Flat();
                break;
            case "shed":
                heights.Shed(walls.FirstOrDefault(w => w.Id == (string)r["low"]) ?? walls.OrderBy(w => w.From.y + w.To.y).ThenByDescending(w => w.Length).First());
                break;
            case "gable":
                heights.Gable(RidgeAlongX(heights.Grid, r, walls));
                break;
            default:
                heights.Hip();
                break;
        }
        var cover = tables.TerrainColor((string)r["cover"]);
        cover = cover.a <= 0f ? DefaultCover : cover;
        cover.a = 1f;
        return heights.Draw(roof, body, cover, tables.Tiled((string)r["cover"]), walls[0].Color);
    }

    static float Slope(JObject r) => r["pitch"] is JArray p && p.Count == 2 ? (float)p[0] / Mathf.Max(1f, (float)p[1]) : 4f / DefaultRun;

    static bool RidgeAlongX(Grid g, JObject r, List<Wall> walls)
    {
        var gable = (r["gables"] as JArray)?.Select(id => walls.FirstOrDefault(w => w.Id == (string)id)).FirstOrDefault(w => w != null);
        return gable != null ? Mathf.Abs(gable.Along.y) > Mathf.Abs(gable.Along.x) : g.W >= g.H;
    }

    // The height of a roof at each grid point, null where there's no roof.
    sealed class RoofField
    {
        public readonly Grid Grid;
        readonly float?[,] height;
        readonly float top, slope;
        readonly bool eaves;
        bool ridgeAlongX;

        public RoofField(Grid grid, float top, float slope, bool eaves)
        {
            Grid = grid;
            this.top = top;
            this.slope = slope;
            this.eaves = eaves;
            height = new float?[grid.W + 1, grid.H + 1];
        }

        void Each(System.Action<int, int> act)
        {
            for (int i = 0; i <= Grid.W; i++)
                for (int j = 0; j <= Grid.H; j++)
                    act(i, j);
        }

        public void Flat() => Each((i, j) =>
        {
            if (Grid.Touches(i, j)) height[i, j] = top + FlatRise;
        });

        // Each point's distance to the outline in grid squares, by the chessboard.
        public void Hip()
        {
            var far = new int[Grid.W + 1, Grid.H + 1];
            Each((i, j) => far[i, j] = Grid.Surrounded(i, j) ? int.MaxValue / 2 : 0);
            for (int a = 0; a <= Grid.W; a++)
                for (int b = 0; b <= Grid.H; b++)
                    Relax(far, a, b, -1);
            for (int a = Grid.W; a >= 0; a--)
                for (int b = Grid.H; b >= 0; b--)
                    Relax(far, a, b, 1);
            Each((i, j) =>
            {
                if (Grid.Touches(i, j)) height[i, j] = top + far[i, j] * Step * slope;
                else if (eaves && Grid.NextTo(i, j)) height[i, j] = top - Step * slope;
            });
        }

        void Relax(int[,] far, int i, int j, int sign)
        {
            if (far[i, j] == 0) return;
            int nearest = Math.Min(Far(far, i + sign, j), Math.Min(Far(far, i, j + sign), Math.Min(Far(far, i + sign, j + sign), Far(far, i - sign, j + sign))));
            far[i, j] = Math.Min(far[i, j], nearest + 1);
        }

        int Far(int[,] far, int i, int j) => i < 0 || j < 0 || i > Grid.W || j > Grid.H ? 0 : far[i, j];

        // Slopes on one axis only: each point's distance to the edge across the ridge. An overhang
        // carries the ridge on past the gable ends and drops one step past the eaves.
        public void Gable(bool alongX)
        {
            ridgeAlongX = alongX;
            Each((i, j) =>
            {
                (int along, int across) = alongX ? (i, j) : (j, i);
                int up = Run(along, across, 1), down = Run(along, across, -1);
                if (up > 0 || down > 0) height[i, j] = top + Mathf.Min(up, down) * Step * slope;
                else if (eaves && NearEaves(along, across)) height[i, j] = top - Step * slope;
            });
        }

        bool NearEaves(int along, int across) => Line(along, across) || Line(along, across - 1) || Line(along, across + 1) || Line(along, across - 2);

        bool Roofed(int along, int across)
        {
            (int i, int j) = ridgeAlongX ? (along, across) : (across, along);
            int di = ridgeAlongX ? 1 : 0, dj = 1 - di;
            return Grid.Square(i, j) || (eaves && (Grid.Square(i - di, j - dj) || Grid.Square(i + di, j + dj)));
        }

        bool Line(int along, int across) => Roofed(along - 1, across) || Roofed(along, across);

        int Run(int along, int across, int sign)
        {
            int k = 0;
            while (Line(along, sign > 0 ? across + k : across - 1 - k)) k++;
            return k;
        }

        // A plane rising from the low wall's line, towards the middle whichever way the wall runs.
        public void Shed(Wall low)
        {
            var points = new List<Vector2>();
            Each((i, j) =>
            {
                if (Grid.Surrounded(i, j)) points.Add(Grid.Point(i, j));
            });
            if (points.Count == 0) return;
            var inward = low.Right;
            var middle = new Vector2(points.Average(p => p.x), points.Average(p => p.y));
            if (Vector2.Dot(middle - low.From, inward) < 0f) inward = -inward;
            Each((i, j) =>
            {
                if (Grid.Touches(i, j) || (eaves && Grid.NextTo(i, j)))
                    height[i, j] = top + Vector2.Dot(Grid.Point(i, j) - low.From, inward) * slope;
            });
        }

        // Every square with roof at all its corners, and wall up to it at the outline. Returns the highest point.
        public float Draw(MeshKit roof, MeshKit body, Color color, bool tiled, Color wall)
        {
            float highest = top;
            for (int i = 0; i < Grid.W; i++)
                for (int j = 0; j < Grid.H; j++)
                {
                    if (!Whole(i, j)) continue;
                    var shade = tiled && i % 2 == 0 ? color * TileShade : color;
                    shade.a = 1f;
                    highest = Mathf.Max(highest, DrawSquare(roof, i, j, shade));
                    if (Grid.Square(i, j)) FillUnder(body, i, j, wall);
                }
            return highest;
        }

        bool Whole(int i, int j) => height[i, j] != null && height[i + 1, j] != null && height[i + 1, j + 1] != null && height[i, j + 1] != null;

        Vector3 At(int i, int j, float y)
        {
            var p = Grid.Point(i, j);
            return new Vector3(p.x, y, p.y);
        }

        // Split on the diagonal that follows the ridge or valley through it.
        float DrawSquare(MeshKit kit, int i, int j, Color color)
        {
            float a = height[i, j].Value, b = height[i + 1, j].Value, c = height[i + 1, j + 1].Value, d = height[i, j + 1].Value;
            Vector3 pa = At(i, j, a), pb = At(i + 1, j, b), pc = At(i + 1, j + 1, c), pd = At(i, j + 1, d);
            bool first = Mathf.Abs(a - c) <= Mathf.Abs(b - d);
            Both(kit, pa, pb, first ? pc : pd, color);
            Both(kit, first ? pa : pb, pc, pd, color);
            return Mathf.Max(a, Mathf.Max(b, Mathf.Max(c, d)));
        }

        static void Both(MeshKit kit, Vector3 a, Vector3 b, Vector3 c, Color color)
        {
            kit.Tri(a, b, c, color, Vector3.up);
            kit.Tri(a, b, c, color, Vector3.down);
        }

        // Where a roof rises above the walls' top at the outline's edge, wall fills the gap up to it.
        void FillUnder(MeshKit kit, int i, int j, Color color)
        {
            if (!Grid.Square(i - 1, j)) Edge(kit, (i, j), (i, j + 1), color, Vector3.left);
            if (!Grid.Square(i + 1, j)) Edge(kit, (i + 1, j), (i + 1, j + 1), color, Vector3.right);
            if (!Grid.Square(i, j - 1)) Edge(kit, (i, j), (i + 1, j), color, Vector3.back);
            if (!Grid.Square(i, j + 1)) Edge(kit, (i, j + 1), (i + 1, j + 1), color, Vector3.forward);
        }

        void Edge(MeshKit kit, (int i, int j) p, (int i, int j) q, Color color, Vector3 outward)
        {
            float y0 = height[p.i, p.j].Value, y1 = height[q.i, q.j].Value;
            if (Mathf.Max(y0, y1) <= top + MinRoofTiltAboveEaves) return;
            kit.Quad(At(p.i, p.j, top), At(q.i, q.j, top), At(q.i, q.j, Mathf.Max(y1, top)), At(p.i, p.j, Mathf.Max(y0, top)), color, outward);
        }
    }

    static void DrawDetails(MeshKit kit, JObject row, List<Wall> walls, float ridge)
    {
        if (!(row["parts"] is JArray parts)) return;
        foreach (var part in parts.OfType<JObject>())
        {
            var w = walls.FirstOrDefault(x => x.Id == (string)part["on"]);
            if (w == null) continue;
            float at = ((float?)part["at"] ?? w.Length / HalfCell / 2f) * HalfCell, every = (float?)part["every"] ?? 0.6f;
            switch ((string)part["kind"])
            {
                case "viga": Vigas(kit, w, every); break;
                case "canale": kit.Box(w.At(at, -CanaleLength / 2f, w.Bottom + w.Height - CanaleSize), new Vector3(CanaleSize, CanaleSize, CanaleLength), Timber, w.Turn); break;
                case "chimney": kit.Box(w.At(at, ChimneySize / 2f, ridge / 2f), new Vector3(ChimneySize, ridge + ChimneyAbove, ChimneySize), Stone, w.Turn); break;
                case "porch": Porch(kit, w, at, (float?)part["width"] ?? 3f, (float?)part["depth"] ?? 1.5f); break;
                case "beam": Beams(kit, w, every); break;
                case "shutters": Shutters(kit, w, at); break;
                case "planter": kit.Box(w.At(at, -PlanterDepth / 2f, w.Bottom + 0.75f), new Vector3(1f, PlanterHeight, PlanterDepth), Timber, w.Turn); break;
            }
        }
    }

    static void Vigas(MeshKit kit, Wall w, float every)
    {
        for (float u = every / 2f; u < w.Length; u += Mathf.Max(0.2f, every))
            kit.Cylinder(w.At(u, 0f, w.Bottom + w.Height - 0.4f), w.At(u, -VigaLength, w.Bottom + w.Height - 0.4f), VigaRadius, VigaSides, Timber);
    }

    static void Beams(MeshKit kit, Wall w, float every)
    {
        for (float u = every / 2f; u < w.Length; u += Mathf.Max(0.2f, every))
            kit.Box(w.At(u, -BeamSize / 2f, w.Bottom + w.Height / 2f), new Vector3(BeamSize, w.Height, BeamSize), Timber, w.Turn);
    }

    static void Porch(MeshKit kit, Wall w, float at, float width, float depth)
    {
        foreach (float side in new[] { -width / 2f, width / 2f })
            kit.Box(w.At(at + side, -depth, w.Bottom + PorchHeight / 2f), new Vector3(PorchPost, PorchHeight, PorchPost), Timber, w.Turn);
        kit.Box(w.At(at, -depth / 2f, w.Bottom + PorchHeight), new Vector3(width + PorchPost, PorchRoof, depth + PorchPost), Timber, w.Turn);
    }

    static void Shutters(MeshKit kit, Wall w, float at)
    {
        foreach (float side in new[] { -0.65f, 0.65f })
            kit.Box(w.At(at + side, -0.02f, w.Bottom + 1.5f), new Vector3(ShutterWidth, 1.2f, 0.04f), Timber, w.Turn);
    }
}
