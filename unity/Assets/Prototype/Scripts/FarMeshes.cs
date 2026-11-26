using System.Collections.Generic;
using Chem;
using Newtonsoft.Json.Linq;
using UnityEngine;

// The far ring's meshes as plain arrays (FarRing, docs/design/world-squares.md section 4), so they can be worked out off
// the main thread and tested without a scene: a known chunk's coarse land from its 9 × 9 heights, painted by its dominant
// biome, with a skirt round it so no crack shows where it meets a chunk terrain; a far landmark's stand-in shape by its
// horizon.json profile; and the sea plane round the map out to the horizon. Only drawn: nothing here reads or changes
// the cells (D4).
public static class FarMeshes
{
    public sealed class Data
    {
        public readonly List<Vector3> Vertices = new();
        public readonly List<Vector3> Normals = new();
        public readonly List<Color32> Colors = new();
        public readonly List<int> Triangles = new();

        public Mesh ToMesh(string name)
        {
            var mesh = new Mesh { name = name };
            if (Vertices.Count > 65535) mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            mesh.SetVertices(Vertices);
            mesh.SetNormals(Normals);
            mesh.SetColors(Colors);
            mesh.SetTriangles(Triangles, 0);
            mesh.RecalculateBounds();
            return mesh;
        }

        int Add(Vector3 v, Vector3 n, Color32 c)
        {
            Vertices.Add(v);
            Normals.Add(n);
            Colors.Add(c);
            return Vertices.Count - 1;
        }

        internal void Flat(Vector3 a, Vector3 b, Vector3 c, Color32 colour)
        {
            var n = Vector3.Cross(b - a, c - a).normalized;
            Triangles.Add(Add(a, n, colour));
            Triangles.Add(Add(b, n, colour));
            Triangles.Add(Add(c, n, colour));
        }

        internal void Quad(int a, int b, int c, int d)
        {
            Triangles.Add(a);
            Triangles.Add(b);
            Triangles.Add(c);
            Triangles.Add(b);
            Triangles.Add(d);
            Triangles.Add(c);
        }

        internal int Vertex(Vector3 v, Vector3 n, Color32 c) => Add(v, n, c);
    }

    public const float SkirtMetres = 10f;

    public sealed class Palette
    {
        readonly Dictionary<string, Color> biomes = new();

        public Color Grass { get; private set; } = new(0.30f, 0.42f, 0.20f);
        public Color Rock { get; private set; } = new(0.40f, 0.39f, 0.36f);
        public Color Wood { get; private set; } = WoodOf(new Color(0.30f, 0.42f, 0.20f));

        const float CanopyShare = 0.85f;

        static Color WoodOf(Color grass) => new(grass.r * 0.42f, grass.g * 0.55f, grass.b * 0.45f);

        public Color Wooded(Color c, byte wood) => wood == 0 ? c : Color.Lerp(c, Wood, wood / 255f * CanopyShare);

        public static readonly Palette Plain = new();

        public Color Of(string biome) => biome != null && biomes.TryGetValue(biome, out var c) ? c : Grass;

        public static Palette Drawn(IReadOnlyDictionary<string, float[]> grounds, IReadOnlyDictionary<string, Color> layers)
        {
            var p = new Palette();
            if (layers.TryGetValue(CellPaint.Grass, out var grass))
            {
                p.Grass = grass;
                p.Wood = WoodOf(grass);
            }
            if (layers.TryGetValue(CellPaint.Rock, out var rock)) p.Rock = rock;
            foreach (var (id, weights) in grounds)
            {
                Color c = Color.black;
                float total = 0f;
                for (int k = 0; k < weights.Length && k < CellPaint.Layers.Count; k++)
                {
                    if (weights[k] <= 0f) continue;
                    c += weights[k] * (layers.TryGetValue(CellPaint.Layers[k], out var layer) ? layer : p.Grass);
                    total += weights[k];
                }
                p.biomes[id] = total > 0f ? new Color(c.r / total, c.g / total, c.b / total) : p.Grass;
            }
            return p;
        }
    }

    static readonly Color Water = new(0.16f, 0.30f, 0.36f), SeaFloor = new(0.09f, 0.19f, 0.22f);
    const float UnderWater = 0.4f;

