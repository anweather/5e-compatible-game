using System;
using System.Collections;
using Unity.Cinemachine;
using UnityEngine;

// A meteor's show (the Meteor spell; SpellVisuals.Entry.fromSky): a burning
// rock falls from high in the sky onto its target in a long slant, a white-hot
// trail and a smoke trail behind it and its own orange light washing over the
// land as it comes in. It lands in a blast: a flash that lights the valley and
// the sky (SkyLight.Flash), the fire and dust explosions, and a shake of the
// camera that fades with distance. The chemistry happens on landing
// (onArrive); this is only the show. If a caster is given and is destroyed in
// flight (the solo wizard when the game goes online) it fizzles, like a
// SpellProjectile.
public class MeteorFall : MonoBehaviour
{
    /// <summary>How long a meteor takes to fall (the spell lands this long after the cast).</summary>
    public const float Seconds = 1.8f;
    const float Height = 110f, Reach = 75f, RockSize = 1.6f;

    static Material hot, trail, smoke;
    static CinemachineImpulseSource shaker;

    Vector3 from, to;
    float t, seconds = Seconds;
    Action onArrive;
    UnityEngine.Object caster;
    bool hasCaster;
    SpellVisuals.Entry look;
    Light glow;
    Transform rock;

    /// <summary>
    /// Brings a meteor down on <paramref name="target"/>, coming in from beyond it as seen from
    /// <paramref name="viewer"/> (the caster), so it streaks across their view.
    /// </summary>
    public static void Launch(Vector3 target, Vector3 viewer, SpellVisuals.Entry look, Action onArrive, UnityEngine.Object caster, float seconds = Seconds)
    {
        var ahead = target - viewer;
        ahead.y = 0f;
        if (ahead.sqrMagnitude < 1f) ahead = Vector3.forward;
        // From beyond the target and off to one side, high up.
        var side = Quaternion.Euler(0f, (target.x + target.z) % 2f < 1f ? 35f : -35f, 0f) * ahead.normalized;
        var go = new GameObject("Meteor");
        var m = go.AddComponent<MeteorFall>();
        m.from = target + side * Reach + Vector3.up * Height;
        m.to = target;
        m.look = look;
        m.onArrive = onArrive;
        m.caster = caster;
        m.hasCaster = caster != null;
        m.seconds = Mathf.Max(0.2f, seconds);
        go.transform.position = m.from;
        m.Build();
    }

