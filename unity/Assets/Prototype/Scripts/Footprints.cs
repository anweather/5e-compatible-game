using System.Collections.Generic;
using Chem;
using UnityEngine;
using UnityEngine.Rendering;

// Footprints in the snow: wizards and the valley's animals leave a print at
// every stride where snow lies (a snow or snowbank cover, CellPaint.IsSnowy),
// left and right in turn, laid on the ground's slope. Falling snow fills them
// in sooner; they go at once where the snow under them melts. All the prints
// are one mesh, rebuilt a few times a second, in an unlit see-through
// material tinted by vertex colour. Presentation only: each machine sees its
// own, and the chemistry never knows.
public class Footprints : MonoBehaviour
{
    /// <summary>Unlit, alpha-blended and tinted by vertex colour (ChemYardBuilder makes it).</summary>
    public Material material;

    const int Max = 2000;
    /// <summary>Animals leave prints only this close to the camera.</summary>
    const float AnimalReach = 50f;
    /// <summary>A wizard's stride, in metres; an animal's scales with its size.</summary>
    const float Stride = 0.6f;
    /// <summary>Seconds a print lasts in falling snow, and once the snow has stopped.</summary>
    const float LifeSnowing = 45f, LifeSettled = 300f;
    const float RebuildEvery = 0.2f;
    static readonly Color Shadow = new(0.42f, 0.5f, 0.66f, 0.7f);

    struct Print
    {
        public Vector3 position, forward, up;
        public float size, born;
        public Vector2Int cell;
        public bool wizard;
    }

    sealed class Walker
    {
        public Vector3 last;
        public bool left;
        public float seen;
    }

    readonly Print[] prints = new Print[Max];
    readonly Vector3[] vertices = new Vector3[Max * 4];
    readonly Color[] colors = new Color[Max * 4];
    readonly Dictionary<int, Walker> wizards = new(), animals = new();
    readonly List<Transform> wizardBodies = new();
    readonly List<int> stale = new();
    int next, count;
    Mesh mesh;
    Material drawn;
    ChemGrid grid;
    ValleyFauna fauna;
    float nextRebuild, nextLook;

    /// <summary>Prints on the ground now, and how many of them are wizards', for the probe.</summary>
    public int Count => count;
    public int WizardPrints { get; private set; }

    void Start()
    {
        grid = FindFirstObjectByType<ChemGrid>();
        fauna = FindFirstObjectByType<ValleyFauna>();
        for (int i = 0; i < Max; i++) prints[i].born = float.NegativeInfinity;
        mesh = new Mesh { name = "Footprints" };
        mesh.MarkDynamic();
        mesh.SetVertices(vertices);
        mesh.SetColors(colors);
        var uv = new Vector2[Max * 4];
        var tris = new int[Max * 6];
        for (int i = 0; i < Max; i++)
        {
            uv[i * 4] = new Vector2(0f, 0f);
            uv[i * 4 + 1] = new Vector2(1f, 0f);
            uv[i * 4 + 2] = new Vector2(1f, 1f);
            uv[i * 4 + 3] = new Vector2(0f, 1f);
            int v = i * 4, t = i * 6;
            tris[t] = v; tris[t + 1] = v + 2; tris[t + 2] = v + 1;
            tris[t + 3] = v; tris[t + 4] = v + 3; tris[t + 5] = v + 2;
        }
        mesh.SetUVs(0, uv);
        mesh.SetTriangles(tris, 0);
        mesh.bounds = new Bounds(Vector3.zero, Vector3.one * 100000f);
        if (material != null)
        {
            drawn = new Material(material) { name = "Footprints" };
            drawn.SetTexture("_BaseMap", ProceduralTextures.Footprint(64));
        }
    }

    void OnDestroy()
    {
        if (mesh != null) Destroy(mesh);
        if (drawn != null)
        {
            Destroy(drawn.GetTexture("_BaseMap"));
            Destroy(drawn);
        }
    }

    void Update()
    {
        var world = grid != null ? grid.World : null;
        if (world == null || drawn == null) return;
        if (Time.time >= nextLook)
        {
            nextLook = Time.time + 2f;
            wizardBodies.Clear();
            foreach (var w in FindObjectsByType<SpellCaster>(FindObjectsSortMode.None)) wizardBodies.Add(w.transform);
        }
        foreach (var body in wizardBodies)
            if (body != null) Walk(world, wizards, body.GetInstanceID(), body.position, body.forward, 1f);
        WalkAnimals(world);
        Forget(animals);
        if (Time.time >= nextRebuild)
        {
            nextRebuild = Time.time + RebuildEvery;
            Rebuild(world);
        }
        if (count > 0)
            Graphics.RenderMesh(new RenderParams(drawn) { shadowCastingMode = ShadowCastingMode.Off, receiveShadows = false }, mesh, 0, Matrix4x4.identity);
    }

