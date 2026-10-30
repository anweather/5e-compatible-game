using Chem;
using UnityEngine;

// Builds one village building's meshes from its plan (VillageBuilding, D12) and
// its own seed, with MeshKit: walls (timber frame with plaster between the
// beams and the upper storey jutting out, stone in courses of jittered blocks,
// or logs), a thatch, slate or shingle roof, gabled or hipped, a chimney, a
// door with a step, shuttered windows that glow at night, flower boxes, and
// the inn's hanging sign or the shop's striped awning. Two meshes, the body
// and the roof, so the cabins' burn rules work on it (ValleyChemistry hides a
// burnt building's "Roof" and chars the rest).
//
// Local frame: x along the front, z from front to back with the door on the -z
// side, y up from the ground; the origin is the footprint's centre. Looks only:
// nothing here reaches the chemistry, and the same seed always looks the same.
public static class BuildingMesher
{
    public struct Spec
    {
        public BuildingKind kind;
        public WallKind walls;
        public RoofKind roof;
        public RoofCover cover;
        public int storeys;
        public bool chimney;
        public int seed;
        /// <summary>The footprint in metres: along the front, and front to back.</summary>
        public float width, depth;
        /// <summary>The plan's windows: x its wall (Front, Back, Left, Right), y its place along that wall in metres; null, the mesher spaces them itself.</summary>
        public Vector2[] windows;
        /// <summary>Where the plan puts the door along the front (m), or null for the seed's choice.</summary>
        public float? doorAt;
        public const int Front = 0, Back = 1, Left = 2, Right = 3;
    }

    public struct Result
    {
        public Mesh body, roof;
        /// <summary>The top of the chimney (for smoke), or NaN without one.</summary>
        public Vector3 chimneyTop;
        /// <summary>Where a lamp hangs by the door.</summary>
        public Vector3 lamp;
        /// <summary>The walls' extent and the roof's top, for a collider.</summary>
        public Vector3 size;
        /// <summary>The walls' top (the eaves): a collider for the walls stops here, the roof has its own.</summary>
        public float eaves;
        /// <summary>Things on the walls to bump into, as local boxes: window boxes and planters.</summary>
        public System.Collections.Generic.List<(Vector3 at, Vector3 size)> solids;
        /// <summary>The windows' glass (WindowPanes).</summary>
        public WindowPanes.Pane[] panes;
    }

    const float Plinth = 0.35f, Storey = 2.7f, Inset = 0.15f, Jetty = 0.3f;

    static Color Rgb(float r, float g, float b) => new(r, g, b, 0f);

    static readonly Color[] Plasters =
    {
        Rgb(0.92f, 0.87f, 0.75f), Rgb(0.93f, 0.92f, 0.88f), Rgb(0.86f, 0.74f, 0.52f), Rgb(0.88f, 0.77f, 0.72f), Rgb(0.76f, 0.81f, 0.82f),
    };
    static readonly Color[] Timbers = { Rgb(0.25f, 0.17f, 0.11f), Rgb(0.33f, 0.23f, 0.15f), Rgb(0.19f, 0.16f, 0.15f) };
    static readonly Color[] Stones = { Rgb(0.56f, 0.56f, 0.54f), Rgb(0.62f, 0.57f, 0.49f), Rgb(0.45f, 0.45f, 0.46f) };
    static readonly Color[] Thatches = { Rgb(0.72f, 0.6f, 0.36f), Rgb(0.63f, 0.52f, 0.3f) };
    static readonly Color[] Slates = { Rgb(0.28f, 0.3f, 0.34f), Rgb(0.36f, 0.37f, 0.41f) };
    static readonly Color[] Shingles = { Rgb(0.45f, 0.3f, 0.2f), Rgb(0.52f, 0.36f, 0.23f), Rgb(0.6f, 0.28f, 0.2f) };
    static readonly Color[] Paints = { Rgb(0.25f, 0.42f, 0.3f), Rgb(0.25f, 0.35f, 0.55f), Rgb(0.55f, 0.2f, 0.18f), Rgb(0.4f, 0.3f, 0.2f) };
    static readonly Color[] Flowers = { Rgb(0.85f, 0.2f, 0.25f), Rgb(0.95f, 0.8f, 0.2f), Rgb(0.6f, 0.35f, 0.8f), Rgb(0.95f, 0.95f, 0.9f) };
    static readonly Color Log = Rgb(0.42f, 0.29f, 0.18f);
    static readonly Color Lit = new(1f, 0.76f, 0.42f, 1f);
    static readonly Color Leaves = Rgb(0.22f, 0.4f, 0.18f);

