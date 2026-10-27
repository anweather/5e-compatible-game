using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

// The coast's small life (docs/WORLD_LIFE.md, the first life layers): gulls
// wheeling over the beach, crabs scuttling along the waterline and shoals of
// fish in the shallows and the lake, now and then one jumping. Gulls climb away
// from a wizard; crabs bolt sideways and burrow into the sand, coming back up
// later; shoals slip away from a swimmer. ValleyBuilder bakes where they live;
// they're drawn instanced (VillageLit), a few mesh frames each (CreatureMesher).
// View-only: every machine has its own, and nothing they do reaches the chemistry.
public class AmbientLife : MonoBehaviour
{
    public Material material;
    [Tooltip("Points on the coast the gulls wheel round (x, sea level, z).")]
    public Vector3[] gullAnchors = System.Array.Empty<Vector3>();
    [Tooltip("Spots on the sand by the water where crabs live (on the ground).")]
    public Vector3[] crabSpots = System.Array.Empty<Vector3>();
    [Tooltip("Where shoals swim: x, the water's surface, z, and w the water's depth there.")]
    public Vector4[] shoals = System.Array.Empty<Vector4>();
    public int gullCount = 18, fishPerShoal = 9;

    Mesh[] gullFrames, crabFrames, fishFrames;
    readonly List<Matrix4x4>[] gullBatch = { new(), new(), new() };
    readonly List<Matrix4x4>[] crabBatch = { new(), new() };
    readonly List<Matrix4x4>[] fishBatch = { new(), new() };
    Gull[] gulls;
    Crab[] crabs;
    Shoal[] schools;
    Jump jump;
    float nextJump = 3f;
    Transform wizard;
    float nextLookup;
    System.Random rng;

    struct Gull
    {
        public Vector3 anchor;
        public float radius, height, speed, angle, flapPhase, scare;
        public int dir;
    }

    struct Crab
    {
        public Vector3 home, pos, target;
        public float wait, buried, walked, yaw;
    }

    struct Shoal
    {
        public Vector3 centre;
        public Vector3 drift;
        public float surface, radius, angle, speed, depth;
    }

    struct Jump
    {
        public bool on;
        public Vector3 from, to;
        public float t;
    }

    void OnEnable()
    {
        rng = new System.Random(GetInstanceID());
        gullFrames = new[] { CreatureMesher.Gull(-1f), CreatureMesher.Gull(0f), CreatureMesher.Gull(1f) };
        crabFrames = new[] { CreatureMesher.Crab(0), CreatureMesher.Crab(1) };
        var silver = new Color(0.45f, 0.55f, 0.62f, 0f);
        fishFrames = new[] { CreatureMesher.Fish(-1f, silver), CreatureMesher.Fish(1f, silver) };
        Spawn();
    }

    void OnDisable()
    {
        foreach (var m in Frames()) if (m != null) DestroyNow(m);
    }

    IEnumerable<Mesh> Frames()
    {
        foreach (var m in gullFrames ?? System.Array.Empty<Mesh>()) yield return m;
        foreach (var m in crabFrames ?? System.Array.Empty<Mesh>()) yield return m;
        foreach (var m in fishFrames ?? System.Array.Empty<Mesh>()) yield return m;
    }

    float R(float lo, float hi) => lo + (float)rng.NextDouble() * (hi - lo);

    void Spawn()
    {
        gulls = new Gull[gullAnchors.Length == 0 ? 0 : gullCount];
        for (int i = 0; i < gulls.Length; i++)
            gulls[i] = new Gull
            {
                anchor = gullAnchors[i % gullAnchors.Length] + new Vector3(R(-15f, 15f), 0f, R(-15f, 15f)),
                radius = R(8f, 26f), height = R(7f, 22f), speed = R(6f, 9f), angle = R(0f, 6.28f),
                flapPhase = R(0f, 6.28f), dir = rng.Next(2) == 0 ? 1 : -1,
            };
        crabs = new Crab[crabSpots.Length];
        for (int i = 0; i < crabs.Length; i++)
            crabs[i] = new Crab { home = crabSpots[i], pos = crabSpots[i], target = crabSpots[i], wait = R(0f, 3f), yaw = R(0f, 360f) };
        schools = new Shoal[shoals.Length];
        for (int i = 0; i < schools.Length; i++)
        {
            var s = shoals[i];
            schools[i] = new Shoal
            {
                centre = new Vector3(s.x, 0f, s.z), surface = s.y, depth = Mathf.Min(s.w * 0.5f, 1.2f) + 0.3f,
                radius = R(1.5f, 3.5f), angle = R(0f, 6.28f), speed = R(0.4f, 0.9f) * (rng.Next(2) == 0 ? 1f : -1f),
            };
        }
    }

