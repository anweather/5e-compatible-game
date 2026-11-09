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
    /// <summary>A mesh's arrays, ready for Mesh.SetVertices and the rest on the main thread.</summary>
    public sealed class Data
    {
        public readonly List<Vector3> Vertices = new();
        public readonly List<Vector3> Normals = new();
        public readonly List<Color32> Colors = new();
        public readonly List<int> Triangles = new();

        /// <summary>A mesh of these arrays (32-bit indices only when it needs them).</summary>
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

        // A flat-shaded triangle, front face clockwise as seen.
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

    /// <summary>How far a known chunk's skirt hangs below its edge (m), to cover the step to a chunk terrain beside it.</summary>
    public const float SkirtMetres = 10f;

    // Each biome's far colour, close to its ground's paint (BiomeGround.Looks); a biome not here, and the pinned map, are grass.
    static readonly Dictionary<string, Color> BiomeColours = new()
    {
        ["temperate"] = new Color(0.30f, 0.42f, 0.20f),
        ["grassland"] = new Color(0.42f, 0.46f, 0.22f),
        ["mountains"] = new Color(0.38f, 0.38f, 0.33f),
        ["tundra"] = new Color(0.82f, 0.85f, 0.88f),
        ["salt-flat"] = new Color(0.84f, 0.81f, 0.72f),
        ["foggy-marsh"] = new Color(0.28f, 0.33f, 0.22f),
        ["ashlands"] = new Color(0.20f, 0.18f, 0.17f),
    };

    static readonly Color Grass = new(0.30f, 0.42f, 0.20f), Rock = new(0.40f, 0.39f, 0.36f), Water = new(0.16f, 0.30f, 0.36f),
        SeaFloor = new(0.09f, 0.19f, 0.22f);

    /// <summary>A biome's far colour (a biomes.json id; anything else, the pinned map's name too, is grass).</summary>
    public static Color BiomeColour(string biome) => biome != null && BiomeColours.TryGetValue(biome, out var c) ? c : Grass;

    /// <summary>
    /// A known chunk's land: its 9 × 9 samples <paramref name="cellMetres"/> × KnownChunk.Step apart, from its north-west
    /// corner (the mesh's origin) east along +x and south along -z, each at its height in metres; then its skirt.
    /// </summary>
    public static Data Known(KnownChunk k, float cellMetres)
    {
        const int n = KnownChunk.Side;
        float step = KnownChunk.Step * cellMetres, unit = (float)MapFile.LandUnitMetres;
        var d = new Data();
        var biome = BiomeColour(k.Biome);
        float H(int i, int j) => k.Heights[Mathf.Clamp(j, 0, n - 1) * n + Mathf.Clamp(i, 0, n - 1)] * unit;
        for (int j = 0; j < n; j++)
            for (int i = 0; i < n; i++)
            {
                // Central differences inside, one-sided at the edges: x grows east, z north (j grows south).
                float dx = (H(i + 1, j) - H(i - 1, j)) / (step * (i == 0 || i == n - 1 ? 1 : 2));
                float dz = (H(i, j - 1) - H(i, j + 1)) / (step * (j == 0 || j == n - 1 ? 1 : 2));
                var normal = new Vector3(-dx, 1f, -dz).normalized;
                d.Vertex(new Vector3(i * step, H(i, j), -j * step), normal, Paint(k, i, j, biome, normal));
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

    // A sample's colour: its water, else its biome's, turned to rock where it's steep, with a little variety.
    static Color32 Paint(KnownChunk k, int i, int j, Color biome, Vector3 normal)
    {
        byte water = k.Water[j * KnownChunk.Side + i];
        Color c = water switch
        {
            KnownChunk.River or KnownChunk.Lake => Water,
            KnownChunk.Sea => SeaFloor,
            _ => Color.Lerp(biome, Rock, Mathf.InverseLerp(0.85f, 0.6f, normal.y)),
        };
        uint hash = (uint)((k.X * 64 + i * 8) * 73856093 ^ (k.Y * 64 + j * 8) * 19349663);
        float vary = 0.94f + 0.12f * ((hash >> 8) % 1000) / 1000f;
        return new Color(c.r * vary, c.g * vary, c.b * vary, 0f);
    }

    // A wall hanging SkirtMetres down from the grid's rim, drawn from both sides.
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

    // Each shape's profile from its foot to its top, as (radius, height) fractions, turned round its axis.
    static readonly Dictionary<string, Vector2[]> Profiles = new()
    {
        ["cone"] = new[] { new Vector2(1f, 0f), new Vector2(0.55f, 0.6f), new Vector2(0.12f, 1f), new Vector2(0f, 1f) },
        // A volcano's cone as its stamp stands (the nine's Old Grumbler sampled: 0.2 of its rise at 0.86 of its radius, 0.55
        // at 0.67, 0.8 at 0.48, its rim at 0.27), concave up to a narrow rim round a crater a fifth of its rise deep.
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

    /// <summary>The shapes a horizon.json row may name; any other is drawn as a keep.</summary>
    public static IEnumerable<string> Shapes => Profiles.Keys;

    /// <summary>A far landmark's colour by its type and shape.</summary>
    public static Color LandmarkColour(string type, string shape) => type switch
    {
        // Basalt and ash, cool so the haze greys it blue far off (a warm brown went ochre in the sun).
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

    /// <summary>
    /// A far landmark's stand-in: its shape's profile turned round its axis (four sides for a keep), <paramref name="radius"/>
    /// across and <paramref name="height"/> high from its foot at the origin, flat-shaded, and <paramref name="sink"/> metres
    /// more below the foot so it never floats on a slope. With an <paramref name="apron"/> (a multiple of the radius, more
    /// than 1) the land it stands on goes on out from its foot to that radius and down <paramref name="drop"/> metres below
    /// it (to the water), so past the known ring, where no land is drawn, it stands on a swell of land, not on the sky.
    /// </summary>
    public static Data Landmark(string type, string shape, float radius, float height, float sink, float apron = 0f, float drop = 0f)
    {
        if (shape == null || !Profiles.TryGetValue(shape, out var profile)) profile = Profiles[shape = "keep"];
        int sides = shape == "keep" ? 4 : 16;
        float turn = shape == "keep" ? Mathf.PI / 4f : 0f;
        // A keep's corners stand at the radius, so its sides are a little inside it.
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
                // Seen from outside: the ring's lower edge, then up; a ring that closes to a point gives one triangle.
                if (hi.x > 0f) d.Flat(a, e, f, c);
                d.Flat(a, f, b, c);
            }
        }
        if (apron > 1f)
        {
            // From the foot's ring out and down: seen from outside, as the rows above.
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

    /// <summary>
    /// How a far landmark's stand-in is drawn past the known ring, from its world/horizon.json row's "far" (view only; the
    /// core's HorizonTable doesn't read it): how much taller it stands where it's full far (1 where it gives way to the
    /// land), how deep a notch its rim shows the eye (a fraction of its height), and its apron (a multiple of its radius).
    /// </summary>
    public readonly struct FarLook
    {
        public readonly float Stretch, Notch, Apron;

        public FarLook(float stretch, float notch, float apron)
        {
            Stretch = stretch;
            Notch = notch;
            Apron = apron;
        }

        /// <summary>Drawn as its row says, nothing more.</summary>
        public static readonly FarLook Plain = new(1f, 0f, 0f);
    }

    /// <summary>Each horizon.json type's FarLook; a type with no "far" isn't listed (it's Plain).</summary>
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

    /// <summary>
    /// The sea plane: a frame at <paramref name="level"/> round the rectangle (x0, z0)-(x1, z1), out <paramref name="reach"/>
    /// metres past it on every side, so the sky never shows below the horizon past the coast.
    /// </summary>
    public static Data SeaFrame(float x0, float z0, float x1, float z1, float reach, float level, Color colour)
    {
        var d = new Data();
        Color32 c = colour;
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
        // North-west, north-east, south-east, south-west, clockwise from above: each side a band from the outer edge in.
        for (int s = 0; s < 4; s++)
        {
            int t = (s + 1) % 4;
            d.Quad(outer[s], outer[t], inner[s], inner[t]);
        }
        return d;
    }
}