    // One building being built: its look, its sizes, and the shapes so far.
    sealed class Build
    {
        public System.Random rng;
        public Spec s;
        public MeshKit kit;
        public readonly System.Collections.Generic.List<(Vector3 at, Vector3 size)> solids = new();
        public readonly System.Collections.Generic.List<WindowPanes.Pane> panes = new();
        public Color wall, timber, stone, roof, paint, door;
        // The walls' half extents (ground storey), their top, the roof's overhang, pitch and rise.
        public float ex, ez, top, over, pitch, rise, doorX;

        public float Range(float lo, float hi) => lo + (float)rng.NextDouble() * (hi - lo);
        public bool Chance(float p) => rng.NextDouble() < p;
        public T Pick<T>(T[] from) => from[rng.Next(from.Length)];

        public Color Jitter(Color c, float by)
        {
            float k = 1f + Range(-by, by);
            return new Color(Mathf.Clamp01(c.r * k), Mathf.Clamp01(c.g * k), Mathf.Clamp01(c.b * k), c.a);
        }

        // The half depth of a storey: the timber frame's upper storeys jut out front and back.
        public float StoreyEz(int storey) => s.walls == WallKind.Timber && storey > 0 ? ez + Jetty : ez;
    }

    // Where the door stands along the front: the plan's, else the seed's for a house, the middle for the rest.
    static float DoorX(Build b)
    {
        float slack = Mathf.Max(0f, b.ex - 1.6f);
        float seeded = b.s.kind == BuildingKind.House ? b.Range(-slack, slack) : 0f;
        return b.s.doorAt ?? seeded;
    }

    public static Result Make(Spec spec)
    {
        var b = new Build { rng = new System.Random(spec.seed), s = spec, kit = new MeshKit() };
        b.wall = b.Jitter(b.Pick(Plasters), 0.04f);
        b.timber = b.Pick(Timbers);
        b.stone = b.Pick(Stones);
        b.paint = b.Pick(Paints);
        b.door = b.Chance(0.5f) ? b.paint : b.timber;
        b.roof = spec.cover switch
        {
            RoofCover.Thatch => b.Pick(Thatches),
            RoofCover.Slate => b.Pick(Slates),
            _ => b.Pick(Shingles),
        };
        b.ex = spec.width / 2f - Inset;
        b.ez = spec.depth / 2f - Inset;
        int storeys = spec.walls == WallKind.Log ? 1 : Mathf.Max(1, spec.storeys);
        b.s.storeys = storeys;
        b.top = Plinth + storeys * Storey;
        b.pitch = (spec.cover == RoofCover.Thatch ? 50f : 40f) * Mathf.Deg2Rad;
        b.over = spec.cover == RoofCover.Thatch ? 0.55f : 0.4f;
        b.rise = (b.StoreyEz(storeys - 1) + b.over) * Mathf.Tan(b.pitch) - b.over * Mathf.Tan(b.pitch);
        b.doorX = DoorX(b);


        var result = new Result { chimneyTop = new Vector3(float.NaN, 0f, 0f) };
        var kit = b.kit;
        kit.Box(new Vector3(0f, Plinth / 2f, 0f), new Vector3(b.ex * 2f + 0.25f, Plinth, b.ez * 2f + 0.25f), b.Jitter(b.stone, 0.05f));
        for (int st = 0; st < storeys; st++) Walls(b, st);
        if (spec.roof == RoofKind.Gable) Gables(b);
        Door(b);
        for (int st = 0; st < storeys; st++) Windows(b, st);
        if (spec.chimney) result.chimneyTop = Chimney(b);
        if (spec.kind == BuildingKind.Inn) result.lamp = Sign(b);
        else if (spec.kind == BuildingKind.Shop) result.lamp = Awning(b);
        else result.lamp = new Vector3(b.doorX + 0.85f, Plinth + 2.1f, -b.ez - 0.25f);
        result.body = kit.ToMesh("Building");

        kit.Clear();
        if (spec.roof == RoofKind.Gable) GableRoof(b);
        else HipRoof(b);
        result.roof = kit.ToMesh("Roof");
        float ridge = b.top + b.rise + 0.2f;
        result.size = new Vector3(b.ex * 2f + 0.3f, ridge, b.StoreyEz(storeys - 1) * 2f + 0.3f);
        result.eaves = b.top;
        result.solids = b.solids;
        result.panes = b.panes.ToArray();
        return result;
    }

