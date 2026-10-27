using UnityEngine;

// Generated props for the beach and the island (D12, docs/WORLD_LIFE.md), built
// with MeshKit from a seed: palm trees (a ringed, leaning trunk, drooping
// fronds, coconuts), glowbulbs (the island's odd plants, lit at night),
// loungers, striped umbrellas, towels, benches, driftwood and starfish. Each
// returns a mesh in its own frame: y up from the ground, and for anything you
// sit on, facing +z with the seat at SeatAt. Looks only.
public static class PropMesher
{
    static Color Rgb(float r, float g, float b) => new(r, g, b, 0f);

    static readonly Color[] Fronds = { Rgb(0.2f, 0.5f, 0.22f), Rgb(0.16f, 0.45f, 0.36f), Rgb(0.28f, 0.55f, 0.2f), Rgb(0.14f, 0.5f, 0.5f) };
    static readonly Color[] Fabrics = { Rgb(0.9f, 0.3f, 0.25f), Rgb(0.2f, 0.45f, 0.8f), Rgb(0.95f, 0.75f, 0.2f), Rgb(0.2f, 0.65f, 0.55f), Rgb(0.85f, 0.4f, 0.65f) };
    static readonly Color Cream = Rgb(0.95f, 0.92f, 0.84f);
    static readonly Color Wood = Rgb(0.55f, 0.4f, 0.26f);
    static readonly Color Bark = Rgb(0.47f, 0.37f, 0.27f);

    static float Range(System.Random rng, float lo, float hi) => lo + (float)rng.NextDouble() * (hi - lo);

    static Color Jitter(System.Random rng, Color c, float by)
    {
        float k = 1f + Range(rng, -by, by);
        return new Color(Mathf.Clamp01(c.r * k), Mathf.Clamp01(c.g * k), Mathf.Clamp01(c.b * k), c.a);
    }

    /// <summary>A palm: its mesh, and the top of its trunk (for a collider up the trunk).</summary>
    public static Mesh Palm(int seed, out Vector3 crown)
    {
        var rng = new System.Random(seed);
        var kit = new MeshKit { OcclusionHeight = 1.5f, Occlusion = 0.2f };
        float height = Range(rng, 5.5f, 9f);
        var lean = new Vector3(Range(rng, -1f, 1f), 0f, Range(rng, -1f, 1f)).normalized * Range(rng, 0.8f, 2.4f);
        const int segments = 9;
        var at = Vector3.zero;
        for (int i = 0; i < segments; i++)
        {
            float t0 = (float)i / segments, t1 = (float)(i + 1) / segments;
            // The trunk bows: its lean grows with the square of the height.
            var a = new Vector3(lean.x * t0 * t0, height * t0, lean.z * t0 * t0);
            var b = new Vector3(lean.x * t1 * t1, height * t1, lean.z * t1 * t1);
            float r = Mathf.Lerp(0.24f, 0.14f, t0);
            kit.Cylinder(a, b, r, 7, Jitter(rng, i % 2 == 0 ? Bark : Bark * 0.85f, 0.05f), false);
            kit.Cylinder(b - (b - a).normalized * 0.06f, b, r + 0.03f, 7, Bark * 0.7f, false);
            at = b;
        }
        crown = at;
        var frond = Fronds[rng.Next(Fronds.Length)];
        int count = rng.Next(7, 10);
        float turn = Range(rng, 0f, 360f);
        for (int f = 0; f < count; f++)
            Frond(kit, rng, at, turn + f * 360f / count + Range(rng, -12f, 12f), Range(rng, 2.6f, 3.6f), Jitter(rng, frond, 0.12f));
        for (int c = 0; c < rng.Next(2, 5); c++)
        {
            float a = c * 2.1f;
            kit.Box(at + new Vector3(Mathf.Cos(a) * 0.22f, -0.25f, Mathf.Sin(a) * 0.22f), Vector3.one * 0.22f, Rgb(0.38f, 0.28f, 0.12f), Quaternion.Euler(30f, a * 50f, 20f));
        }
        return kit.ToMesh("Palm");
    }

