using System.Collections.Generic;
using UnityEngine;

// Earth shaping in the yard: when a cell's Height changes (a terraform spell:
// a trench, a wall, a hill), its rebuilt tile starts where the old one stood
// and eases to its new height over half a second, throwing up a puff of dust.
// Whatever stands on a rising tile rides up with it (crates, barrels, the
// wizard) instead of being pushed through it, and whatever stands on a
// sinking one is woken so it falls in after it.
public partial class ChemGrid
{
    [Tooltip("Optional: the material of the dust thrown up where the ground rises or sinks (the Particle Pack's). A plain lit material stands in.")]
    public Material dustMaterial;
    [Tooltip("Seconds a reshaped cell takes to reach its new height.")]
    public float shapeSeconds = 0.5f;

    sealed class Shaping
    {
        public View view;
        public Vector3 center;
        public float from, to, start;
    }

    readonly List<Shaping> shaping = new();
    readonly Collider[] shapeHits = new Collider[32];
    readonly Dictionary<Rigidbody, float> riders = new();
    Material plainDust;

    /// <summary>True while any yard tile is still easing to a new height.</summary>
    public bool IsShaping => shaping.Count > 0;

    // The view's new tile sits at its new height; move it back to where the old one stood and ease it.
    void StartShaping(View v, float from, Vector3 center)
    {
        float to = v.tile.transform.position.y;
        shaping.RemoveAll(s => s.view == v);
        if (Mathf.Approximately(from, to)) return;
        SetTileY(v.tile, from);
        shaping.Add(new Shaping { view = v, center = center, from = from, to = to, start = Time.time });
        Dust(new Vector3(center.x, Mathf.Max(from, to), center.z));
    }

    void AnimateShaping()
    {
        for (int i = shaping.Count - 1; i >= 0; i--)
        {
            var s = shaping[i];
            if (s.view.tile == null) { shaping.RemoveAt(i); continue; }
            float k = shapeSeconds > 0f ? Mathf.Clamp01((Time.time - s.start) / shapeSeconds) : 1f;
            float was = s.view.tile.transform.position.y;
            float now = Mathf.Lerp(s.from, s.to, k * k * (3f - 2f * k));
            SetTileY(s.view.tile, now);
            Carry(s.center, was, now);
            if (k >= 1f) shaping.RemoveAt(i);
        }
    }

    static void SetTileY(GameObject tile, float y)
    {
        var p = tile.transform.position;
        tile.transform.position = new Vector3(p.x, y, p.z);
    }

    // What stands on a cell whose ground moved from `was` to `now` this frame: bodies resting on a
    // rising tile go up with it; anything over a sinking one is woken so it drops.
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