    // --- walls ------------------------------------------------------------------------

    static void Walls(Build b, int storey)
    {
        float y0 = Plinth + storey * Storey, ez = b.StoreyEz(storey);
        var kit = b.kit;
        switch (b.s.walls)
        {
            case WallKind.Timber:
                kit.Box(new Vector3(0f, y0 + Storey / 2f, 0f), new Vector3(b.ex * 2f, Storey, ez * 2f), b.wall);
                Frame(b, y0, ez);
                break;
            case WallKind.Stone:
                kit.Box(new Vector3(0f, y0 + Storey / 2f, 0f), new Vector3(b.ex * 2f, Storey, ez * 2f), b.stone);
                Blocks(b, y0, ez);
                break;
            default:
                Logs(b, y0, ez);
                break;
        }
    }

    // The timber frame on the plaster: corner posts, a sill and a plate, studs, and on the front a
    // pair of braces beside the middle.
    static void Frame(Build b, float y0, float ez)
    {
        const float beam = 0.18f, proud = 0.05f;
        var kit = b.kit;
        float y1 = y0 + Storey, ym = y0 + Storey / 2f;
        for (int fx = -1; fx <= 1; fx += 2)
            for (int fz = -1; fz <= 1; fz += 2)
                kit.Box(new Vector3(fx * b.ex, ym, fz * ez), new Vector3(beam + proud * 2f, Storey, beam + proud * 2f), b.timber);
        foreach (float y in new[] { y0 + beam / 2f, y1 - beam / 2f })
        {
            kit.Box(new Vector3(0f, y, -ez), new Vector3(b.ex * 2f, beam, beam + proud * 2f), b.timber);
            kit.Box(new Vector3(0f, y, ez), new Vector3(b.ex * 2f, beam, beam + proud * 2f), b.timber);
            kit.Box(new Vector3(-b.ex, y, 0f), new Vector3(beam + proud * 2f, beam, ez * 2f), b.timber);
            kit.Box(new Vector3(b.ex, y, 0f), new Vector3(beam + proud * 2f, beam, ez * 2f), b.timber);
        }
        int studs = Mathf.Max(1, Mathf.RoundToInt(b.ex * 2f / 1.3f));
        for (int i = 1; i < studs; i++)
        {
            float x = -b.ex + i * b.ex * 2f / studs;
            kit.Box(new Vector3(x, ym, -ez), new Vector3(beam * 0.8f, Storey, beam + proud), b.timber);
            kit.Box(new Vector3(x, ym, ez), new Vector3(beam * 0.8f, Storey, beam + proud), b.timber);
        }
        int sideStuds = Mathf.Max(1, Mathf.RoundToInt(ez * 2f / 1.3f));
        for (int i = 1; i < sideStuds; i++)
        {
            float z = -ez + i * ez * 2f / sideStuds;
            kit.Box(new Vector3(-b.ex, ym, z), new Vector3(beam + proud, Storey, beam * 0.8f), b.timber);
            kit.Box(new Vector3(b.ex, ym, z), new Vector3(beam + proud, Storey, beam * 0.8f), b.timber);
        }
        if (y0 > Plinth)
        {
            // Under the jutting storey, the joists' ends.
            for (float x = -b.ex + 0.3f; x < b.ex; x += 0.6f)
            {
                kit.Box(new Vector3(x, y0 - 0.06f, -ez + Jetty / 2f), new Vector3(0.12f, 0.12f, Jetty + 0.1f), b.timber);
                kit.Box(new Vector3(x, y0 - 0.06f, ez - Jetty / 2f), new Vector3(0.12f, 0.12f, Jetty + 0.1f), b.timber);
            }
        }
        // Two braces on the front, leaning in toward the corners.
        float braceLength = Mathf.Sqrt(1.1f * 1.1f + Storey * Storey * 0.6f);
        float lean = Mathf.Atan2(1.1f, Storey * 0.78f) * Mathf.Rad2Deg;
        for (int fx = -1; fx <= 1; fx += 2)
        {
            var at = new Vector3(fx * (b.ex - 0.65f), ym, -ez - proud);
            kit.Box(at, new Vector3(beam * 0.8f, braceLength, beam), b.timber, Quaternion.Euler(0f, 0f, fx * lean));
        }
    }