    // The animals moving near the camera.
    void WalkAnimals(World world)
    {
        var cam = Camera.main;
        if (fauna == null || cam == null) return;
        var eye = cam.transform.position;
        for (int i = 0; i < fauna.Count; i++)
        {
            var s = fauna.State(i);
            var at = new Vector3(s.Position.x, s.Position.y, s.Position.z);
            if (s.Speed < 0.1f || (at - eye).sqrMagnitude > AnimalReach * AnimalReach) continue;
            var forward = new Vector3(Mathf.Sin(s.Yaw), 0f, Mathf.Cos(s.Yaw));
            Walk(world, animals, fauna.Id(i), at, forward, 0.5f * s.Scale);
        }
    }

    // A print each stride on snowy ground, left and right in turn.
    void Walk(World world, Dictionary<int, Walker> walkers, int key, Vector3 at, Vector3 forward, float size)
    {
        if (!walkers.TryGetValue(key, out var w))
        {
            walkers[key] = new Walker { last = at, seen = Time.time };
            return;
        }
        w.seen = Time.time;
        var step = at - w.last;
        step.y = 0f;
        if (step.magnitude < Stride * size) return;
        w.last = at;
        var cell = grid.CellAt(at);
        if (!CellPaint.IsSnowy(world.At(cell.x, cell.y))) return;
        w.left = !w.left;
        forward.y = 0f;
        forward = forward.sqrMagnitude > 1e-4f ? forward.normalized : step.normalized;
        var side = Vector3.Cross(Vector3.up, forward) * ((w.left ? -0.12f : 0.12f) * size);
        var p = at + side;
        float y = grid.GroundAt(p);
        // The slope under the print, from the ground a little either side.
        float dx = grid.GroundAt(p + Vector3.right * 0.3f) - grid.GroundAt(p - Vector3.right * 0.3f);
        float dz = grid.GroundAt(p + Vector3.forward * 0.3f) - grid.GroundAt(p - Vector3.forward * 0.3f);
        var up = new Vector3(-dx / 0.6f, 1f, -dz / 0.6f).normalized;
        prints[next] = new Print
        {
            position = new Vector3(p.x, y + 0.02f, p.z),
            forward = Vector3.ProjectOnPlane(forward, up).normalized,
            up = up,
            size = size,
            born = Time.time,
            cell = cell,
            wizard = walkers == wizards,
        };
        next = (next + 1) % Max;
        count++;
    }

    // Animals despawn; drop the ones not seen for a while.
    void Forget(Dictionary<int, Walker> walkers)
    {
        stale.Clear();
        foreach (var kv in walkers)
            if (Time.time - kv.Value.seen > 5f) stale.Add(kv.Key);
        foreach (int k in stale) walkers.Remove(k);
    }

    // Every print's quad, fading toward the end of its life; gone where its snow has melted.
    void Rebuild(World world)
    {
        bool snowing = world.Weather.Snow > 0;
        float life = snowing ? LifeSnowing : LifeSettled;
        int live = 0, byWizards = 0;
        for (int i = 0; i < Max; i++)
        {
            var pr = prints[i];
            float age = Time.time - pr.born;
            bool shown = age < life && CellPaint.IsSnowy(world.At(pr.cell.x, pr.cell.y));
            int v = i * 4;
            if (!shown)
            {
                for (int k = 0; k < 4; k++) vertices[v + k] = Vector3.zero;
                continue;
            }
            live++;
            if (pr.wizard) byWizards++;
            var right = Vector3.Cross(pr.up, pr.forward) * (0.1f * pr.size);
            var ahead = pr.forward * (0.16f * pr.size);
            vertices[v] = pr.position - right - ahead;
            vertices[v + 1] = pr.position + right - ahead;
            vertices[v + 2] = pr.position + right + ahead;
            vertices[v + 3] = pr.position - right + ahead;
            var c = Shadow;
            c.a *= Mathf.Clamp01((life - age) / (life * 0.3f));
            for (int k = 0; k < 4; k++) colors[v + k] = c;
        }
        mesh.SetVertices(vertices);
        mesh.SetColors(colors);
        count = live;
        WizardPrints = byWizards;
    }
}