    void Update()
    {
        if (Time.unscaledTime >= nextLookup)
        {
            var go = GameObject.Find(WizardRig.Name);
            wizard = go != null ? go.transform : null;
            nextLookup = Time.unscaledTime + 1f;
        }
        float dt = Mathf.Min(Time.deltaTime, 0.1f);
        foreach (var b in gullBatch) b.Clear();
        foreach (var b in crabBatch) b.Clear();
        foreach (var b in fishBatch) b.Clear();
        UpdateGulls(dt);
        UpdateCrabs(dt);
        UpdateFish(dt);
        Draw(gullFrames, gullBatch);
        Draw(crabFrames, crabBatch);
        Draw(fishFrames, fishBatch);
    }

    // Round and round their anchor, banking into the turn, gliding with bursts of flapping; a wizard
    // under them sends them climbing.
    void UpdateGulls(float dt)
    {
        for (int i = 0; i < gulls.Length; i++)
        {
            ref var g = ref gulls[i];
            g.angle += g.dir * g.speed / g.radius * dt;
            var at = g.anchor + new Vector3(Mathf.Cos(g.angle) * g.radius, 0f, Mathf.Sin(g.angle) * g.radius);
            bool near = wizard != null && Flat(wizard.position - at).sqrMagnitude < 14f * 14f && wizard.position.y > g.anchor.y - 2f;
            g.scare = Mathf.MoveTowards(g.scare, near ? 1f : 0f, dt * (near ? 0.8f : 0.1f));
            at.y = g.anchor.y + g.height + g.scare * 14f + Mathf.Sin(g.angle * 3f + i) * 0.8f;
            var forward = new Vector3(-Mathf.Sin(g.angle), 0f, Mathf.Cos(g.angle)) * g.dir;
            // Flap for a while, then glide (wings level), each gull to its own beat; scared gulls flap hard.
            g.flapPhase += dt * (g.scare > 0.1f ? 14f : 9f);
            bool flapping = Mathf.Sin(g.flapPhase * 0.13f + i) > 0.3f || g.scare > 0.1f;
            int frame = flapping ? WingFrame(g.flapPhase) : 1;
            var turn = Quaternion.LookRotation(forward) * Quaternion.Euler(-g.scare * 15f, 0f, -g.dir * 22f);
            gullBatch[frame].Add(Matrix4x4.TRS(at, turn, Vector3.one * 1.1f));
        }
    }

    // Wings down, level or up through a flap.
    static int WingFrame(float phase)
    {
        float s = Mathf.Sin(phase);
        if (s > 0.3f) return 2;
        return s < -0.3f ? 0 : 1;
    }

    // Sideways along the waterline between pauses; from a wizard close by, a dash away and then down
    // into the sand for a while.
    void UpdateCrabs(float dt)
    {
        for (int i = 0; i < crabs.Length; i++)
            if (StepCrab(ref crabs[i], dt))
            {
                var c = crabs[i];
                crabBatch[(int)(c.walked * 12f) % 2].Add(Matrix4x4.TRS(c.pos, Quaternion.Euler(0f, c.yaw, 0f), Vector3.one * 1.3f));
            }
    }

    // One crab's frame; false while it's down in the sand.
    bool StepCrab(ref Crab c, float dt)
    {
        if (c.buried > 0f)
        {
            c.buried -= dt;
            if (c.buried > 0f) return false;
            c.pos = c.target = c.home;
        }
        var toWizard = wizard != null ? Flat(wizard.position - c.pos) : Vector3.one * 99f;
        float speed = 0.5f;
        if (toWizard.sqrMagnitude < 1.4f * 1.4f)
        {
            c.buried = R(6f, 14f);
            return false;
        }
        if (toWizard.sqrMagnitude < 4f * 4f)
        {
            c.target = c.pos - toWizard.normalized * 2f;
            c.wait = 0f;
            speed = 2.6f;
        }
        var to = Flat(c.target - c.pos);
        if (to.sqrMagnitude < 0.01f) Pause(ref c, dt);
        else
        {
            var step = to.normalized * Mathf.Min(speed * dt, to.magnitude);
            c.pos += step;
            c.walked += step.magnitude;
            // Crabs go sideways: face across the way they're going.
            c.yaw = Mathf.MoveTowardsAngle(c.yaw, Mathf.Atan2(to.x, to.z) * Mathf.Rad2Deg + 90f, 400f * dt);
        }
        c.pos.y = Ground(c.pos, c.home.y);
        return true;
    }