    // Stone in courses: blocks of jittered tone standing a little proud of the wall, with
    // big pale quoins at the corners.
    static void Blocks(Build b, float y0, float ez)
    {
        const float course = 0.42f, proud = 0.04f;
        var kit = b.kit;
        int rows = Mathf.FloorToInt(Storey / course);
        for (int r = 0; r < rows; r++)
        {
            float y = y0 + (r + 0.5f) * course;
            Course(b, y, course, new Vector3(0f, 0f, -ez - proud / 2f), Vector3.right, b.ex * 2f, r);
            Course(b, y, course, new Vector3(0f, 0f, ez + proud / 2f), Vector3.right, b.ex * 2f, r + 1);
            Course(b, y, course, new Vector3(-b.ex - proud / 2f, 0f, 0f), Vector3.forward, ez * 2f, r);
            Course(b, y, course, new Vector3(b.ex + proud / 2f, 0f, 0f), Vector3.forward, ez * 2f, r + 1);
            var quoin = b.Jitter(b.stone * 1.12f, 0.03f);
            float qx = r % 2 == 0 ? 0.55f : 0.35f;
            for (int fx = -1; fx <= 1; fx += 2)
                for (int fz = -1; fz <= 1; fz += 2)
                {
                    var corner = new Vector3(fx * (b.ex + proud), y, fz * (ez + proud));
                    kit.Box(corner + new Vector3(-fx * qx / 2f, 0f, 0f), new Vector3(qx, course * 0.92f, 0.12f), quoin);
                    kit.Box(corner + new Vector3(0f, 0f, -fz * (0.9f - qx) / 2f), new Vector3(0.12f, course * 0.92f, 0.9f - qx), quoin);
                }
        }
    }

    // One course of blocks along a face, about two in three of them standing out.
    static void Course(Build b, float y, float course, Vector3 face, Vector3 along, float length, int row)
    {
        float x = -length / 2f + (row % 2 == 0 ? 0f : 0.3f) + 0.2f;
        var across = Vector3.Cross(Vector3.up, along);
        while (x < length / 2f - 0.5f)
        {
            float w = b.Range(0.45f, 0.9f);
            w = Mathf.Min(w, length / 2f - 0.25f - x);
            if (b.Chance(0.65f))
            {
                var size = along * (w - 0.05f) + Vector3.up * (course * 0.86f) + across * 0.05f;
                b.kit.Box(face + along * (x + w / 2f) + Vector3.up * y, Abs(size), b.Jitter(b.stone, 0.1f));
            }
            x += w;
        }
    }

    static Vector3 Abs(Vector3 v) => new(Mathf.Abs(v.x), Mathf.Abs(v.y), Mathf.Abs(v.z));

