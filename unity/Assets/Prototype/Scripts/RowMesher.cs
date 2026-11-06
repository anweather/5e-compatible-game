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
// counted in squares, which for walls on the axes is the true straight skeleton. So a 45° wall steps
// on the roof's edge, and eaves are the grid's last ring, drawn out to the row's overhang.
// Every wall is cut into stretches, one per cell it stands on, and the meshes remember what each
// stretch drew and which roof squares it holds up, so a burnt wall can leave a ruin (RowRuin).
// A window's pane carries its lamp (the opening's "lamp", or its table row's look): the glass glows
// at night (WindowPanes), and remembers its stretch so a burnt or fallen wall puts it out.
public static class RowMesher
{
    const float HalfCell = 0.75f, Step = 0.375f, FloorSkirt = 0.5f, DoorDepth = 0.05f, PaneDepth = 0.08f, FlatRise = 0.04f;
    const float VigaRadius = 0.09f, VigaLength = 0.55f, CanaleLength = 0.7f, CanaleSize = 0.18f, ChimneySize = 0.6f, ChimneyAbove = 0.9f;
    const float PorchHeight = 2.3f, PorchPost = 0.12f, PorchRoof = 0.12f, BeamSize = 0.12f, ShutterWidth = 0.35f, PlanterDepth = 0.25f, PlanterHeight = 0.22f;
    const float MinRoofTiltAboveEaves = 0.01f, DefaultRun = 12f, TileShade = 0.86f, WornShade = 0.8f;
    const float StumpLow = 0.2f, StumpHigh = 0.7f, Cell = HalfCell * 2f, Touching = 0.05f;
    const int ArchSlices = 20, VigaSides = 6;
    const float DockAbove = 0.45f, PlankWide = 0.32f, PlankThick = 0.08f, PlankGap = 0.03f, DockBeam = 0.18f, PostEvery = 3f, PostSize = 0.24f, PostAbove = 0.8f, PostDown = 4f;
    const int DockSeed = 41;
    static readonly Color DefaultCover = new(0.32f, 0.3f, 0.32f), DoorColor = new(0.36f, 0.25f, 0.16f), Stone = new(0.45f, 0.44f, 0.42f), Timber = new(0.3f, 0.2f, 0.13f), Underside = new(0.24f, 0.18f, 0.14f);
    static readonly Color GatePlank = new(0.42f, 0.28f, 0.15f), GatePlankDark = new(0.33f, 0.21f, 0.11f), GateIron = new(0.13f, 0.13f, 0.14f);
    static readonly Color Flag = new(0.62f, 0.58f, 0.5f), FlagDark = new(0.52f, 0.48f, 0.42f), FlagPale = new(0.7f, 0.66f, 0.58f);
    const float RoofLift = 0.03f, DeckDepth = 0.5f, GateDepth = 0.25f, MerlonDepth = 0.45f, ParapetHeight = 0.5f, Tread = 0.4f;

    /// <summary>What a row's drawing needs from the rules in force.</summary>
    public sealed class Tables
    {
        /// <summary>An opening kind's view numbers (width, height, sill); null if there's none.</summary>
        public Func<string, JToken> OpeningLook;
        /// <summary>A terrain's colour.</summary>
        public Func<string, Color> TerrainColor;
        /// <summary>Whether a roof cover's look says barrel tiles.</summary>
        public Func<string, bool> Tiled;
        /// <summary>How far the footing reaches below the row's middle, where the ground falls away under it.</summary>
        public float Footing = FloorSkirt;
        /// <summary>The ground at a point of the row (x east, y as joins have it), from its root; a level row stands its openings on it.</summary>
        public Func<Vector2, float> GroundAt = _ => 0f;
    }

    /// <summary>The meshes of one building.</summary>
    public struct Result
    {
        public Mesh body, roof;
        /// <summary>What a wizard walks on and against up a level row, drawn by nothing: stairs, the deck over a passage, parapets, a tower's rim.</summary>
        public Mesh walk;
        /// <summary>What stops a wizard at a parapet, drawn by nothing, and what the camera passes: a wall walk is too narrow for it to stay behind one.</summary>
        public Mesh rail;
        /// <summary>How high each wall's top stands from the root, by the cell it stands on: where the blockers stop.</summary>
        public Dictionary<Vector2Int, float> tops;
        public WindowPanes.Pane[] panes;
        /// <summary>The stretch each pane stands in, by pane.</summary>
        public Stretch[] paneStretches;
        public Stretch[] stretches;
        /// <summary>The top of each chimney, for its smoke.</summary>
        public Vector3[] chimneys;
    }

    /// <summary>A part of a mesh: its indices and its vertices, each in one piece.</summary>
    public struct Run
    {
        public int Index, Indices, Vertex, Vertices;
    }