    // Stopped: wait a while, then pick somewhere near home to go next.
    void Pause(ref Crab c, float dt)
    {
        c.wait -= dt;
        if (c.wait > 0f) return;
        c.target = c.home + new Vector3(R(-2.5f, 2.5f), 0f, R(-2.5f, 2.5f));
        c.wait = R(1f, 5f);
    }

    // Each shoal circles its spot under the surface, slipping away from a swimmer; one fish now and
    // then leaps clear of the water in an arc.
    void UpdateFish(float dt)
    {
        float t = Time.time;
        for (int i = 0; i < schools.Length; i++)
        {
            ref var s = ref schools[i];
            s.angle += s.speed * dt;
            if (wizard != null)
            {
                var away = Flat(s.centre + s.drift - wizard.position);
                if (away.sqrMagnitude < 5f * 5f && wizard.position.y < s.surface + 0.5f) s.drift += away.normalized * (3f * dt);
            }
            s.drift = Vector3.MoveTowards(s.drift, Vector3.zero, 0.15f * dt);
            for (int k = 0; k < fishPerShoal; k++)
            {
                float a = s.angle + k * 0.7f;
                float r = s.radius * (0.55f + 0.45f * Mathf.Sin(k * 2.3f));
                var at = s.centre + s.drift + new Vector3(Mathf.Cos(a) * r, 0f, Mathf.Sin(a) * r);
                at.y = s.surface - s.depth + 0.15f * Mathf.Sin(t * 1.3f + k);
                var forward = new Vector3(-Mathf.Sin(a), 0f, Mathf.Cos(a)) * Mathf.Sign(s.speed);
                int frame = Mathf.Sin(t * 9f + k * 1.7f) > 0f ? 0 : 1;
                fishBatch[frame].Add(Matrix4x4.TRS(at, Quaternion.LookRotation(forward), Vector3.one * 1.4f));
            }
        }
        Leap(dt);
    }

    void Leap(float dt)
    {
        if (!jump.on && schools.Length > 0 && Time.time > nextJump)
        {
            var s = schools[rng.Next(schools.Length)];
            float a = R(0f, 6.28f);
            var dir = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
            var from = s.centre + s.drift + dir * R(0f, s.radius);
            from.y = s.surface - 0.2f;
            jump = new Jump { on = true, from = from, to = from + dir * R(1.2f, 2f), t = 0f };
            nextJump = Time.time + R(2f, 6f);
        }
        if (!jump.on) return;
        jump.t += dt / 0.9f;
        if (jump.t >= 1f)
        {
            jump.on = false;
            return;
        }
        var p = Vector3.Lerp(jump.from, jump.to, jump.t) + Vector3.up * (Mathf.Sin(jump.t * Mathf.PI) * 0.9f);
        var v = (jump.to - jump.from) + Vector3.up * (Mathf.Cos(jump.t * Mathf.PI) * 2.8f);
        fishBatch[0].Add(Matrix4x4.TRS(p, Quaternion.LookRotation(v), Vector3.one * 1.6f));
    }

    void Draw(Mesh[] frames, List<Matrix4x4>[] batches)
    {
        if (material == null) return;
        var rp = new RenderParams(material) { shadowCastingMode = ShadowCastingMode.On, receiveShadows = true };
        for (int f = 0; f < frames.Length; f++)
            if (batches[f].Count > 0) Graphics.RenderMeshInstanced(rp, frames[f], 0, batches[f]);
    }

    static Vector3 Flat(Vector3 v) => new(v.x, 0f, v.z);

    // The ground under a point (the terrain there), or a fallback height.
    static float Ground(Vector3 p, float fallback)
    {
        foreach (var t in Terrain.activeTerrains)
        {
            var size = t.terrainData.size;
            var local = p - t.transform.position;
            if (local.x < 0f || local.z < 0f || local.x > size.x || local.z > size.z) continue;
            return t.SampleHeight(p) + t.transform.position.y;
        }
        return fallback;
    }

    static void DestroyNow(Object o)
    {
        if (Application.isPlaying) Destroy(o);
        else DestroyImmediate(o);
    }
}