    // One frond: a spine arching out and down, with leaflets each side, both faces drawn.
    static void Frond(MeshKit kit, System.Random rng, Vector3 root, float yaw, float length, Color color)
    {
        var dir = Quaternion.Euler(0f, yaw, 0f) * Vector3.forward;
        var side = Vector3.Cross(Vector3.up, dir);
        const int steps = 7;
        float rise = Range(rng, 0.4f, 0.9f), droop = Range(rng, 1.4f, 2.4f);
        var prev = root;
        for (int i = 1; i <= steps; i++)
        {
            float t = (float)i / steps;
            var p = root + dir * (length * t) + Vector3.up * (rise * t - droop * t * t);
            float w0 = 0.55f * Mathf.Sin(Mathf.PI * ((float)(i - 1) / steps * 0.9f + 0.1f)), w1 = 0.55f * Mathf.Sin(Mathf.PI * (t * 0.9f + 0.1f));
            // Leaflets hang a little below the spine, so each frond has a V.
            var hang = Vector3.down * 0.12f;
            kit.Sheet(prev, p, p + side * w1 + hang, prev + side * w0 + hang, color);
            kit.Sheet(prev, prev - side * w0 + hang, p - side * w1 + hang, p, color * 0.92f);
            prev = p;
        }
    }

    /// <summary>A glowbulb: a curling stalk with a lit bulb, a little taller than a wizard.</summary>
    public static Mesh Glowbulb(int seed)
    {
        var rng = new System.Random(seed);
        var kit = new MeshKit();
        var stalk = Rgb(0.35f, 0.25f, 0.45f);
        var bulb = new Color(Range(rng, 0.5f, 1f), Range(rng, 0.3f, 0.6f), Range(rng, 0.7f, 1f), 1f);
        int stems = rng.Next(1, 4);
        for (int s = 0; s < stems; s++)
        {
            float yaw = Range(rng, 0f, Mathf.PI * 2f), h = Range(rng, 1.2f, 2.4f), curl = Range(rng, 0.3f, 0.8f);
            var tip = Vector3.zero;
            for (int i = 0; i < 6; i++)
            {
                float t0 = i / 6f, t1 = (i + 1) / 6f;
                Vector3 P(float t) => new(Mathf.Cos(yaw) * curl * t * t, h * t, Mathf.Sin(yaw) * curl * t * t);
                kit.Cylinder(P(t0), P(t1), Mathf.Lerp(0.07f, 0.035f, t0), 5, stalk, false);
                tip = P(t1);
            }
            float r = Range(rng, 0.18f, 0.3f);
            kit.Cylinder(tip + Vector3.down * (r * 0.2f), tip + Vector3.up * (r * 1.4f), r, 7, bulb);
            kit.Cone(tip + Vector3.up * (r * 1.4f), r, r * 0.8f, 7, bulb);
        }
        return kit.ToMesh("Glowbulb");
    }

    /// <summary>Where a seat's sitter's hips go, in the seat's frame.</summary>
    public static readonly Vector3 LoungerSeat = new(0f, 0.38f, -0.15f), BenchSeat = new(0f, 0.48f, 0f);

