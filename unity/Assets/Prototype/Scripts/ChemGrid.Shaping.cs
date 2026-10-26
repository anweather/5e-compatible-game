using System.Collections.Generic;
using Chem;
using UnityEngine;

// Earth shaping on the terrain: when a cell's elevation changes (a terraform
// spell: a trench, a wall, a hill; a bridge burning through into a pit), the
// valley eases the chunk terrain under it to its new height (CellHeightfield,
// at stepHeight per shapeSeconds) with a puff of dust. The cell's dressing
// and blocker ride with its ground, and whatever stands on rising ground rides
// up with it (crates, barrels, the wizard) instead of being pushed through it;
// whatever stands on sinking ground is woken so it falls in after it.
public partial class ChemGrid
{
    [Tooltip("Optional: the material of the dust thrown up where the ground rises or sinks (the Particle Pack's). A plain lit material stands in.")]
    public Material dustMaterial;
    [Tooltip("Seconds the ground takes to move one step (stepHeight) when reshaped.")]
    public float shapeSeconds = 0.5f;

    sealed class Shaping
    {
        public View view;
        public int x, y;
        public Vector3 center;
        public float shown;
    }

    readonly List<Shaping> shaping = new();
    readonly Collider[] shapeHits = new Collider[32];
    readonly Dictionary<Rigidbody, float> riders = new();
    Material plainDust;

    /// <summary>True while any reshaped ground is still easing to a new height.</summary>
    public bool IsShaping => shaping.Count > 0;

    // The ground drawn at a cell now: the terrain's, which lags its target while it eases.
    float ShownGround(int x, int y, Cell c) => valley != null ? valley.ShownGround(x, y) : GroundTop(x, y, c);

    // A raised wall built of rocks over flat terrain (ValleyChemistry.FlatUnderWalls): its top
    // eases on its own, at the terrain's pace, since the terrain under it doesn't move.
    bool StandsAlone(Cell c) => valley != null && valley.FlatUnderWalls && c != null && World.IsRaisedWall(c);

    // A cell's ground starts moving from where it is drawn now.
    void StartShaping(View v, int x, int y, float shown)
    {
        shaping.RemoveAll(s => s.view == v);
        var center = CellCenter(x, y);
        shaping.Add(new Shaping { view = v, x = x, y = y, center = center, shown = shown });
        Dust(new Vector3(center.x, Mathf.Max(shown, GroundTop(x, y, World.At(x, y))), center.z));
    }

    // Each frame: the valley eases its terrain, and each reshaped cell's dressing, blocker and
    // riders follow the ground drawn at its centre, until it stops moving.
    void AnimateShaping()
    {
        if (valley != null) valley.AdvanceShaping(Time.deltaTime);
        for (int i = shaping.Count - 1; i >= 0; i--)
        {
            var s = shaping[i];
            var c = World.At(s.x, s.y);
            float now = StandsAlone(c)
                ? Mathf.MoveTowards(s.shown, GroundTop(s.x, s.y, c), stepHeight / Mathf.Max(0.05f, shapeSeconds) * Time.deltaTime)
                : ShownGround(s.x, s.y, c);
            if (s.view.dressing != null) SetY(s.view.dressing, now);
            blockers.Move(s.x, s.y, now);
            Carry(s.center, s.shown, now);
            if (Mathf.Approximately(now, s.shown)) shaping.RemoveAt(i);
            s.shown = now;
        }
    }

    static void SetY(GameObject go, float y)
    {
        var p = go.transform.position;
        go.transform.position = new Vector3(p.x, y, p.z);
    }

    // What stands on a cell whose ground moved from `was` to `now` this frame: bodies resting on
    // rising ground go up with it; anything over sinking ground is woken so it drops.
    void Carry(Vector3 center, float was, float now)
    {
        float lo = Mathf.Min(was, now) - 0.3f, hi = Mathf.Max(was, now) + 2.5f;
        var half = new Vector3(cellSize * 0.45f, (hi - lo) / 2f, cellSize * 0.45f);
        int n = Physics.OverlapBoxNonAlloc(new Vector3(center.x, (lo + hi) / 2f, center.z), half, shapeHits, Quaternion.identity, ~0, QueryTriggerInteraction.Ignore);
        riders.Clear();
        for (int i = 0; i < n; i++)
        {
            var hit = shapeHits[i];
            if (hit is CharacterController walker) { LiftWalker(walker, now); continue; }
            var body = hit.attachedRigidbody;
            if (body == null || body.isKinematic) continue;
            float bottom = hit.bounds.min.y;
            riders[body] = riders.TryGetValue(body, out var low) ? Mathf.Min(low, bottom) : bottom;
        }
        foreach (var (body, bottom) in riders)
        {
            body.WakeUp();
            if (now <= was || bottom > was + 0.15f) continue;
            body.position += Vector3.up * (now - was);
            var velocity = body.linearVelocity;
            if (velocity.y < 0f) body.linearVelocity = new Vector3(velocity.x, 0f, velocity.z);
        }
    }

    // A CharacterController isn't pushed by a moving collider, so lift it onto the rising ground.
    static void LiftWalker(CharacterController walker, float ground)
    {
        var t = walker.transform;
        if (t.position.y >= ground + 0.01f || t.position.y < ground - 1f) return;
        walker.enabled = false;
        t.position = new Vector3(t.position.x, ground + 0.02f, t.position.z);
        walker.enabled = true;
    }

    // --- dust ------------------------------------------------------------------------

    void Dust(Vector3 at) => Destroy(DustBurst(at), 2.5f);

    // A short burst of earth-coloured dust over the cell, rising a little and settling back.
    GameObject DustBurst(Vector3 at)
    {
        var go = new GameObject("Dust");
        go.transform.SetParent(transform, false);
        go.transform.position = at;
        var ps = go.AddComponent<ParticleSystem>();
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        var main = ps.main;
        main.duration = 0.3f;
        main.loop = false;
        main.startLifetime = new ParticleSystem.MinMaxCurve(0.7f, 1.4f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(0.6f, 1.8f);
        main.startSize = dustMaterial != null ? new ParticleSystem.MinMaxCurve(0.6f, 1.3f) : new ParticleSystem.MinMaxCurve(0.1f, 0.25f);
        main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
        main.startColor = new ParticleSystem.MinMaxGradient(new Color(0.50f, 0.40f, 0.29f, 1f), new Color(0.66f, 0.58f, 0.47f, 0.85f));
        main.gravityModifier = 0.25f;
        var emission = ps.emission;
        emission.rateOverTime = 0f;
        emission.SetBursts(new[] { new ParticleSystem.Burst(0f, (short)(dustMaterial != null ? 12 : 20)) });
        var fade = ps.colorOverLifetime;
        fade.enabled = true;
        fade.color = new ParticleSystem.MinMaxGradient(new Gradient
        {
            colorKeys = new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
            alphaKeys = new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0f, 1f) },
        });
        var shape = ps.shape;
        shape.shapeType = ParticleSystemShapeType.Box;
        shape.scale = new Vector3(cellSize, 0.1f, cellSize);
        if (dustMaterial == null && plainDust == null) plainDust = UrpMaterials.NewLit("Dust", new Color(0.55f, 0.46f, 0.35f));
        go.GetComponent<ParticleSystemRenderer>().sharedMaterial = dustMaterial != null ? dustMaterial : plainDust;
        ps.Play();
        return go;
    }
}
