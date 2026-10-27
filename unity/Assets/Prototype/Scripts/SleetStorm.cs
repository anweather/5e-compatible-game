using System;
using UnityEngine;

// Sleet Storm's show (SpellVisuals.Entry.storm): a low dark cloud gathers
// over the spell's squares and drives sleet down on them, icy rain streaks
// and pellets of snow slanting a little downwind (or across the caster's
// view in still air), with a whirl of spray low over the ground and the hiss
// of it and the wind. It swells in, holds for the spell's time and dies away.
// The chemistry happens once the first sleet reaches the ground (onArrive,
// LandSeconds after the cast; online NetChem's tick is timed to match). In
// Punchy's precipitation shader, the weather's own rain and snow materials
// (WeatherFx), so it's lit like the sky around it. Presentation only. If a
// caster is given and is destroyed before it lands (the solo wizard when the
// game goes online) it doesn't land, like a SpellProjectile.
public class SleetStorm : MonoBehaviour
{
    /// <summary>How long the storm rages when the spell has no duration of its own.</summary>
    public const float Seconds = 7f;
    /// <summary>When the spell takes effect after the cast: the sleet has gathered and reached the ground.</summary>
    public const float LandSeconds = 1f;
    const float FadeIn = 0.8f, FadeOut = 1.6f, CloudHeight = 10f, SleetSpeed = 14f, PelletSpeed = 5f, Drift = 3f;

    static Material cloudMaterial, fallbackMaterial;

    /// <summary>The icy rain streaks.</summary>
    public ParticleSystem Sleet { get; private set; }
    /// <summary>The pellets of snow among them.</summary>
    public ParticleSystem Pellets { get; private set; }
    /// <summary>The cloud over the storm.</summary>
    public ParticleSystem Cloud { get; private set; }
    /// <summary>The ground in the middle of the spell's squares.</summary>
    public Vector3 Center { get; private set; }
    /// <summary>Half the width of the square of ground it falls on, in metres.</summary>
    public float HalfWidth { get; private set; }
    /// <summary>How hard it's falling now, 0 to 1, as it swells in and dies away.</summary>
    public float Strength { get; private set; }

    float t, seconds;
    Action onArrive;
    UnityEngine.Object caster;
    bool hasCaster;
    Vector3 drift;
    ParticleSystem spray;
    AudioSource hiss, wind;

    /// <summary>
    /// Brings a storm down on the square of ground <paramref name="halfWidth"/> metres each way of
    /// <paramref name="center"/> for <paramref name="seconds"/>, slanting across the view from
    /// <paramref name="viewer"/> unless the wind blows. <paramref name="onArrive"/> runs once, LandSeconds in.
    /// </summary>
    public static SleetStorm Launch(Vector3 center, float halfWidth, float seconds, Vector3 viewer, Action onArrive, UnityEngine.Object caster)
    {
        var go = new GameObject("Sleet Storm");
        go.transform.position = center;
        var s = go.AddComponent<SleetStorm>();
        s.Center = center;
        s.HalfWidth = halfWidth;
        s.seconds = Mathf.Max(LandSeconds + FadeOut, seconds);
        s.onArrive = onArrive;
        s.caster = caster;
        s.hasCaster = caster != null;
        s.drift = DriftOf(center, viewer) * Drift;
        s.Build();
        return s;
    }

    // Downwind if the wind blows (the world's weather), else across the viewer's line of sight.
    static Vector3 DriftOf(Vector3 center, Vector3 viewer)
    {
        var grid = FindFirstObjectByType<ChemGrid>();
        int windDir = grid != null && grid.World != null ? grid.World.WindDir : -1;
        if (windDir >= 0) return WeatherFx.Downwind(windDir);
        var ahead = center - viewer;
        ahead.y = 0f;
        return ahead.sqrMagnitude > 1e-4f ? Vector3.Cross(Vector3.up, ahead.normalized) : Vector3.right;
    }

    void Build()
    {
        var weather = FindFirstObjectByType<WeatherFx>();
        var rain = weather != null && weather.rainMaterial != null ? weather.rainMaterial : Fallback();
        var snow = weather != null && weather.snowMaterial != null ? weather.snowMaterial : Fallback();
        float width = HalfWidth * 2f;

        Cloud = Particles("Storm cloud", CloudMaterial(rain), 4f, 260);
        Cloud.transform.position = Center + Vector3.up * CloudHeight;
        Box(Cloud, new Vector3(width + 4f, 1.5f, width + 4f));
        var main = Cloud.main;
        main.startSize = new ParticleSystem.MinMaxCurve(6f, 10f);
        main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
        main.startColor = new ParticleSystem.MinMaxGradient(new Color(0.22f, 0.24f, 0.28f, 0.95f), new Color(0.34f, 0.36f, 0.4f, 0.85f));
        FadeOverLife(Cloud, 0.25f);
        Churn(Cloud, 0.8f, 0.15f);

        Sleet = Falling("Sleet", rain, SleetSpeed, width);
        main = Sleet.main;
        main.startSize = 0.03f;
        main.startColor = new Color(0.8f, 0.85f, 0.92f, 0.5f);
        var streaks = Sleet.GetComponent<ParticleSystemRenderer>();
        streaks.renderMode = ParticleSystemRenderMode.Stretch;
        streaks.velocityScale = 0.05f;
        streaks.lengthScale = 1f;
        streaks.maxParticleSize = 0.01f;

        Pellets = Falling("Ice pellets", snow, PelletSpeed, width);
        main = Pellets.main;
        main.startSize = new ParticleSystem.MinMaxCurve(0.04f, 0.09f);
        main.startColor = new ParticleSystem.MinMaxGradient(new Color(0.85f, 0.88f, 0.94f, 0.9f), new Color(1f, 1f, 1f, 0.95f));
        Churn(Pellets, 0.5f, 0.6f);

        // Spray whirling low over the ground: what makes it a blizzard rather than rain.
        spray = Particles("Spray", CloudMaterial(rain), 2.5f, 160);
        spray.transform.position = Center + Vector3.up * 0.8f;
        Box(spray, new Vector3(width, 0.6f, width));
        main = spray.main;
        main.startSize = new ParticleSystem.MinMaxCurve(2.5f, 4.5f);
        main.startColor = new Color(0.85f, 0.88f, 0.92f, 0.3f);
        FadeOverLife(spray, 0.3f);
        Churn(spray, 1.5f, 0.3f);
        Velocity(spray, drift);

        hiss = GameSounds.Loop(SoundIds.RainLoop, transform, 0f);
        wind = GameSounds.Loop(SoundIds.WindLoop, transform, 0f);
        hiss.Play();
        wind.Play();
    }