    // Log walls: courses of logs front and back along x, the sides along z half a log higher, the
    // ends running past the corners as on a real log cabin.
    static void Logs(Build b, float y0, float ez)
    {
        const float r = 0.17f;
        int courses = Mathf.FloorToInt(Storey / (r * 2f));
        for (int i = 0; i < courses; i++)
        {
            float y = y0 + r + i * r * 2f;
            var c = b.Jitter(Log, 0.08f);
            b.kit.Cylinder(new Vector3(-b.ex - 0.3f, y, -ez), new Vector3(b.ex + 0.3f, y, -ez), r, 7, c);
            b.kit.Cylinder(new Vector3(-b.ex - 0.3f, y, ez), new Vector3(b.ex + 0.3f, y, ez), r, 7, c);
            b.kit.Cylinder(new Vector3(-b.ex, y + r, -ez - 0.3f), new Vector3(-b.ex, y + r, ez + 0.3f), r, 7, c);
            b.kit.Cylinder(new Vector3(b.ex, y + r, -ez - 0.3f), new Vector3(b.ex, y + r, ez + 0.3f), r, 7, c);
        }
        // A wall behind the logs, so no gaps show between them.
        b.kit.Box(new Vector3(0f, y0 + Storey / 2f, 0f), new Vector3(b.ex * 2f - 0.1f, Storey, ez * 2f - 0.1f), Log * 0.6f);
    }

    // The gable ends under a gabled roof: wall-coloured triangles, with a king post on timber.
    static void Gables(Build b)
    {
        float ez = b.StoreyEz(b.s.storeys - 1);
        var color = b.s.walls switch
        {
            WallKind.Timber => b.wall,
            WallKind.Stone => b.stone,
            _ => Log,
        };
        b.kit.GableEnds(b.ex * 2f, ez * 2f, b.top, b.rise, color);
        if (b.s.walls != WallKind.Timber) return;
        for (int fx = -1; fx <= 1; fx += 2)
            b.kit.Box(new Vector3(fx * (b.ex + 0.03f), b.top + b.rise * 0.45f, 0f), new Vector3(0.1f, b.rise * 0.9f, 0.16f), b.timber);
    }

    // --- openings ---------------------------------------------------------------------

    static void Door(Build b)
    {
        float z = -b.ez, x = b.doorX;
        var kit = b.kit;
        // Stood clear of the wall's face: a stone wall's jittered blocks poked through a door set
        // in it (Andrew's playtest #4).
        kit.Box(new Vector3(x, Plinth + 1.1f, z - 0.12f), new Vector3(1.3f, 2.25f, 0.2f), b.timber);
        kit.Box(new Vector3(x, Plinth + 1.02f, z - 0.18f), new Vector3(1.0f, 2.02f, 0.1f), b.door);
        kit.Box(new Vector3(x + 0.35f, Plinth + 1.0f, z - 0.25f), new Vector3(0.08f, 0.08f, 0.06f), Rgb(0.7f, 0.6f, 0.3f));
        kit.Box(new Vector3(x, Plinth / 2f, z - 0.45f), new Vector3(1.4f, Plinth, 0.6f), b.Jitter(b.stone, 0.05f));
    }

    static void Windows(Build b, int storey)
    {
        float y = Plinth + storey * Storey + 1.45f, ez = b.StoreyEz(storey);
        if (b.s.windows != null && b.s.windows.Length > 0)
        {
            foreach (var w in b.s.windows) PlannedWindow(b, w, y, ez);
            return;
        }
        int count = Mathf.Max(1, Mathf.FloorToInt(b.ex * 2f / 2.2f));
        for (int i = 0; i < count; i++)
        {
            float x = -b.ex + (i + 0.5f) * b.ex * 2f / count;
            bool byDoor = storey == 0 && Mathf.Abs(x - b.doorX) < 1.2f;
            if (!byDoor) Window(b, new Vector3(x, y, -ez), Vector3.back);
            if (b.Chance(0.8f)) Window(b, new Vector3(x, y, ez), Vector3.forward);
        }
        if (ez * 2f < 3.8f) return;
        Window(b, new Vector3(-b.ex, y, 0f), Vector3.left);
        Window(b, new Vector3(b.ex, y, 0f), Vector3.right);
    }