    void Build()
    {
        if (hot == null) hot = UrpMaterials.NewLit("Meteor rock", new Color(0.12f, 0.08f, 0.06f), 0.2f, new Color(1f, 0.45f, 0.12f) * 2.5f);
        // The trails take the particle pack's soft materials when they're there: the flames' embers
        // (a soft glowing dot, stretched along the trail) and the dust's smoke.
        if (trail == null) trail = ParticleMaterial(look?.projectile, "Embers");
        if (trail == null) trail = UrpMaterials.NewUnlitParticles("Meteor trail", Color.white, additive: true);
        if (smoke == null) smoke = ParticleMaterial(look?.afterImpact, null);
        if (smoke == null) smoke = UrpMaterials.NewUnlitParticles("Meteor smoke", new Color(0.25f, 0.22f, 0.2f, 0.6f), additive: false);

        var grid = FindAnyObjectByType<ChemGrid>();
        var stones = grid != null ? grid.wallStones : null;
        GameObject body = stones != null && stones.Length > 0 && stones[0] != null
            ? Instantiate(stones[0], transform)
            : Primitives.Visual(PrimitiveType.Sphere);
        body.transform.SetParent(transform, false);
        body.transform.localScale = RockSize * (stones != null && stones.Length > 0 ? 1.4f : 1f) * Vector3.one;
        foreach (var r in body.GetComponentsInChildren<Renderer>()) r.sharedMaterial = hot;
        rock = body.transform;

        if (look?.projectile != null)
        {
            var flames = Instantiate(look.projectile, transform);
            SpellVisuals.NoColliders(flames);
            flames.transform.localScale = Vector3.one * 3f;
        }
        Trail("Fire trail", trail, 0.9f, 2.4f, 0f, new Gradient
        {
            colorKeys = new[] { new GradientColorKey(new Color(1f, 0.95f, 0.8f), 0f), new GradientColorKey(new Color(1f, 0.5f, 0.12f), 0.35f), new GradientColorKey(new Color(0.6f, 0.12f, 0.04f), 1f) },
            alphaKeys = new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0.8f, 0.4f), new GradientAlphaKey(0f, 1f) },
        });
        Trail("Smoke trail", smoke, 3f, 1.5f, 5f, new Gradient
        {
            colorKeys = new[] { new GradientColorKey(new Color(0.35f, 0.3f, 0.28f), 0f), new GradientColorKey(new Color(0.2f, 0.19f, 0.18f), 1f) },
            alphaKeys = new[] { new GradientAlphaKey(0.7f, 0f), new GradientAlphaKey(0f, 1f) },
        });

        glow = new GameObject("Meteor light").AddComponent<Light>();
        glow.transform.SetParent(transform, false);
        glow.type = LightType.Point;
        glow.color = new Color(1f, 0.55f, 0.2f);
        glow.range = 60f;
        glow.intensity = 4f;
    }

    // A particle material from an effect prefab (the one named, or its first), or null.
    static Material ParticleMaterial(GameObject prefab, string name)
    {
        if (prefab == null) return null;
        foreach (var r in prefab.GetComponentsInChildren<ParticleSystemRenderer>(true))
            if (r.sharedMaterial != null && (name == null || r.sharedMaterial.name == name)) return r.sharedMaterial;
        return null;
    }

    void Trail(string name, Material mat, float seconds, float width, float endWidth, Gradient colors)
    {
        var tr = new GameObject(name).AddComponent<TrailRenderer>();
        tr.transform.SetParent(transform, false);
        tr.sharedMaterial = mat;
        tr.time = seconds;
        tr.minVertexDistance = 0.6f;
        tr.widthCurve = AnimationCurve.Linear(0f, width, 1f, endWidth);
        tr.colorGradient = colors;
        tr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        tr.textureMode = LineTextureMode.Stretch;
    }

    void Update()
    {
        if (hasCaster && caster == null) // its caster went in flight (Unity's null check)
        {
            Destroy(gameObject);
            return;
        }
        t += Time.deltaTime / seconds;
        float k = Mathf.Clamp01(t);
        // Falling faster as it comes.
        transform.position = Vector3.Lerp(from, to, k * (0.55f + 0.45f * k));
        rock.Rotate(new Vector3(200f, 140f, 90f) * Time.deltaTime, Space.Self);
        glow.intensity = Mathf.Lerp(4f, 30f, k * k);
        if (k < 1f) return;
        enabled = false;
        StartCoroutine(Impact());
    }

    IEnumerator Impact()
    {
        onArrive?.Invoke();
        onArrive = null;
        if (rock != null) Destroy(rock.gameObject);
        foreach (var ps in GetComponentsInChildren<ParticleSystem>()) ps.Stop(true, ParticleSystemStopBehavior.StopEmitting);
        Blast(look?.impact, 2.4f);
        Blast(look?.afterImpact, 2f);
        Shake(to);
        var cam = Camera.main;
        float distance = cam != null ? Vector3.Distance(cam.transform.position, to) : 50f;
        var sky = FindAnyObjectByType<SkyLight>();
        if (sky != null) sky.Flash(Mathf.Lerp(0.8f, 0.2f, distance / 400f));

        glow.transform.SetParent(null, true);
        glow.transform.position = to + Vector3.up * 6f;
        glow.range = 90f;
        glow.color = new Color(1f, 0.7f, 0.4f);
        for (float s = 0f; s < 1.6f; s += Time.deltaTime)
        {
            float f = 1f - s / 1.6f;
            // A white-hot flash, then the fire's glow dying down.
            glow.intensity = s < 0.08f ? 120f : 60f * f * f;
            yield return null;
        }
        Destroy(glow.gameObject);
        Destroy(gameObject, 2.5f);
    }

    void Blast(GameObject prefab, float scale)
    {
        if (prefab == null) return;
        var fx = SpellVisuals.Spawn(prefab, to, Quaternion.identity);
        fx.transform.localScale = Vector3.one * scale;
        Destroy(fx, Mathf.Max(3f, look?.impactSeconds ?? 3f));
    }

    // The camera shakes, hardest near the blast (CinemachineImpulseListener on the follow camera):
    // a short, light jolt (Andrew's playtest: the old 1.2 s one was too strong).
    static void Shake(Vector3 at)
    {
        if (shaker == null)
        {
            shaker = new GameObject("Meteor shake").AddComponent<CinemachineImpulseSource>();
            var d = shaker.ImpulseDefinition;
            d.ImpulseShape = CinemachineImpulseDefinition.ImpulseShapes.Explosion;
            d.ImpulseType = CinemachineImpulseDefinition.ImpulseTypes.Dissipating;
            d.ImpulseDuration = 0.5f;
            d.DissipationDistance = 160f;
            d.DissipationRate = 0.4f;
        }
        shaker.GenerateImpulseAtPositionWithVelocity(at, Vector3.down * 0.5f);
    }
}
