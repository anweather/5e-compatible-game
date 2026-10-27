using System;
using UnityEngine;

// Ray of Frost's show (SpellVisuals.Entry.beam): one beam of frost shot from
// the caster's hand along the spell's line to where it ends. A bright icy
// core and a soft pale halo round it (two lines, added on top so they glow
// and bloom), frost motes drifting off along it, and a cold light at its tip.
// It shoots out to its end in ReachSeconds, holds a moment, then fades. The
// chemistry happens when it gets there (onArrive; online NetChem's tick is
// timed to match through SpellCaster's flight time). Made from code: no pack
// prefab and no collider. Presentation only. If a caster is given and is
// destroyed before the beam gets there (the solo wizard when the game goes
// online) it doesn't land, like a SpellProjectile.
public class FrostBeam : MonoBehaviour
{
    /// <summary>How long the beam takes to reach the end of its line: when the spell lands.</summary>
    public const float ReachSeconds = 0.12f;
    const float HoldSeconds = 0.4f, FadeSeconds = 0.45f, CoreWidth = 0.1f, HaloWidth = 0.42f, Wobble = 0.035f;
    const int Points = 24;

    static Material beamMaterial, moteMaterial;

    /// <summary>Where it starts: the caster's hand.</summary>
    public Vector3 From { get; private set; }
    /// <summary>Where it ends: the end of the spell's line.</summary>
    public Vector3 To { get; private set; }
    /// <summary>How far along it has shot, 0 to 1.</summary>
    public float Reached { get; private set; }

    LineRenderer core, halo;
    ParticleSystem motes;
    Light glow;
    Color tint;
    float t, seed;
    bool landed, hasCaster;
    Action onArrive;
    UnityEngine.Object caster;

    /// <summary>
    /// Shoots a beam of frost from <paramref name="from"/> to <paramref name="to"/>, tinted
    /// <paramref name="tint"/>; <paramref name="onArrive"/> runs once, when it gets there.
    /// </summary>
    public static FrostBeam Launch(Vector3 from, Vector3 to, Color tint, Action onArrive, UnityEngine.Object caster)
    {
        var go = new GameObject("Frost beam");
        go.transform.position = from;
        var beam = go.AddComponent<FrostBeam>();
        beam.From = from;
        beam.To = to;
        beam.tint = tint;
        beam.onArrive = onArrive;
        beam.caster = caster;
        beam.hasCaster = caster != null;
        beam.seed = UnityEngine.Random.value * 100f;
        beam.Build();
        beam.Draw(1f);
        return beam;
    }

    void Build()
    {
        if (beamMaterial == null) beamMaterial = UrpMaterials.NewUnlitParticles("Frost beam", new Color(1.4f, 1.7f, 2f), additive: true);
        core = Line("Beam core", Color.white, new Color(0.85f, 0.95f, 1f, 0.9f));
        halo = Line("Beam halo", new Color(tint.r, tint.g, tint.b, 0.45f), new Color(tint.r, tint.g, tint.b, 0.25f));
        halo.widthCurve = new AnimationCurve(new Keyframe(0f, 0.45f), new Keyframe(0.2f, 1f), new Keyframe(1f, 0.7f));

        glow = new GameObject("Frost light").AddComponent<Light>();
        glow.transform.SetParent(transform, false);
        glow.type = LightType.Point;
        glow.range = 5f;
        glow.color = tint;

        motes = Motes();
    }

    LineRenderer Line(string name, Color start, Color end)
    {
        var line = new GameObject(name).AddComponent<LineRenderer>();
        line.transform.SetParent(transform, false);
        line.positionCount = Points;
        line.sharedMaterial = beamMaterial;
        line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        line.numCapVertices = 4;
        line.startColor = start;
        line.endColor = end;
        return line;
    }