    // A window from the plan: x its wall (Spec.windows), y its place along that wall.
    static void PlannedWindow(Build b, Vector2 w, float y, float ez)
    {
        switch ((int)w.x)
        {
            case Spec.Front: Window(b, new Vector3(w.y, y, -ez), Vector3.back); break;
            case Spec.Back: Window(b, new Vector3(w.y, y, ez), Vector3.forward); break;
            case Spec.Left: Window(b, new Vector3(-b.ex, y, w.y), Vector3.left); break;
            default: Window(b, new Vector3(b.ex, y, w.y), Vector3.right); break;
        }
    }

    // A lit window in a wall facing out, with a frame, shutters folded back and maybe flowers.
    static void Window(Build b, Vector3 at, Vector3 facing)
    {
        var kit = b.kit;
        var along = Vector3.Cross(Vector3.up, facing);
        Vector3 Size(float a, float h, float d) => Abs(along * a + Vector3.up * h + facing * d);
        kit.Box(at + facing * 0.06f, Size(0.95f, 1.05f, 0.08f), b.timber);
        kit.Box(at + facing * 0.09f, Size(0.75f, 0.85f, 0.04f), b.Jitter(Lit, 0.08f));
        // Mullions: a cross of timber over the glass.
        kit.Box(at + facing * 0.12f, Size(0.06f, 0.85f, 0.03f), b.timber);
        kit.Box(at + facing * 0.12f, Size(0.75f, 0.06f, 0.03f), b.timber);
        // Its glass, over all that (WindowPanes draws it, apart from the walls).
        b.panes.Add(new WindowPanes.Pane { at = at + facing * 0.145f, along = along * 0.375f, up = Vector3.up * 0.425f, facing = facing });
        if (b.s.walls != WallKind.Log)
            for (int side = -1; side <= 1; side += 2)
                kit.Box(at + along * (side * 0.72f) + facing * 0.15f, Size(0.4f, 0.95f, 0.05f), b.paint); // clear of the wall and its beams
        if (!b.Chance(0.35f)) return;
        var box = at + Vector3.down * 0.6f + facing * 0.18f;
        kit.Box(box, Size(0.95f, 0.18f, 0.25f), b.timber);
        b.solids.Add((box + Vector3.up * 0.1f, Size(0.95f, 0.4f, 0.25f)));
        kit.Box(box + Vector3.up * 0.12f, Size(0.85f, 0.08f, 0.2f), Leaves);
        for (int f = 0; f < 4; f++)
            kit.Box(box + along * (-0.32f + f * 0.21f) + Vector3.up * 0.18f, Vector3.one * 0.11f, b.Pick(Flowers));
    }

    // --- extras -----------------------------------------------------------------------

    // A stone chimney up the inside of one gable end, through the roof, with a cap.
    static Vector3 Chimney(Build b)
    {
        float x = (b.Chance(0.5f) ? 1f : -1f) * (b.ex - 0.45f), z = b.ez * 0.35f;
        float ridgeY = b.top + b.rise;
        float height = ridgeY + 0.9f;
        var c = b.Jitter(b.stone, 0.06f);
        b.kit.Box(new Vector3(x, height / 2f, z), new Vector3(0.7f, height, 0.7f), c);
        b.kit.Box(new Vector3(x, height + 0.06f, z), new Vector3(0.85f, 0.12f, 0.85f), c * 0.85f);
        return new Vector3(x, height + 0.2f, z);
    }

