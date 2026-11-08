using System.Collections.Generic;
using Chem;
using UnityEngine;

// The keep's crystal at the Hold's heart (docs/design/the-hold.md; hold-batch 3): what the raiders strike, so the keep's health
// has something to see. A tall six-sided crystal with shards at its foot, glowing and lit; every blow on the heart flashes it,
// and as the keep's health falls it cracks in stages (a quarter gone, a half, three quarters), each with a crack, a few shards
// and a sound, dimming and reddening; at 0 it shatters, and a new run brings it back whole. Drawn from code: no art. Only the
// show: the core's World.Hold.Health decides everything (HoldShow makes it).
public class HoldCrystal : MonoBehaviour
{
    public const int Stages = 3;
    const float Height = 7f, Waist = 0.34f, Radius = 1.5f, CrackWidth = 0.13f, FlashSeconds = 0.25f;
    static readonly Color Whole = new(0.45f, 0.8f, 1f), Hurt = new(1f, 0.25f, 0.35f);

    World world;
    int maxHealth, lastHealth = -1;
    readonly List<GameObject> cracks = new();
    GameObject body;
    Material glass;
    Light glow;
    float flashAt = -10f;

    /// <summary>How many crack stages show (0 whole, Stages at the last), and whether it has shattered: for the probe.</summary>
    public int Stage { get; private set; }
    public bool Shattered { get; private set; }
    /// <summary>Blows seen on it since it was made: for the probe.</summary>
    public int Blows { get; private set; }
    /// <summary>Its light's intensity and its glow's strength now: for the probe.</summary>
    public float LightNow => glow != null ? glow.intensity : 0f;
    public Color GlowNow => glass != null ? glass.GetColor("_EmissionColor") : Color.black;

    /// <summary>Makes the crystal on the heart's cell.</summary>
    public static HoldCrystal At(Transform parent, Vector3 ground)
    {
        var go = new GameObject("Hold crystal");
        go.transform.SetParent(parent, true);
        go.transform.position = ground;
        return go.AddComponent<HoldCrystal>();
    }

    void Awake()
    {
        body = new GameObject("Crystal");
        body.transform.SetParent(transform, false);
        glass = UrpMaterials.NewLit("Hold crystal", Whole, 0.92f, Whole * 0.6f);
        Shape(body.transform, Vector3.zero, Quaternion.identity, 1f);
        var random = new System.Random(7);
        for (int i = 0; i < 3; i++)
        {
            float a = i * 2.1f + 0.4f;
            var at = Radius * 1.1f * new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
            var tilt = Quaternion.AngleAxis(25f + i * 6f, new Vector3(-Mathf.Sin(a), 0f, Mathf.Cos(a)));
            Shape(body.transform, at, tilt, 0.35f + 0.05f * (float)random.NextDouble());
        }
        var dark = UrpMaterials.NewLit("Crystal cracks", new Color(0.05f, 0.04f, 0.08f), 0.2f);
        for (int stage = 0; stage < Stages; stage++)
        {
            var crack = new GameObject($"Cracks {stage + 1}");
            crack.transform.SetParent(body.transform, false);
            crack.AddComponent<MeshFilter>().sharedMesh = CrackMesh(new System.Random(31 + stage), 3 + stage);
            crack.AddComponent<MeshRenderer>().sharedMaterial = dark;
            crack.SetActive(false);
            cracks.Add(crack);
        }
        var light = new GameObject("Crystal light");
        light.transform.SetParent(transform, false);
        light.transform.localPosition = Height * 0.6f * Vector3.up;
        glow = light.AddComponent<Light>();
        glow.type = LightType.Point;
        glow.range = 10f;
        glow.color = Whole;
        glow.intensity = 3f;
    }

    /// <summary>Follows the run: a fresh one (or health coming back) whole, each blow a flash, each stage a crack, 0 a shatter.</summary>
    public void Follow(World w)
    {
        world = w;
        var h = w.Hold;
        int health = h == null ? w.Rules.Sim.Hold.Health : Mathf.Max(0, h.Health);
        if (h == null || lastHealth < 0 || (w.HoldRunning && health > lastHealth))
        {
            maxHealth = Mathf.Max(health, w.Rules.Sim.Hold.Health);
            Mend();
        }
        else if (health < lastHealth)
        {
            Blows++;
            flashAt = Time.time;
        }
        lastHealth = health;
        if (Shattered) return;
        if (h != null && health <= 0)
        {
            Shatter();
            return;
        }
        float share = maxHealth > 0 ? health / (float)maxHealth : 1f;
        int stage = Mathf.Clamp(Mathf.FloorToInt((1f - share) * (Stages + 1)), 0, Stages);
        while (Stage < stage) Crack();
        Shine(share);
    }

    void Mend()
    {
        Shattered = false;
        Stage = 0;
        body.SetActive(true);
        glow.enabled = true;
        foreach (var c in cracks) c.SetActive(false);
    }

    void Crack()
    {
        cracks[Stage].SetActive(true);
        Stage++;
        var at = transform.position + Height * 0.5f * Vector3.up;
        ShardBurst.At(at, Whole, 10 + 6 * Stage);
        GameSounds.Play(SoundIds.Break, at, 0.9f);
        flashAt = Time.time;
    }