    public static Data Known(KnownChunk k, float cellMetres, Palette palette = null, System.Func<int, int, float> rockAt = null)
    {
        palette ??= Palette.Plain;
        const int n = KnownChunk.Side;
        float step = KnownChunk.Step * cellMetres, unit = (float)MapFile.LandUnitMetres;
        var d = new Data();
        var biome = palette.Of(k.Biome);
        var biomes = k.Biomes;
        float H(int i, int j)
        {
            int q = Mathf.Clamp(j, 0, n - 1) * n + Mathf.Clamp(i, 0, n - 1);
            return k.Heights[q] * unit - (k.Water[q] is KnownChunk.Lake or KnownChunk.River ? UnderWater : 0f);
        }
        for (int j = 0; j < n; j++)
            for (int i = 0; i < n; i++)
            {
                float dx = (H(i + 1, j) - H(i - 1, j)) / (step * (i == 0 || i == n - 1 ? 1 : 2));
                float dz = (H(i, j - 1) - H(i, j + 1)) / (step * (j == 0 || j == n - 1 ? 1 : 2));
                var normal = new Vector3(-dx, 1f, -dz).normalized;
                int at = j * n + i, x = k.X * ChunkGen.Size + i * KnownChunk.Step, y = k.Y * ChunkGen.Size + j * KnownChunk.Step;
                var own = palette.Wooded(biomes?[at] is { } b ? palette.Of(b) : biome, k.Wood[at]);
                d.Vertex(new Vector3(i * step, H(i, j), -j * step), normal, LandColour(own, palette.Rock, normal, k.Water[at], x, y, rockAt?.Invoke(x, y) ?? 0f));
            }
        for (int j = 0; j < n - 1; j++)
            for (int i = 0; i < n - 1; i++)
            {
                int a = j * n + i;
                d.Quad(a, a + 1, a + n, a + n + 1);
            }
        Skirt(d, n);
        return d;
    }

    public static Color32 LandColour(Color biome, Color rockColour, Vector3 normal, byte water, int x, int y, float rock = 0f)
    {
        Color c = water switch
        {
            KnownChunk.River or KnownChunk.Lake => Water,
            KnownChunk.Sea => SeaFloor,
            _ => Color.Lerp(biome, rockColour, Mathf.Max(Mathf.InverseLerp(0.85f, 0.6f, normal.y), rock)),
        };
        uint hash = (uint)(x * 73856093 ^ y * 19349663);
        float vary = 0.94f + 0.12f * ((hash >> 8) % 1000) / 1000f;
        return new Color(c.r * vary, c.g * vary, c.b * vary, 0f);
    }

    static void Skirt(Data d, int n)
    {
        var rim = new List<int>(4 * (n - 1));
        for (int i = 0; i < n - 1; i++) rim.Add(i);
        for (int j = 0; j < n - 1; j++) rim.Add(j * n + n - 1);
        for (int i = n - 1; i > 0; i--) rim.Add((n - 1) * n + i);
        for (int j = n - 1; j > 0; j--) rim.Add(j * n);
        int first = d.Vertices.Count;
        foreach (int r in rim) d.Vertex(d.Vertices[r] + Vector3.down * SkirtMetres, d.Normals[r], d.Colors[r]);
        for (int s = 0; s < rim.Count; s++)
        {
            int a = rim[s], b = rim[(s + 1) % rim.Count], c = first + s, e = first + (s + 1) % rim.Count;
            d.Quad(a, b, c, e);
            d.Quad(b, a, e, c);
        }
    }

    static readonly Dictionary<string, Vector2[]> Profiles = new()
    {
        ["cone"] = new[] { new Vector2(1f, 0f), new Vector2(0.55f, 0.6f), new Vector2(0.12f, 1f), new Vector2(0f, 1f) },
        ["crater"] = new[]
        {
            new Vector2(1f, 0f), new Vector2(0.8f, 0.3f), new Vector2(0.6f, 0.62f), new Vector2(0.42f, 0.88f), new Vector2(0.28f, 1f),
            new Vector2(0.18f, 0.86f), new Vector2(0f, 0.78f),
        },
        ["needle"] = new[] { new Vector2(1f, 0f), new Vector2(0.55f, 0.22f), new Vector2(0.32f, 0.7f), new Vector2(0.14f, 0.95f), new Vector2(0f, 1f) },
        ["dome"] = new[] { new Vector2(1f, 0f), new Vector2(0.92f, 0.38f), new Vector2(0.7f, 0.72f), new Vector2(0.38f, 0.93f), new Vector2(0f, 1f) },
        ["tree"] = new[] { new Vector2(0.1f, 0f), new Vector2(0.1f, 0.38f), new Vector2(0.75f, 0.42f), new Vector2(1f, 0.62f), new Vector2(0.72f, 0.88f), new Vector2(0f, 1f) },
        ["keep"] = new[] { new Vector2(1f, 0f), new Vector2(1f, 1f), new Vector2(0f, 1f) },
    };

    public static IEnumerable<string> Shapes => Profiles.Keys;