    // The inn's sign hanging from a bracket by the door, a painted board with a gilt emblem.
    static Vector3 Sign(Build b)
    {
        float x = b.ex * 0.55f, y = Plinth + Storey - 0.1f, z = -b.ez;
        var kit = b.kit;
        kit.Box(new Vector3(x, y, z - 0.55f), new Vector3(0.1f, 0.1f, 1.1f), b.timber);
        kit.Box(new Vector3(x, y - 0.25f, z - 0.15f), new Vector3(0.08f, 0.5f, 0.08f), b.timber);
        kit.Box(new Vector3(x, y - 0.55f, z - 0.8f), new Vector3(0.06f, 0.65f, 0.9f), b.paint);
        kit.Box(new Vector3(x, y - 0.55f, z - 0.8f), new Vector3(0.09f, 0.3f, 0.3f), Rgb(0.85f, 0.7f, 0.25f));
        for (int k = -1; k <= 1; k += 2) kit.Box(new Vector3(x, y - 0.15f, z - 0.8f + k * 0.35f), new Vector3(0.02f, 0.2f, 0.02f), Rgb(0.2f, 0.2f, 0.2f));
        // A lantern by the door.
        var lamp = new Vector3(-0.95f, Plinth + 2.2f, z - 0.22f);
        kit.Box(lamp, new Vector3(0.22f, 0.3f, 0.22f), Lit);
        kit.Box(lamp + Vector3.up * 0.2f, new Vector3(0.28f, 0.08f, 0.28f), Rgb(0.15f, 0.15f, 0.15f));
        return lamp + Vector3.back * 0.3f;
    }

    // The shop's striped awning over its front, and a couple of crates under it.
    static Vector3 Awning(Build b)
    {
        float y0 = Plinth + 2.65f, z = -b.ez, reach = 1.3f, drop = 0.5f;
        float half = b.ex - 0.2f;
        var stripe = Rgb(0.93f, 0.9f, 0.82f);
        int n = Mathf.Max(2, Mathf.RoundToInt(half * 2f / 0.45f));
        for (int i = 0; i < n; i++)
        {
            float x0 = -half + i * half * 2f / n, x1 = x0 + half * 2f / n;
            var c = i % 2 == 0 ? b.paint : stripe;
            b.kit.Sheet(new Vector3(x0, y0, z - 0.05f), new Vector3(x1, y0, z - 0.05f), new Vector3(x1, y0 - drop, z - reach), new Vector3(x0, y0 - drop, z - reach), c);
            b.kit.Sheet(new Vector3(x0, y0 - drop, z - reach), new Vector3(x1, y0 - drop, z - reach), new Vector3(x1, y0 - drop - 0.25f, z - reach), new Vector3(x0, y0 - drop - 0.25f, z - reach), c);
        }
        for (int k = 0; k < 2; k++)
        {
            float x = (k == 0 ? -1f : 1f) * Mathf.Min(half - 0.4f, 1.6f);
            b.kit.Box(new Vector3(x, 0.3f, z - 0.75f), new Vector3(0.6f, 0.6f, 0.55f), b.Jitter(Rgb(0.55f, 0.4f, 0.24f), 0.1f));
            b.solids.Add((new Vector3(x, 0.36f, z - 0.75f), new Vector3(0.6f, 0.72f, 0.55f)));
            b.kit.Box(new Vector3(x, 0.66f, z - 0.75f), new Vector3(0.5f, 0.12f, 0.45f), b.Pick(Flowers));
        }
        return new Vector3(0f, y0 - 0.3f, z - 0.6f);
    }

    // --- roofs ------------------------------------------------------------------------