    /// <summary>A wall's length on one cell, and what's drawn with it.</summary>
    public sealed class Stretch
    {
        /// <summary>Its cell, from the row's at cell, y south.</summary>
        public Vector2Int Place;
        /// <summary>Where its middle stands, at the foot of the wall.</summary>
        public Vector3 Middle;
        public float From, To;
        /// <summary>A hole's middle is here: what's above and below it needs a stretch beside it.</summary>
        public bool Opening;
        /// <summary>The stretches next along its wall; null at the wall's ends.</summary>
        public Stretch Before, After;
        /// <summary>At a wall's end, the ends of the walls that meet it there.</summary>
        public List<Stretch> Meets { get; } = new();
        /// <summary>In the body: its wall and details; its door panel; the gable filling up to the roof it holds.</summary>
        public List<Run> Whole { get; } = new();
        public List<Run> Door { get; } = new();
        public List<Run> Gable { get; } = new();
        /// <summary>In the body: what's left when it falls.</summary>
        public Run Left;
        /// <summary>In the walk mesh: what a wizard stands on that goes with it.</summary>
        public List<Run> Walk { get; } = new();
        /// <summary>In the rail mesh: the parapet that stops a wizard, which goes with it.</summary>
        public List<Run> Rail { get; } = new();
        /// <summary>A part that stands on many cells (a tower): those, from the row's at cell. It falls when enough of them have.</summary>
        public List<Vector2Int> Group;
        /// <summary>In the roof mesh: the squares nearest it.</summary>
        public List<Run> Roof { get; } = new();
    }

    sealed class Wall
    {
        public string Id;
        public Vector2 From, To, Along, Right;
        public float Length, Thickness, Height, Bottom, Past0, Past1;
        /// <summary>The row's towers, middle and radius: a wall's top stops at their edge.</summary>
        public List<(Vector2 middle, float radius)> Towers = new();
        /// <summary>A level row's wall: its openings stand on the ground under them, not on its foot.</summary>
        public bool Level;
        public Color Color;
        public JArray Openings;
        public List<Stretch> Stretches;

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
        // A colour's alpha is glow to VillageLit: with theirs at 1, whole houses shone all night and pulsed.
        var body = new MeshKit { Glows = false };
        var roof = new MeshKit { Glows = false };
        var walk = new MeshKit { Glows = false };
        var rail = new MeshKit { Glows = false };
        var panes = new List<(WindowPanes.Pane pane, Stretch on)>();
        bool level = (bool?)row["level"] ?? false;
        float floor = level ? 0f : (float?)row["floor"]?["height"] ?? 0f;
        float wallTop = 0f;
        var walls = Walls(row, joins, tables, floor);
        if ((string)row["wall"]?["finish"]?["pattern"] == "coursed") body.Coursed = Finished(row["wall"]["finish"], tables.TerrainColor((string)row["wall"]["material"]));
        foreach (var w in walls)
        {
            float parapet = ParapetOver(row);
            w.Height += parapet;
            DrawWall(body, walk, w, tables, panes);
            wallTop = Mathf.Max(wallTop, w.Bottom + w.Height - parapet);
        }
        var outline = Outline(row, joins, walls);
        if (outline.Count >= 3 && !level) DrawFloor(body, outline, floor, tables.Footing);
        float ridge = wallTop;
        if (row["roofs"] is JObject roofs && walls.Count > 0)
            foreach (var r in roofs.Properties().OrderBy(p => p.Name, StringComparer.Ordinal))
                ridge = Mathf.Max(ridge, RoofOf(roof, body, (JObject)r.Value, joins, walls, wallTop, tables));
        var chimneys = new List<Vector3>();
        DrawDetails(body, walk, rail, row, walls, ridge, chimneys, tables);
        var towers = RowTowers.Draw(body, walk, row, joins, walls.Select(w => (w.At(0f, 0f, 0f), w.At(w.Length, 0f, 0f), w.At(0f, w.Thickness, 0f))).ToList(), WalkTop(row), tables.Footing, Finished(row["wall"]?["finish"], tables.TerrainColor((string)row["wall"]?["material"])));
        return new Result { body = body.ToMesh("Row body"), roof = roof.ToMesh("Row roof"), walk = walk.ToMesh("Row walk"), rail = rail.ToMesh("Row rail"), tops = Tops(walls), panes = panes.Select(p => p.pane).ToArray(), paneStretches = panes.Select(p => p.on).ToArray(), stretches = walls.SelectMany(w => w.Stretches).Concat(towers).ToArray(), chimneys = chimneys.ToArray() };
    }

    /// <summary>How high a level row's wall walk stands above its root: its "wall" height.</summary>
    public static float WalkTop(JObject row) => (float?)row["wall"]?["height"] ?? 2.7f;