    /// <summary>A beach lounger: a wooden frame, a striped cloth, its back raised at the -z end.</summary>
    public static Mesh Lounger(int seed)
    {
        var rng = new System.Random(seed);
        var kit = new MeshKit();
        var fabric = Fabrics[rng.Next(Fabrics.Length)];
        // Legs and side rails.
        for (int sx = -1; sx <= 1; sx += 2)
        {
            kit.Box(new Vector3(sx * 0.3f, 0.32f, 0.2f), new Vector3(0.05f, 0.05f, 1.3f), Wood);
            for (int sz = -1; sz <= 1; sz += 2)
                kit.Box(new Vector3(sx * 0.3f, 0.16f, 0.2f + sz * 0.6f), new Vector3(0.05f, 0.32f, 0.05f), Wood);
        }
        // The flat part toward +z, in stripes across.
        for (int i = 0; i < 6; i++)
        {
            float z = -0.15f + i * 0.21f;
            kit.Box(new Vector3(0f, 0.36f, z + 0.1f), new Vector3(0.58f, 0.03f, 0.21f), i % 2 == 0 ? fabric : Cream);
        }
        // The back, tilted up at the -z end.
        var turn = Quaternion.Euler(-55f, 0f, 0f);
        for (int i = 0; i < 3; i++)
            kit.Box(new Vector3(0f, 0.36f, -0.2f) + turn * new Vector3(0f, 0f, -0.1f - i * 0.21f), new Vector3(0.58f, 0.03f, 0.21f), i % 2 == 0 ? Cream : fabric, turn);
        return kit.ToMesh("Lounger");
    }

    /// <summary>A beach umbrella: a pole and a striped canopy.</summary>
    public static Mesh Umbrella(int seed)
    {
        var rng = new System.Random(seed);
        var kit = new MeshKit();
        var fabric = Fabrics[rng.Next(Fabrics.Length)];
        float tilt = Range(rng, -8f, 8f);
        kit.Frame = Matrix4x4.Rotate(Quaternion.Euler(tilt, 0f, tilt * 0.5f));
        kit.Cylinder(new Vector3(0f, -0.3f, 0f), new Vector3(0f, 2.5f, 0f), 0.03f, 6, Cream);
        const int panels = 10;
        var top = new Vector3(0f, 2.55f, 0f);
        for (int i = 0; i < panels; i++)
        {
            float a0 = i * Mathf.PI * 2f / panels, a1 = (i + 1) * Mathf.PI * 2f / panels;
            var rim0 = new Vector3(Mathf.Cos(a0) * 1.3f, 2.15f, Mathf.Sin(a0) * 1.3f);
            var rim1 = new Vector3(Mathf.Cos(a1) * 1.3f, 2.15f, Mathf.Sin(a1) * 1.3f);
            var c = i % 2 == 0 ? fabric : Cream;
            kit.Tri(top, rim0, rim1, c, Vector3.up);
            kit.Tri(top, rim0, rim1, c * 0.7f, Vector3.down);
        }
        return kit.ToMesh("Umbrella");
    }

    /// <summary>A towel laid on the sand.</summary>
    public static Mesh Towel(int seed)
    {
        var rng = new System.Random(seed);
        var kit = new MeshKit();
        var a = Fabrics[rng.Next(Fabrics.Length)];
        for (int i = 0; i < 5; i++)
            kit.Box(new Vector3(0f, 0.01f, -0.8f + i * 0.4f), new Vector3(0.8f, 0.02f, 0.4f), i % 2 == 0 ? a : Cream);
        return kit.ToMesh("Towel");
    }

    /// <summary>A wooden bench with a back, seat along x.</summary>
    public static Mesh Bench(int seed)
    {
        var rng = new System.Random(seed);
        var kit = new MeshKit();
        var wood = Jitter(rng, Wood, 0.15f);
        for (int i = 0; i < 3; i++) kit.Box(new Vector3(0f, 0.46f, -0.15f + i * 0.15f), new Vector3(1.6f, 0.05f, 0.12f), wood);
        for (int sx = -1; sx <= 1; sx += 2)
        {
            kit.Box(new Vector3(sx * 0.7f, 0.22f, 0f), new Vector3(0.08f, 0.44f, 0.4f), wood * 0.8f);
            kit.Box(new Vector3(sx * 0.7f, 0.7f, -0.22f), new Vector3(0.08f, 0.5f, 0.06f), wood * 0.8f);
        }
        for (int i = 0; i < 2; i++) kit.Box(new Vector3(0f, 0.68f + i * 0.2f, -0.24f), new Vector3(1.6f, 0.1f, 0.04f), wood);
        return kit.ToMesh("Bench");
    }