    // Frost motes: a scatter along the whole beam once it's there, drifting off it and fading.
    ParticleSystem Motes()
    {
        var go = new GameObject("Frost motes");
        go.transform.SetParent(transform, false);
        var along = To - From;
        go.transform.SetPositionAndRotation(From + along * 0.5f, Quaternion.LookRotation(along.sqrMagnitude > 1e-4f ? along : Vector3.forward));
        var ps = go.AddComponent<ParticleSystem>();
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        var main = ps.main;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.startLifetime = new ParticleSystem.MinMaxCurve(0.5f, 1f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(0.15f, 0.6f);
        main.startSize = new ParticleSystem.MinMaxCurve(0.04f, 0.11f);
        main.startColor = new ParticleSystem.MinMaxGradient(new Color(0.8f, 0.92f, 1f, 0.9f), Color.white);
        main.gravityModifier = 0.05f;
        main.maxParticles = 400;
        main.playOnAwake = false;
        var emission = ps.emission;
        emission.rateOverTime = 0f;
        var shape = ps.shape;
        shape.shapeType = ParticleSystemShapeType.Box;
        shape.scale = new Vector3(0.2f, 0.2f, along.magnitude);
        shape.randomDirectionAmount = 1f;
        var fade = ps.colorOverLifetime;
        fade.enabled = true;
        fade.color = new ParticleSystem.MinMaxGradient(new Gradient
        {
            colorKeys = new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
            alphaKeys = new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0f, 1f) },
        });
        var renderer = go.GetComponent<ParticleSystemRenderer>();
        renderer.sharedMaterial = MoteMaterial();
        renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        ps.Play();
        return ps;
    }

    // The weather's snowflakes when there are any (soft round flakes), else plain glowing dots.
    static Material MoteMaterial()
    {
        var weather = FindFirstObjectByType<WeatherFx>();
        if (weather != null && weather.snowMaterial != null) return weather.snowMaterial;
        if (moteMaterial == null) moteMaterial = UrpMaterials.NewUnlitParticles("Frost motes", Color.white, additive: true);
        return moteMaterial;
    }

    void Update()
    {
        t += Time.deltaTime;
        if (onArrive != null && hasCaster && caster == null) onArrive = null; // its caster went first (Unity's null check)
        Reached = Mathf.Clamp01(t / ReachSeconds);
        if (Reached >= 1f && !landed) Land();
        float fade = Mathf.Clamp01(1f - (t - ReachSeconds - HoldSeconds) / FadeSeconds);
        Draw(fade);
        if (fade > 0f) return;
        // Faded: the lines go, the motes already out finish drifting, then it goes.
        enabled = false;
        core.enabled = halo.enabled = glow.enabled = false;
        Destroy(gameObject, 1.2f);
    }

    void Land()
    {
        landed = true;
        motes.Emit(Mathf.Clamp(Mathf.RoundToInt(Vector3.Distance(From, To) * 10f), 10, 300));
        var emission = motes.emission;
        emission.rateOverTime = Vector3.Distance(From, To) * 12f;
        var land = onArrive;
        onArrive = null;
        land?.Invoke();
    }

    // The beam from the hand out to how far it has reached, shimmering a little, at <paramref name="fade"/> strength.
    void Draw(float fade)
    {
        var tip = Vector3.Lerp(From, To, Reached);
        var across = Vector3.Cross(To - From, Vector3.up);
        across = across.sqrMagnitude > 1e-4f ? across.normalized : Vector3.right;
        for (int i = 0; i < Points; i++)
        {
            float k = i / (Points - 1f);
            float shimmer = i == 0 || i == Points - 1 ? 0f : Mathf.Sin(seed + k * 23f + t * 40f) * Wobble;
            var p = Vector3.Lerp(From, tip, k) + across * shimmer + Vector3.up * (shimmer * 0.6f);
            core.SetPosition(i, p);
            halo.SetPosition(i, p);
        }
        float pulse = 1f + 0.15f * Mathf.Sin(t * 37f + seed);
        core.widthMultiplier = CoreWidth * fade * pulse;
        halo.widthMultiplier = HaloWidth * fade * (2f - pulse);
        glow.transform.position = tip;
        glow.intensity = 4f * fade * Reached;
        if (fade < 1f && motes.emission.rateOverTime.constant > 0f)
        {
            var emission = motes.emission;
            emission.rateOverTime = 0f;
        }
    }
}