    static Dictionary<Vector2Int, float> Tops(List<Wall> walls)
    {
        var tops = new Dictionary<Vector2Int, float>();
        foreach (var w in walls)
            foreach (var s in w.Stretches) tops[s.Place] = Mathf.Max(tops.TryGetValue(s.Place, out float was) ? was : float.MinValue, w.Bottom + w.Height);
        return tops;
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
            bool level = (bool?)row["level"] ?? false;
            walls.Add(new Wall
            {
                Level = level,
                Id = p.Name,
                From = from,
                To = to,
                Along = along,
                Right = RightOf(along),
                Length = (to - from).magnitude,
                Thickness = (float?)p.Value["thickness"] ?? (float?)defaults?["thickness"] ?? 0.3f,
                Height = level ? height + tables.Footing : height,
                Bottom = level ? -tables.Footing : floor + height * ((int?)p.Value["storey"] ?? 0),
                Color = Finished(p.Value["finish"] ?? defaults?["finish"], tables.TerrainColor((string)p.Value["material"] ?? (string)defaults?["material"])),
                Openings = p.Value["openings"] as JArray,
            });
        }
        foreach (var w in walls)
            foreach (var other in walls.Where(o => o != w)) Reach(w, other);
        var towerRings = RowTowers.Rings(row, joins);
        foreach (var w in walls)
        {
            w.Towers = towerRings;
            w.Stretches = StretchesOf(w);
        }
        foreach (var w in walls)
            foreach (var other in walls.Where(o => o != w && w.Stretches.Count > 0 && o.Stretches.Count > 0)) Join(w, other);
        return walls;
    }

    static void Join(Wall w, Wall other)
    {
        if (Near(other.From, w.From) || Near(other.To, w.From)) w.Stretches[0].Meets.Add(Near(other.From, w.From) ? other.Stretches[0] : other.Stretches[^1]);
        if (Near(other.From, w.To) || Near(other.To, w.To)) w.Stretches[^1].Meets.Add(Near(other.From, w.To) ? other.Stretches[0] : other.Stretches[^1]);
    }

    // The half cells along a wall, each on the cell the core gives it (BuildingRaster.Run: the cell an
    // eighth of a cell to the right of the half cell's middle), joined where they share one.
    static List<Stretch> StretchesOf(Wall w)
    {
        float step = Mathf.Abs(w.Along.x) > 0.01f && Mathf.Abs(w.Along.y) > 0.01f ? HalfCell * Mathf.Sqrt(2f) : HalfCell;
        int steps = Mathf.RoundToInt(w.Length / step);
        var stretches = new List<Stretch>();
        for (int i = 0; i < steps; i++)
        {
            var p = w.From + w.Along * ((i + 0.5f) * step) + w.Right * (step / 4f);
            var cell = new Vector2Int(Mathf.FloorToInt(p.x / Cell), Mathf.FloorToInt(-p.y / Cell));
            var last = stretches.LastOrDefault();
            if (last != null && last.Place == cell)
            {
                last.To = (i + 1) * step;
                continue;
            }
            var next = new Stretch { Place = cell, From = i * step, To = (i + 1) * step, Before = last };
            if (last != null) last.After = next;
            stretches.Add(next);
        }
        if (stretches.Count == 0) return stretches;
        stretches[0].From = -w.Past0;
        stretches[^1].To = w.Length + w.Past1;
        foreach (var s in stretches) s.Middle = w.At((s.From + s.To) / 2f, w.Thickness / 2f, w.Bottom);
        return stretches;
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
        return color;
    }

    static void DrawWall(MeshKit kit, MeshKit walk, Wall w, Tables tables, List<(WindowPanes.Pane pane, Stretch on)> panes)
    {
        float cursor = -w.Past0, end = w.Length + w.Past1, top = w.Height;
        var openings = (w.Openings ?? new JArray()).OfType<JObject>().Select(o => Hole(o, w, tables)).OrderBy(h => h.from).ToList();
        var pieces = new List<(float u0, float u1, float y0, float y1)>();
        var holes = new List<HoleShape>();
        foreach (var hole in openings)
        {
            if (hole.from < cursor || hole.to > end) continue;
            pieces.Add((cursor, hole.from, 0f, top));
            pieces.Add((hole.from, hole.to, 0f, hole.foot + hole.sill));
            if (hole.round) ArchHead(pieces, hole, top);
            else pieces.Add((hole.from, hole.to, hole.foot + hole.sill + hole.height, top));
            holes.Add(hole);
            if (w.Level && hole.sill <= 0f) HolderAt(w, (hole.from + hole.to) / 2f)?.Walk.Add(Record(walk, () => Piece(walk, w, hole.from, hole.to, top - DeckDepth, top, Color.clear)));
            cursor = hole.to;
        }
        pieces.Add((cursor, end, 0f, top));
        foreach (var s in w.Stretches) DrawStretch(kit, w, s, pieces, holes, panes, w.Level ? tables.GroundAt(Flat(s.Middle)) - w.Bottom : 0f);
    }

    static Stretch HolderAt(Wall w, float u) => w.Stretches.FirstOrDefault(s => u <= s.To) ?? w.Stretches.LastOrDefault();

    // A stretch's share of the wall, its holes' fillings, and what's left of it when it falls.
    static void DrawStretch(MeshKit kit, Wall w, Stretch s, List<(float u0, float u1, float y0, float y1)> pieces, List<HoleShape> holes, List<(WindowPanes.Pane pane, Stretch on)> panes, float foot)
    {
        s.Whole.Add(Record(kit, () =>
        {
            foreach (var (u0, u1, y0, y1) in pieces) Piece(kit, w, Mathf.Max(u0, s.From), Mathf.Min(u1, s.To), y0, y1, w.Color);
        }));
        float middle = (s.From + s.To) / 2f, sill = float.MaxValue;
        foreach (var hole in holes)
        {
            float from = Mathf.Max(hole.from, s.From), to = Mathf.Min(hole.to, s.To);
            if (to - from > Touching) Infill(kit, w, hole, from, to, s, panes);
            if (hole.from > middle || hole.to < middle) continue;
            s.Opening = true;
            sill = hole.foot + hole.sill;
        }
        s.Left = Record(kit, () => Stump(kit, w, s, sill, foot));
    }

    // A broken edge, two heights side by side, picked by the cell; never above a sill, nor over a door's gap.
    static void Stump(MeshKit kit, Wall w, Stretch s, float sill, float foot)
    {
        if (w.Bottom > w.Height / 2f || sill <= 0f) return;
        float middle = (s.From + s.To) / 2f;
        var broken = new Color(w.Color.r * WornShade, w.Color.g * WornShade, w.Color.b * WornShade);
        Piece(kit, w, s.From, middle, 0f, Mathf.Min(sill, foot + Rough(s.Place.x * 3 + s.Place.y * 7)), broken);
        Piece(kit, w, middle, s.To, 0f, Mathf.Min(sill, foot + Rough(s.Place.x * 5 + s.Place.y * 11 + 1)), broken);
    }

    static float Rough(int seed) => Mathf.Lerp(StumpLow, StumpHigh, unchecked((uint)seed * 2654435761u) / (float)uint.MaxValue);

    internal static Run Record(MeshKit kit, Action draw)
    {
        int index = kit.IndexCount, vertex = kit.VertexCount;
        draw();
        return new Run { Index = index, Indices = kit.IndexCount - index, Vertex = vertex, Vertices = kit.VertexCount - vertex };
    }

    struct HoleShape
    {
        public float from, to, sill, height;
        /// <summary>Where the hole's sill counts from, up the wall: its foot, or a level row's ground under it.</summary>
        public float foot;
        public bool round;
        public Color lamp;
        public string kind;
        public JObject row;
    }

    static HoleShape Hole(JObject o, Wall w, Tables tables)
    {
        string kind = (string)o["kind"];
        var look = tables.OpeningLook(kind);
        float centreFrom = ((float?)o["at"] ?? 0f) * HalfCell;
        float width = (float?)o["width"] ?? (float?)look?["width"] ?? HalfCell * 2f;
        float span = (float?)o["width"] != null ? width : Mathf.Max(width, Mathf.Ceil(width / (HalfCell * 2f)) * HalfCell * 2f);
        float middle = centreFrom + span / 2f;
        var under = w.From + w.Along * middle + w.Right * (w.Thickness / 2f);
        return new HoleShape
        {
            foot = w.Level ? tables.GroundAt(under) - w.Bottom : 0f,
            from = middle - width / 2f,
            to = middle + width / 2f,
            sill = (float?)look?["sill"] ?? 0f,
            height = (float?)look?["height"] ?? 2.1f,
            round = (string)o["shape"] == "round",
            lamp = LampOf(o["lamp"] ?? look?["lamp"]),
            kind = kind,
            row = o,
        };
    }

    // A lamp's colour ("#rrggbb"), or clear for "off" or none.
    static Color LampOf(JToken lamp) =>
        (string)lamp is { } hex && hex != Chem.BuildingsShown.LampOff && ColorUtility.TryParseHtmlString(hex, out var c) ? c : Color.clear;

    // A stretch of wall from u0 to u1 along it, y0 to y1 up it, as a box in its thickness.
    static void Piece(MeshKit kit, Wall w, float u0, float u1, float y0, float y1, Color color)
    {
        if (u1 - u0 < 0.001f || y1 - y0 < 0.001f) return;
        var centre = w.At((u0 + u1) / 2f, w.Thickness / 2f, w.Bottom + (y0 + y1) / 2f);
        kit.Box(centre, new Vector3(u1 - u0, y1 - y0, w.Thickness), color, w.Turn);
    }

    static void ArchHead(List<(float u0, float u1, float y0, float y1)> pieces, HoleShape h, float top)
    {
        float width = h.to - h.from, spring = h.foot + h.sill + h.height - width / 2f;
        for (int i = 0; i < ArchSlices; i++)
        {
            float a = h.from + width * i / ArchSlices, b = h.from + width * (i + 1) / ArchSlices, mid = ((a + b) / 2f - (h.from + h.to) / 2f) / (width / 2f);
            pieces.Add((a, b, spring + width / 2f * Mathf.Sqrt(1f - mid * mid), top));
        }
    }

    // What fills a stretch's share of a hole: a door's panel or a window's pane; an arch is open.
    static void Infill(MeshKit kit, Wall w, HoleShape h, float from, float to, Stretch s, List<(WindowPanes.Pane pane, Stretch on)> panes)
    {
        if (h.kind == "arch") return;
        float mid = (from + to) / 2f;
        if (h.sill > 0f)
        {
            var at = w.At(mid, PaneDepth, w.Bottom + h.foot + h.sill + h.height / 2f);
            var along = new Vector3(w.Along.x, 0f, w.Along.y) * (to - from) / 2f;
            panes.Add((new WindowPanes.Pane { at = at, along = along, up = Vector3.up * h.height / 2f, facing = -new Vector3(w.Right.x, 0f, w.Right.y), lamp = h.lamp }, s));
            return;
        }
        var color = Finished(h.row["finish"], DoorColor);
        float depth = w.Level ? GateDepth : DoorDepth;
        s.Door.Add(Record(kit, () =>
        {
            kit.Box(w.At(mid, depth, w.Bottom + h.foot + h.height / 2f), new Vector3(to - from, h.height, depth), color, w.Turn);
            if (w.Level) GateFace(kit, w, from, to, depth, w.Bottom + h.foot, h.height);
        }));
    }

    // A castle gate's face: upright planks in two tones with iron bands across them.
    static void GateFace(MeshKit kit, Wall w, float from, float to, float depth, float foot, float height)
    {
        const float Plank = 0.42f, Proud = 0.08f, BandHeight = 0.22f;
        int planks = Mathf.Max(1, Mathf.RoundToInt((to - from) / Plank));
        float wide = (to - from) / planks, face = depth / 2f - Proud;
        for (int i = 0; i < planks; i++)
            kit.Box(w.At(from + (i + 0.5f) * wide, face, foot + height / 2f), new Vector3(wide * 0.92f, height, Proud), i % 2 == 0 ? GatePlank : GatePlankDark, w.Turn);
        foreach (float share in new[] { 0.22f, 0.5f, 0.78f })
            kit.Box(w.At((from + to) / 2f, face - Proud, foot + height * share), new Vector3(to - from, BandHeight, Proud), GateIron, w.Turn);
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

    static void DrawFloor(MeshKit kit, List<Vector2> outline, float height, float footing)
    {
        var grid = new Grid(outline, 0);
        for (int j = 0; j < grid.H; j++)
            for (int i = 0; i < grid.W; i++)
                if (grid.Square(i, j) && !grid.Square(i - 1, j)) FloorRun(kit, grid, i, j, height, footing);
    }

    // One row of floor squares, from its west end, as one box.
    static void FloorRun(MeshKit kit, Grid grid, int start, int j, float height, float footing)
    {
        int end = start;
        while (grid.Square(end + 1, j)) end++;
        float x = grid.X0 + (start + end + 1) * Step / 2f, z = grid.Z0 + (j + 0.5f) * Step, wide = (end + 1 - start) * Step;
        kit.Box(new Vector3(x, (height - footing) / 2f, z), new Vector3(wide, height + footing, Step), Stone);
    }

    // One roof: the height of each grid point, then its squares. Returns its highest point.
    static float RoofOf(MeshKit roof, MeshKit body, JObject r, Dictionary<string, Vector2> joins, List<Wall> walls, float top, Tables tables)
    {
        var outline = r["over"] is JArray over ? over.Select(j => joins[(string)j]).ToList() : Outline(new JObject(), joins, walls);
        if (outline.Count < 3) return top;
        string shape = (string)r["shape"] ?? "gable";
        float overhang = shape == "flat" ? 0f : (float?)r["overhang"] ?? 0f;
        bool eaves = overhang > Step / 3f;
        var heights = new RoofField(new Grid(outline, eaves ? 2 : 1), top, Slope(r), eaves ? overhang : 0f);
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
        return heights.Draw(roof, body, cover, tables.Tiled((string)r["cover"]), walls[0].Color, walls.SelectMany(w => w.Stretches).ToList());
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
        readonly float overhang;
        readonly bool eaves;
        bool ridgeAlongX;

        public RoofField(Grid grid, float top, float slope, float overhang)
        {
            Grid = grid;
            this.top = top;
            this.slope = slope;
            this.overhang = overhang;
            eaves = overhang > 0f;
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
                else if (eaves && Grid.NextTo(i, j)) height[i, j] = top - overhang * slope;
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
                else if (eaves && NearEaves(along, across)) height[i, j] = top - overhang * slope;
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
        public float Draw(MeshKit roof, MeshKit body, Color color, bool tiled, Color wall, List<Stretch> holders)
        {
            float highest = top;
            for (int i = 0; i < Grid.W; i++)
                for (int j = 0; j < Grid.H; j++)
                {
                    if (!Whole(i, j)) continue;
                    var shade = tiled && i % 2 == 0 ? color * TileShade : color;
                    var holder = Nearest(holders, Grid.Point(i, j) + new Vector2(Step, Step) / 2f);
                    int east = i, north = j;
                    holder.Roof.Add(Record(roof, () => highest = Mathf.Max(highest, DrawSquare(roof, east, north, shade))));
                    if (Grid.Square(i, j)) holder.Gable.Add(Record(body, () => FillUnder(body, east, north, wall)));
                }
            return highest;
        }

        static Stretch Nearest(List<Stretch> holders, Vector2 p)
        {
            Stretch best = null;
            float nearest = float.MaxValue;
            foreach (var s in holders)
            {
                float d = (new Vector2(s.Middle.x, s.Middle.z) - p).sqrMagnitude;
                if (d >= nearest) continue;
                nearest = d;
                best = s;
            }
            return best;
        }

        bool Whole(int i, int j) => height[i, j] != null && height[i + 1, j] != null && height[i + 1, j + 1] != null && height[i, j + 1] != null;

        Vector3 At(int i, int j, float y)
        {
            var p = Grid.Point(i, j);
            if (eaves && !Grid.Touches(i, j)) p += Out(i, j) * (overhang - Step);
            return new Vector3(p.x, y + RoofLift, p.y); // clear of the wall tops it would flicker against
        }

        // Which way a point of the eaves' ring lies from the roof's edge; a corner, both ways.
        Vector2 Out(int i, int j)
        {
            var way = new Vector2((Grid.Touches(i - 1, j) ? 1 : 0) - (Grid.Touches(i + 1, j) ? 1 : 0), (Grid.Touches(i, j - 1) ? 1 : 0) - (Grid.Touches(i, j + 1) ? 1 : 0));
            if (way != Vector2.zero) return way;
            return new Vector2((Grid.Touches(i - 1, j - 1) || Grid.Touches(i - 1, j + 1) ? 1 : 0) - (Grid.Touches(i + 1, j - 1) || Grid.Touches(i + 1, j + 1) ? 1 : 0),
                (Grid.Touches(i - 1, j - 1) || Grid.Touches(i + 1, j - 1) ? 1 : 0) - (Grid.Touches(i - 1, j + 1) || Grid.Touches(i + 1, j + 1) ? 1 : 0));
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
            kit.Tri(a, b, c, Underside, Vector3.down);
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

    static void DrawDetails(MeshKit kit, MeshKit walk, MeshKit rail, JObject row, List<Wall> walls, float ridge, List<Vector3> chimneys, Tables tables)
    {
        if (!(row["parts"] is JArray parts)) return;
        foreach (var part in parts.OfType<JObject>())
        {
            var w = walls.FirstOrDefault(x => x.Id == (string)part["on"]);
            if (w == null) continue;
            float at = ((float?)part["at"] ?? w.Length / HalfCell / 2f) * HalfCell, every = (float?)part["every"] ?? 0.6f;
            switch ((string)part["kind"])
            {
                case "battlements": Battlements(kit, rail, w, every, (float?)part["height"] ?? 1.2f); break;
                case "stair": Stair(kit, walk, w, at, ((float?)part["length"] ?? w.Length / HalfCell) * HalfCell, tables); break;
                case "dock": Dock(kit, walk, w, at, ((float?)part["length"] ?? 20f) * HalfCell, ((float?)part["width"] ?? 4f) * HalfCell, tables); break;
                case "viga": Vigas(kit, w, every); break;
                case "canale": Held(kit, w, at, () => kit.Box(w.At(at, -CanaleLength / 2f, w.Bottom + w.Height - CanaleSize), new Vector3(CanaleSize, CanaleSize, CanaleLength), Timber, w.Turn)); break;
                case "chimney":
                    kit.Box(w.At(at, ChimneySize / 2f, ridge / 2f), new Vector3(ChimneySize, ridge + ChimneyAbove, ChimneySize), Stone, w.Turn); // stone: it outlasts the wall
                    chimneys.Add(w.At(at, ChimneySize / 2f, ridge + ChimneyAbove / 2f));
                    break;
                case "porch": Held(kit, w, at, () => Porch(kit, w, at, (float?)part["width"] ?? 3f, (float?)part["depth"] ?? 1.5f)); break;
                case "beam": Beams(kit, w, every); break;
                case "shutters": Held(kit, w, at, () => Shutters(kit, w, at)); break;
                case "planter": Held(kit, w, at, () => kit.Box(w.At(at, -PlanterDepth / 2f, w.Bottom + 0.75f), new Vector3(1f, PlanterHeight, PlanterDepth), Timber, w.Turn)); break;
            }
        }
    }

    // A low parapet along the wall's outer edge, with a merlon every so often; what stops a wizard is the parapet's full height. The
    // wall's top between them is laid in flagstones.
    static void Battlements(MeshKit kit, MeshKit rail, Wall w, float every, float height)
    {
        float top = w.Bottom + w.Height;
        foreach (var s in w.Stretches)
        {
            s.Whole.Add(Record(kit, () => Flagstones(kit, w, s.From, s.To, top)));
            foreach (var (from, to) in OutsideTowers(w, s.From, s.To))
            {
                float u = (from + to) / 2f, long_ = to - from;
                s.Whole.Add(Record(kit, () => kit.Box(w.At(u, MerlonDepth / 2f, top + ParapetHeight / 2f), new Vector3(long_, ParapetHeight, MerlonDepth), w.Color, w.Turn)));
                s.Rail.Add(Record(rail, () => rail.Box(w.At(u, MerlonDepth / 2f, top + height / 2f), new Vector3(long_, height, MerlonDepth), Color.clear, w.Turn)));
            }
        }
        for (float u = every / 2f; u < w.Length; u += Mathf.Max(0.4f, every))
        {
            float along = u;
            if (InTower(w, along, MerlonDepth / 2f, every / 2f)) continue;
            Held(kit, w, along, () => kit.Box(w.At(along, MerlonDepth / 2f, top + height / 2f), new Vector3(every / 2f, height, MerlonDepth), w.Color, w.Turn));
        }
    }

    // Whether a point along a wall, this far in from its face, is inside a tower, with some room to spare.
    static bool InTower(Wall w, float along, float depth, float spare = 0f)
    {
        var at = w.At(along, depth, 0f);
        foreach (var (middle, radius) in w.Towers)
            if (new Vector2(at.x - middle.x, at.z - middle.y).sqrMagnitude < (radius + spare) * (radius + spare)) return true;
        return false;
    }

    // The runs of a stretch of wall's outer edge that aren't inside a tower.
    static List<(float from, float to)> OutsideTowers(Wall w, float from, float to)
    {
        const float Step = 0.25f, Shortest = 0.1f;
        var runs = new List<(float, float)>();
        float start = from;
        bool open = false;
        for (float u = from; u < to; u += Step)
        {
            bool clear = !InTower(w, u, MerlonDepth / 2f);
            if (clear && !open) start = u;
            else if (!clear && open && u - start > Shortest) runs.Add((start, u));
            open = clear;
        }
        if (open && to - start > Shortest) runs.Add((start, to));
        return runs;
    }

    static void Flagstones(MeshKit kit, Wall w, float from, float to, float top)
    {
        const float Stone = 0.75f, Thin = 0.05f;
        int stones = Mathf.Max(1, Mathf.RoundToInt((to - from) / Stone));
        float each = (to - from) / stones, across = w.Thickness * 0.5f;
        for (int i = 0; i < stones; i++)
            for (int row = 0; row < 2; row++)
                if (!InTower(w, from + (i + 0.5f) * each, MerlonDepth + across * (row + 0.5f) * 0.9f, each / 2f))
                kit.Box(w.At(from + (i + 0.5f) * each, MerlonDepth + across * (row + 0.5f) * 0.9f, top + Thin / 2f), new Vector3(each * 0.96f, Thin, across * 0.86f), (i + row) % 3 == 0 ? FlagDark : (i + row) % 3 == 1 ? Flag : FlagPale, w.Turn);
    }

    // Steps up the wall's inner side, from the ground at "at" to a landing a cell long at the wall's top, one cell wide. The treads are
    // drawn; a wizard walks a ramp (walk), since a controller climbs one and catches on steps.
    static void Stair(MeshKit kit, MeshKit walk, Wall w, float at, float length, Tables tables)
    {
        float from = w.Thickness, to = w.Thickness + Cell, side = (from + to) / 2f, foot = tables.GroundAt(Flat(w.At(at, side, 0f))), top = w.Bottom + w.Height;
        int steps = Mathf.Max(1, Mathf.RoundToInt(length / Tread));
        float climb = length - Cell;
        var holder = HolderAt(w, at + length);
        var treads = Record(kit, () =>
        {
            for (int i = 0; i < steps; i++)
            {
                float u = at + (i + 0.5f) * length / steps, y = Mathf.Lerp(foot, top, (u - at) / climb);
                float low = Mathf.Min(foot, tables.GroundAt(Flat(w.At(u, side, 0f)))) - FloorSkirt;
                kit.Box(w.At(u, side, (y + low) / 2f), new Vector3(length / steps + Touching, y - low, Cell), w.Color, w.Turn);
            }
        });
        var ramp = Record(walk, () =>
        {
            var a = w.At(at, from, foot);
            var b = w.At(at + climb, from, top);
            var c = w.At(at + climb, to, top);
            var d = w.At(at, to, foot);
            walk.Quad(a, b, c, d, Color.clear, Vector3.up);
            walk.Quad(d, c, b, a, Color.clear, Vector3.up);
            walk.Box(w.At(at + climb + Cell / 2f, side, top - DeckDepth / 2f), new Vector3(Cell, DeckDepth, Cell), Color.clear, w.Turn);
        });
        holder?.Whole.Add(treads);
        holder?.Walk.Add(ramp);
    }

    // Out from a wall over the water: planks on two beams, on posts down into the lake bed, and a deck to walk on. Its height
    // is the shore's at the wall, so the water it stands in is what's under it.
    static void Dock(MeshKit kit, MeshKit walk, Wall w, float at, float length, float width, Tables tables)
    {
        float deck = tables.GroundAt(Flat(w.At(at, 0f, 0f))) + DockAbove;
        var rng = new System.Random(DockSeed);
        for (float d = 0f; d < length; d += PlankWide)
        {
            float tone = 0.8f + 0.4f * (float)rng.NextDouble(), ragged = 0.92f + 0.08f * (float)rng.NextDouble();
            var color = new Color(Timber.r * tone * 1.5f, Timber.g * tone * 1.5f, Timber.b * tone * 1.5f);
            kit.Box(w.At(at, -d - PlankWide / 2f, deck - PlankThick / 2f), new Vector3(width * ragged, PlankThick, PlankWide - PlankGap), color, w.Turn);
        }
        foreach (float side in new[] { -1f, 1f })
        {
            kit.Box(w.At(at + side * (width / 2f - DockBeam), -length / 2f, deck - PlankThick - DockBeam / 2f), new Vector3(DockBeam, DockBeam, length), Timber, w.Turn);
            for (float d = PostEvery; d <= length; d += PostEvery)
            {
                float top = d + PostEvery > length ? deck + PostAbove : deck;
                float foot = deck - PostDown;
                kit.Box(w.At(at + side * (width / 2f), -d, (top + foot) / 2f), new Vector3(PostSize, top - foot, PostSize), Timber, w.Turn);
            }
        }
        walk.Box(w.At(at, -length / 2f, deck - PlankThick / 2f), new Vector3(width, PlankThick, length), Color.clear, w.Turn);
    }

    static Vector2 Flat(Vector3 p) => new(p.x, p.z);

    // A detail goes with the stretch it's fixed to.
    static void Held(MeshKit kit, Wall w, float u, Action draw)
    {
        var holder = w.Stretches.FirstOrDefault(s => u <= s.To) ?? w.Stretches.LastOrDefault();
        if (holder == null) draw();
        else holder.Whole.Add(Record(kit, draw));
    }

    static void Vigas(MeshKit kit, Wall w, float every)
    {
        for (float u = every / 2f; u < w.Length; u += Mathf.Max(0.2f, every))
        {
            float along = u;
            Held(kit, w, along, () => kit.Cylinder(w.At(along, 0f, w.Bottom + w.Height - 0.4f), w.At(along, -VigaLength, w.Bottom + w.Height - 0.4f), VigaRadius, VigaSides, Timber));
        }
    }

    static void Beams(MeshKit kit, Wall w, float every)
    {
        for (float u = every / 2f; u < w.Length; u += Mathf.Max(0.2f, every))
        {
            float along = u;
            Held(kit, w, along, () => kit.Box(w.At(along, -BeamSize / 2f, w.Bottom + w.Height / 2f), new Vector3(BeamSize, w.Height, BeamSize), Timber, w.Turn));
        }
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