    /// <summary>The dock's deck height above its foot, and its width (m).</summary>
    public const float DockDeck = 1.6f, DockWidth = 2.4f;

    /// <summary>
    /// A wooden dock running <paramref name="length"/> metres along +z from its foot on the shore:
    /// planks on cross beams, posts down into the water, a rail along one side and a bollard at the end.
    /// Its deck is DockDeck above the foot.
    /// </summary>
    public static Mesh Dock(int seed, float length)
    {
        var rng = new System.Random(seed);
        var kit = new MeshKit { Occlusion = 0f };
        var plank = Rgb(0.6f, 0.45f, 0.3f);
        var post = Rgb(0.36f, 0.27f, 0.2f);
        float half = DockWidth / 2f;
        for (float z = 0.15f; z < length; z += 0.32f)
            kit.Box(new Vector3(Range(rng, -0.03f, 0.03f), DockDeck - 0.04f, z), new Vector3(DockWidth, 0.07f, 0.28f), Jitter(rng, plank, 0.1f));
        for (float z = 0.4f; z < length; z += 2.5f)
        {
            kit.Box(new Vector3(0f, DockDeck - 0.16f, z), new Vector3(DockWidth + 0.2f, 0.16f, 0.16f), post);
            for (int s = -1; s <= 1; s += 2)
                kit.Cylinder(new Vector3(s * (half + 0.05f), -4f, z), new Vector3(s * (half + 0.05f), DockDeck + (s > 0 ? 0.95f : 0.1f), z), 0.12f, 6, post);
        }
        kit.Box(new Vector3(half + 0.05f, DockDeck + 0.9f, length / 2f), new Vector3(0.08f, 0.08f, length), post);
        kit.Cylinder(new Vector3(-half + 0.3f, DockDeck, length - 0.4f), new Vector3(-half + 0.3f, DockDeck + 0.45f, length - 0.4f), 0.13f, 7, Rgb(0.25f, 0.25f, 0.27f));
        return kit.ToMesh("Dock");
    }

    /// <summary>A bleached, twisting piece of driftwood lying on the sand.</summary>
    public static Mesh Driftwood(int seed)
    {
        var rng = new System.Random(seed);
        var kit = new MeshKit();
        var grey = Jitter(rng, Rgb(0.72f, 0.68f, 0.6f), 0.08f);
        var at = Vector3.zero;
        float yaw = 0f, r = Range(rng, 0.1f, 0.2f);
        for (int i = 0; i < 4; i++)
        {
            yaw += Range(rng, -30f, 30f);
            var next = at + Quaternion.Euler(0f, yaw, 0f) * (Vector3.forward * Range(rng, 0.6f, 1.1f));
            var lift = Vector3.up * (r * 0.7f);
            kit.Cylinder(at + lift, next + lift, r, 6, grey);
            at = next;
            r *= 0.85f;
        }
        return kit.ToMesh("Driftwood");
    }

    /// <summary>A starfish (or a few shells' worth of colour) on the sand.</summary>
    public static Mesh Starfish(int seed)
    {
        var rng = new System.Random(seed);
        var kit = new MeshKit();
        var c = rng.Next(3) switch { 0 => Rgb(0.95f, 0.45f, 0.25f), 1 => Rgb(0.85f, 0.35f, 0.55f), _ => Rgb(0.95f, 0.75f, 0.35f) };
        for (int i = 0; i < 5; i++)
        {
            var turn = Quaternion.Euler(0f, i * 72f, 0f);
            kit.Box(turn * new Vector3(0f, 0.025f, 0.09f), new Vector3(0.06f, 0.04f, 0.18f), c, turn);
        }
        return kit.ToMesh("Starfish");
    }
}