    public static Color LandmarkColour(string type, string shape) => type switch
    {
        "volcano" => new Color(0.13f, 0.14f, 0.17f),
        "lake" or "port" => new Color(0.42f, 0.4f, 0.36f),
        "great tree" or "lone great tree" => new Color(0.05f, 0.09f, 0.11f),
        _ => shape switch
        {
            "needle" => new Color(0.5f, 0.48f, 0.45f),
            "cone" or "dome" => new Color(0.4f, 0.38f, 0.33f),
            _ => new Color(0.46f, 0.44f, 0.41f),
        },
    };

    static readonly Color Trunk = new(0.3f, 0.22f, 0.15f);

    public static Data Landmark(string type, string shape, float radius, float height, float sink, float apron = 0f, float drop = 0f)
    {
        if (shape == null || !Profiles.TryGetValue(shape, out var profile)) profile = Profiles[shape = "keep"];
        int sides = shape == "keep" ? 4 : 16;
        float turn = shape == "keep" ? Mathf.PI / 4f : 0f;
        var colour = LandmarkColour(type, shape);
        var d = new Data();
        Vector3 At(Vector2 p, int side, bool foot)
        {
            float a = turn + side * 2f * Mathf.PI / sides;
            return new Vector3(Mathf.Cos(a) * p.x * radius, foot ? -sink : p.y * height, Mathf.Sin(a) * p.x * radius);
        }
        for (int r = 0; r < profile.Length - 1; r++)
        {
            var lo = profile[r];
            var hi = profile[r + 1];
            Color32 c = shape == "tree" && r < 1 ? Trunk : colour;
            for (int s = 0; s < sides; s++)
            {
                Vector3 a = At(lo, s, r == 0), b = At(lo, s + 1, r == 0), e = At(hi, s, false), f = At(hi, s + 1, false);
                if (hi.x > 0f) d.Flat(a, e, f, c);
                d.Flat(a, f, b, c);
            }
        }
        if (apron > 1f)
        {
            float low = -Mathf.Max(sink, drop);
            for (int s = 0; s < sides; s++)
            {
                Vector3 a = At(new Vector2(apron, 0f), s, false), b = At(new Vector2(apron, 0f), s + 1, false);
                a.y = b.y = low;
                Vector3 e = At(profile[0], s, true), f = At(profile[0], s + 1, true);
                d.Flat(a, e, f, colour);
                d.Flat(a, f, b, colour);
            }
        }
        return d;
    }

    public readonly struct FarLook
    {
        public readonly float Stretch, Notch, Apron;

        public FarLook(float stretch, float notch, float apron)
        {
            Stretch = stretch;
            Notch = notch;
            Apron = apron;
        }

        public static readonly FarLook Plain = new(1f, 0f, 0f);
    }

    public static Dictionary<string, FarLook> FarLooks(string horizonJson)
    {
        var looks = new Dictionary<string, FarLook>();
        if (!(JObject.Parse(horizonJson)["landmarks"] is JObject rows)) return looks;
        foreach (var p in rows.Properties())
        {
            if (!(p.Value["far"] is JObject far)) continue;
            float F(string key, float plain, float least, float most) => Mathf.Clamp((float?)far[key] ?? plain, least, most);
            looks[p.Name] = new FarLook(F("stretch", 1f, 1f, 4f), F("notch", 0f, 0f, 0.5f), F("apron", 0f, 0f, 6f));
        }
        return looks;
    }

    public const byte UnderTheMap = 255;

    public static Data SeaFrame(float x0, float z0, float x1, float z1, float reach, float level, Color colour)
    {
        var d = new Data();
        Color32 c = colour;
        c.a = 0;
        var up = Vector3.up;
        int[] inner =
        {
            d.Vertex(new Vector3(x0, level, z1), up, c), d.Vertex(new Vector3(x1, level, z1), up, c),
            d.Vertex(new Vector3(x1, level, z0), up, c), d.Vertex(new Vector3(x0, level, z0), up, c),
        };
        int[] outer =
        {
            d.Vertex(new Vector3(x0 - reach, level, z1 + reach), up, c), d.Vertex(new Vector3(x1 + reach, level, z1 + reach), up, c),
            d.Vertex(new Vector3(x1 + reach, level, z0 - reach), up, c), d.Vertex(new Vector3(x0 - reach, level, z0 - reach), up, c),
        };
        for (int s = 0; s < 4; s++)
        {
            int t = (s + 1) % 4;
            d.Quad(outer[s], outer[t], inner[s], inner[t]);
        }
        Color32 under = c;
        under.a = UnderTheMap;
        int nw = d.Vertex(new Vector3(x0, level, z1), up, under), ne = d.Vertex(new Vector3(x1, level, z1), up, under);
        int se = d.Vertex(new Vector3(x1, level, z0), up, under), sw = d.Vertex(new Vector3(x0, level, z0), up, under);
        d.Quad(nw, ne, sw, se);
        return d;
    }
}
