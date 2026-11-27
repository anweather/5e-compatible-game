using System.Collections.Generic;
using UnityEngine;

// Water thrown up where something breaks it (Andrew's playtest #12): a splash when a wizard
// drops or walks into water, bigger the faster they come down; a little spray round them
// while they wade or swim; and mist off the surf along the coast near the camera. One
// pair of particle systems fed with Emit serves all of it, so it costs a few hundred particles at
// most (the drops fall, the mist doesn't: two systems). The water's top is UnderwaterView.WaterTop (the cells' water, the lake, the sea).
// View only: every machine splashes its own from where the wizards are. Starts itself.
public class WaterSplashes : MonoBehaviour
{
    static readonly Color Spray = new(0.88f, 0.95f, 1f, 0.75f);
    static readonly Color Mist = new(0.95f, 0.98f, 1f, 0.4f);
    const float WakeEvery = 0.12f, MistEvery = 0.25f, FindEvery = 1f;
    const float WakeSpeed = 0.8f, HardFall = 8f;
    const float MistRange = 80f, MistSpread = 35f;
    const float SurfDepth = 1.2f;

    sealed class Wader
    {
        public Transform body;
        public Vector3 last;
        public bool wet;
    }

    ChemGrid grid;
    ParticleSystem drops, mist;
    readonly List<Wader> waders = new();
    float nextFind, nextWake, nextMist;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Attach()
    {
        if (ChemGrid.Current == null || FindFirstObjectByType<WaterSplashes>() != null) return;
        new GameObject("Water splashes").AddComponent<WaterSplashes>();
    }

    void Awake()
    {
        grid = ChemGrid.Current?.Grid;
        drops = PuffParticles.Make(gameObject, "Water spray", 0.6f);
        var haze = new GameObject("Surf mist");
        haze.transform.SetParent(transform, false);
        mist = PuffParticles.Make(haze, "Surf mist", 0f);
    }

    void LateUpdate()
    {
        if (grid == null || grid.World == null || Time.deltaTime <= 0f) return;
        if (Time.time >= nextFind) FindWaders();
        bool wake = Time.time >= nextWake;
        if (wake) nextWake = Time.time + WakeEvery;
        foreach (var w in waders) if (w.body != null) Watch(w, wake);
        if (Time.time >= nextMist)
        {
            nextMist = Time.time + MistEvery;
            SurfMist();
        }
    }

    void FindWaders()
    {
        nextFind = Time.time + FindEvery;
        waders.RemoveAll(w => w.body == null);
        foreach (var caster in Caster.All)
            if (!waders.Exists(w => w.body == caster.transform))
                waders.Add(new Wader { body = caster.transform, last = caster.transform.position });
    }

    void Watch(Wader w, bool wake)
    {
        var at = w.body.position;
        var moved = (at - w.last) / Time.deltaTime;
        w.last = at;
        float top = UnderwaterView.WaterTop(grid, at);
        bool wet = at.y < top + 0.05f;
        var surface = new Vector3(at.x, top, at.z);
        if (wet && !w.wet) Splash(surface, Mathf.Clamp01(-moved.y / HardFall));
        else if (wet && wake && new Vector2(moved.x, moved.z).magnitude > WakeSpeed) Wake(surface);
        w.wet = wet;
    }

    void Splash(Vector3 at, float hard)
    {
        int count = Mathf.RoundToInt(Mathf.Lerp(24f, 70f, hard));
        for (int i = 0; i < count; i++)
        {
            var out2 = Random.insideUnitCircle.normalized * Random.Range(0.6f, 1.6f + 2f * hard);
            Emit(drops, at + new Vector3(out2.x, 0f, out2.y) * 0.15f, new Vector3(out2.x, Random.Range(2f, 3.5f + 3f * hard), out2.y),
                Random.Range(0.15f, 0.35f), Random.Range(0.7f, 1.2f), Spray);
        }
    }

    void Wake(Vector3 at)
    {
        for (int i = 0; i < 3; i++)
        {
            var out2 = Random.insideUnitCircle.normalized;
            Emit(drops, at + new Vector3(out2.x, 0f, out2.y) * 0.35f, new Vector3(out2.x * 0.6f, Random.Range(0.8f, 1.6f), out2.y * 0.6f),
                Random.Range(0.1f, 0.22f), Random.Range(0.4f, 0.7f), Spray);
        }
    }

    void SurfMist()
    {
        var sea = Ocean.Instance;
        var cam = Camera.main;
        if (sea == null || cam == null) return;
        var seaward = sea.seaward.normalized;
        var along = Vector2.Perpendicular(seaward);
        var eye = new Vector2(cam.transform.position.x, cam.transform.position.z);
        if (Mathf.Abs(Vector2.Dot(eye - sea.shoreOrigin, seaward)) > MistRange) return;
        float t = Vector2.Dot(eye - sea.shoreOrigin, along);
        for (int tries = 0, made = 0; tries < 10 && made < 4; tries++)
        {
            var p = sea.shoreOrigin + along * (t + Random.Range(-MistSpread, MistSpread)) + seaward * Random.Range(-10f, 30f);
            if (!InSurf(p, sea.level)) continue;
            made++;
            Emit(mist, new Vector3(p.x, sea.level + 0.2f, p.y), new Vector3(Random.Range(-0.3f, 0.3f), Random.Range(0.2f, 0.6f), Random.Range(-0.3f, 0.3f)),
                Random.Range(1.2f, 2.6f), Random.Range(1.8f, 3f), Mist);
        }
    }

    static bool InSurf(Vector2 p, float level)
    {
        if (!Physics.Raycast(new Vector3(p.x, level + 30f, p.y), Vector3.down, out var hit, 60f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
            return false;
        return hit.point.y < level && hit.point.y > level - SurfDepth;
    }

    static void Emit(ParticleSystem into, Vector3 at, Vector3 velocity, float size, float life, Color colour)
    {
        var p = new ParticleSystem.EmitParams
        {
            position = at,
            velocity = velocity,
            startSize = size,
            startLifetime = life,
            startColor = colour,
            applyShapeToPosition = false,
        };
        into.Emit(p, 1);
    }
}