    void Shatter()
    {
        Shattered = true;
        Stage = Stages;
        var at = transform.position + Height * 0.45f * Vector3.up;
        ShardBurst.At(at, Whole, 70);
        GameSounds.Play(SoundIds.Shatter, at, 1.2f);
        body.SetActive(false);
        glow.enabled = false;
    }

    // Brighter whole, dimmer and redder the more it's hurt, a stutter in its light from the second crack, and a flash on a blow.
    void Shine(float share)
    {
        float flash = Mathf.Clamp01(1f - (Time.time - flashAt) / FlashSeconds);
        var colour = Color.Lerp(Hurt, Whole, share);
        float stutter = Stage >= 2 ? 0.75f + 0.25f * Mathf.PerlinNoise(Time.time * 9f, 0.3f) : 1f;
        glass.SetColor("_EmissionColor", colour * (0.15f + 0.45f * share + 0.8f * flash) * stutter);
        glass.SetColor("_BaseColor", Color.Lerp(colour, Color.white, 0.2f));
        glow.color = colour;
        glow.intensity = (0.8f + 2.2f * share + 2.5f * flash) * stutter;
        body.transform.localScale = Vector3.one * (1f + 0.04f * flash);
    }

    void Update()
    {
        if (world != null) Follow(world);
    }

    // A flat-shaded six-sided crystal: a short point below the waist, a long one above, so it stands like a spire.
    void Shape(Transform parent, Vector3 at, Quaternion turn, float scale)
    {
        var go = new GameObject("Crystal part");
        go.transform.SetParent(parent, false);
        go.transform.SetLocalPositionAndRotation(at, turn);
        go.transform.localScale = Vector3.one * scale;
        var mesh = new Mesh { name = "Crystal" };
        var vertices = new List<Vector3>();
        var triangles = new List<int>();
        var top = new Vector3(0f, Height, 0f);
        var bottom = new Vector3(0f, -0.3f, 0f);
        for (int i = 0; i < 6; i++)
        {
            Vector3 a = Ring(i), b = Ring(i + 1);
            Face(vertices, triangles, a, top, b);
            Face(vertices, triangles, b, bottom, a);
        }
        mesh.SetVertices(vertices);
        mesh.SetTriangles(triangles, 0);
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        go.AddComponent<MeshFilter>().sharedMesh = mesh;
        go.AddComponent<MeshRenderer>().sharedMaterial = glass;
    }

    static Vector3 Ring(int i)
    {
        float a = i * Mathf.PI / 3f;
        return new Vector3(Mathf.Cos(a) * Radius, Height * Waist, Mathf.Sin(a) * Radius);
    }

    // A triangle with its own corners, so it shades flat.
    static void Face(List<Vector3> v, List<int> t, Vector3 first, Vector3 second, Vector3 third)
    {
        int n = v.Count;
        v.Add(first);
        v.Add(second);
        v.Add(third);
        t.Add(n);
        t.Add(n + 1);
        t.Add(n + 2);
    }

    // Jagged dark lines on the upper faces, a little proud of them: each crack a zigzag from a point near the waist upward.
    static Mesh CrackMesh(System.Random random, int count)
    {
        var v = new List<Vector3>();
        var normals = new List<Vector3>();
        var t = new List<int>();
        var top = new Vector3(0f, Height, 0f);
        for (int k = 0; k < count; k++)
        {
            int face = random.Next(6);
            Vector3 a = Ring(face), b = Ring(face + 1);
            var normal = Vector3.Cross(top - a, b - a).normalized;
            if (Vector3.Dot(normal, (a + b) / 2f) < 0f) normal = -normal;
            float u = 0.25f + 0.5f * (float)random.NextDouble(), up = 0.05f;
            var at = Vector3.Lerp(Vector3.Lerp(a, b, u), top, up);
            for (int step = 0; step < 6; step++)
            {
                up = Mathf.Min(0.85f, up + 0.08f + 0.06f * (float)random.NextDouble());
                u = Mathf.Clamp(u + ((float)random.NextDouble() - 0.5f) * 0.35f, 0.08f, 0.92f);
                var next = Vector3.Lerp(Vector3.Lerp(a, b, u), top, up);
                var side = CrackWidth * (1f - step / 7f) * Vector3.Cross(next - at, normal).normalized;
                var lift = normal * 0.02f;
                // One-sided, facing out of the face, with the face's own normal: a two-sided strip on shared corners
                // gets normals that cancel to nothing, and its lighting comes out as a blinding bloom.
                int n = v.Count;
                v.Add(at + side + lift);
                v.Add(next + side + lift);
                v.Add(next - side + lift);
                v.Add(at - side + lift);
                for (int i = 0; i < 4; i++) normals.Add(normal);
                bool outward = Vector3.Dot(Vector3.Cross(next - at, -side), normal) > 0f;
                t.AddRange(outward ? new[] { n, n + 1, n + 2, n, n + 2, n + 3 } : new[] { n, n + 2, n + 1, n, n + 3, n + 2 });
                at = next;
            }
        }
        var mesh = new Mesh { name = "Crystal cracks" };
        mesh.SetVertices(v);
        mesh.SetNormals(normals);
        mesh.SetTriangles(t, 0);
        mesh.RecalculateBounds();
        return mesh;
    }
}