    // Sleet or pellets: falling from just under the cloud to the ground, from upwind of the
    // spell's squares by as far as the wind carries them on the way down, so they land on them.
    ParticleSystem Falling(string name, Material material, float speed, float width)
    {
        float fall = CloudHeight - 1f;
        float life = fall / speed;
        var ps = Particles(name, material, life, 6000);
        ps.transform.position = Center + Vector3.up * fall - drift * life;
        Box(ps, new Vector3(width, 0.5f, width));
        Velocity(ps, drift + Vector3.down * speed);
        return ps;
    }

    // A particle system that lives in the world, emitting nothing until Update swells it in.
    ParticleSystem Particles(string name, Material material, float lifetime, int max)
    {
        var go = new GameObject(name);
        go.transform.SetParent(transform, false);
        var ps = go.AddComponent<ParticleSystem>();
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        var main = ps.main;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.startLifetime = lifetime;
        main.startSpeed = 0f;
        main.maxParticles = max;
        main.playOnAwake = false;
        var emission = ps.emission;
        emission.rateOverTime = 0f;
        var renderer = go.GetComponent<ParticleSystemRenderer>();
        renderer.renderMode = ParticleSystemRenderMode.Billboard;
        renderer.sharedMaterial = material;
        renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        ps.Play();
        return ps;
    }

    static void Box(ParticleSystem ps, Vector3 size)
    {
        var shape = ps.shape;
        shape.shapeType = ParticleSystemShapeType.Box;
        shape.scale = size;
    }

    static void Velocity(ParticleSystem ps, Vector3 v)
    {
        var velocity = ps.velocityOverLifetime;
        velocity.enabled = true;
        velocity.space = ParticleSystemSimulationSpace.World;
        velocity.x = v.x;
        velocity.y = v.y;
        velocity.z = v.z;
    }

    // Billows and flakes tumble about as they go.
    static void Churn(ParticleSystem ps, float strength, float frequency)
    {
        var noise = ps.noise;
        noise.enabled = true;
        noise.strength = strength;
        noise.frequency = frequency;
    }

    // Soft blobs fade in and out over their lives rather than pop.
    static void FadeOverLife(ParticleSystem ps, float edge)
    {
        var fade = ps.colorOverLifetime;
        fade.enabled = true;
        fade.color = new ParticleSystem.MinMaxGradient(new Gradient
        {
            colorKeys = new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
            alphaKeys = new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, edge), new GradientAlphaKey(1f, 1f - edge), new GradientAlphaKey(0f, 1f) },
        });
    }

    // The precipitation shader's veil (very soft blobs), for the cloud and the spray.
    static Material CloudMaterial(Material rain)
    {
        if (cloudMaterial != null) return cloudMaterial;
        cloudMaterial = new Material(rain) { name = "Sleet cloud" };
        if (cloudMaterial.HasProperty("_Shape")) cloudMaterial.SetFloat("_Shape", 2f);
        return cloudMaterial;
    }

    // Without the weather's materials (a scene without WeatherFx): plain see-through particles.
    static Material Fallback()
    {
        if (fallbackMaterial == null) fallbackMaterial = UrpMaterials.NewUnlitParticles("Sleet", Color.white, additive: false);
        return fallbackMaterial;
    }

    void Update()
    {
        t += Time.deltaTime;
        if (onArrive != null && hasCaster && caster == null) onArrive = null; // its caster went first (Unity's null check)
        if (t >= LandSeconds && onArrive != null)
        {
            var land = onArrive;
            onArrive = null;
            land();
        }
        Strength = Mathf.Clamp01(Mathf.Min(t / FadeIn, (seconds - t) / FadeOut));
        Rate(Cloud, 70f, Mathf.Clamp01(Strength * 2f));
        Rate(Sleet, 2600f, Strength);
        Rate(Pellets, 700f, Strength);
        Rate(spray, 45f, Strength);
        hiss.volume = 0.5f * Strength * GameVolume.Effects;
        wind.volume = 0.35f * Strength * GameVolume.Effects;
        if (t < seconds) return;
        // Over: no more falls; what's in the air finishes and the storm goes.
        enabled = false;
        hiss.Stop();
        wind.Stop();
        Destroy(gameObject, 4.5f);
    }

    static void Rate(ParticleSystem ps, float full, float k)
    {
        var emission = ps.emission;
        emission.rateOverTime = full * k;
    }
}