    // Two slopes in overlapping courses (thick bundles for thatch, thin rows for slate and
    // shingle), a ridge along x, and eaves over the walls and gables.
    static void GableRoof(Build b)
    {
        float ez = b.StoreyEz(b.s.storeys - 1);
        float run = ez + b.over, slope = run / Mathf.Cos(b.pitch);
        float ridgeY = b.top + b.rise, width = b.ex * 2f + b.over * 1.2f;
        float thick = b.s.cover == RoofCover.Thatch ? 0.32f : 0.09f;
        int courses = b.s.cover == RoofCover.Thatch ? 5 : Mathf.Max(6, Mathf.RoundToInt(slope / 0.32f));
        var kit = b.kit;
        for (int side = -1; side <= 1; side += 2)
        {
            var down = new Vector3(0f, -Mathf.Sin(b.pitch), side * Mathf.Cos(b.pitch));
            var turn = Quaternion.LookRotation(down, new Vector3(0f, Mathf.Cos(b.pitch), side * Mathf.Sin(b.pitch)));
            float step = slope / courses;
            for (int i = 0; i < courses; i++)
            {
                // Each course lies a little on the one below it, so the roof shows rows.
                var at = new Vector3(0f, ridgeY, 0f) + down * ((i + 0.5f) * step) + turn * Vector3.up * (thick * 0.5f + (i % 2) * 0.015f);
                kit.Box(at, new Vector3(width, thick, step * 1.12f), b.Jitter(b.roof, 0.07f), turn);
            }
        }
        // Ridge cap.
        if (b.s.cover == RoofCover.Thatch)
            kit.Cylinder(new Vector3(-width / 2f, ridgeY + 0.12f, 0f), new Vector3(width / 2f, ridgeY + 0.12f, 0f), 0.3f, 8, b.roof * 0.9f);
        else
            kit.Box(new Vector3(0f, ridgeY + 0.1f, 0f), new Vector3(width, 0.18f, 0.32f), b.roof * 0.8f, Quaternion.Euler(45f, 0f, 0f));
    }

    // Four slopes meeting at a short ridge, one colour each with a darker underside.
    static void HipRoof(Build b)
    {
        float ez = b.StoreyEz(b.s.storeys - 1);
        float tan = Mathf.Tan(b.pitch);
        float eaveY = b.top - b.over * tan, ridgeY = b.top + b.rise;
        float hx = b.ex + b.over, hz = ez + b.over, r = Mathf.Max(0.15f, hx - hz);
        float lift = b.s.cover == RoofCover.Thatch ? 0.25f : 0.08f;
        Vector3 E1 = new(-hx, eaveY, -hz), E2 = new(hx, eaveY, -hz), E3 = new(hx, eaveY, hz), E4 = new(-hx, eaveY, hz);
        Vector3 R1 = new(-r, ridgeY, 0f), R2 = new(r, ridgeY, 0f);
        var kit = b.kit;
        var under = b.roof * 0.55f;
        void Face(Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3, Vector3 outward, bool tri)
        {
            var up = Vector3.up * lift;
            if (tri) kit.Tri(p0 + up, p1 + up, p2 + up, b.Jitter(b.roof, 0.05f), outward);
            else kit.Quad(p0 + up, p1 + up, p2 + up, p3 + up, b.Jitter(b.roof, 0.05f), outward);
            if (tri) kit.Tri(p0, p1, p2, under, -outward);
            else kit.Quad(p0, p1, p2, p3, under, -outward);
        }
        Face(E1, E2, R2, R1, new Vector3(0f, 1f, -1f), false);
        Face(E3, E4, R1, R2, new Vector3(0f, 1f, 1f), false);
        Face(E4, E1, R1, default, new Vector3(-1f, 1f, 0f), true);
        Face(E2, E3, R2, default, new Vector3(1f, 1f, 0f), true);
        // The eaves' edge, so the roof has a thickness.
        kit.Box(new Vector3(0f, eaveY + lift / 2f, -hz), new Vector3(hx * 2f, lift, 0.06f), b.roof * 0.75f);
        kit.Box(new Vector3(0f, eaveY + lift / 2f, hz), new Vector3(hx * 2f, lift, 0.06f), b.roof * 0.75f);
        kit.Box(new Vector3(-hx, eaveY + lift / 2f, 0f), new Vector3(0.06f, lift, hz * 2f), b.roof * 0.75f);
        kit.Box(new Vector3(hx, eaveY + lift / 2f, 0f), new Vector3(0.06f, lift, hz * 2f), b.roof * 0.75f);
        kit.Box(new Vector3(0f, ridgeY + lift + 0.05f, 0f), new Vector3(r * 2f + 0.3f, 0.16f, 0.26f), b.roof * 0.8f, Quaternion.Euler(45f, 0f, 0f));
    }
}
