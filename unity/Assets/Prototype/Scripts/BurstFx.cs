using System.Collections.Generic;
using UnityEngine;

// Short particle bursts made in code and pooled (GPU-first: a few reused
// particle systems, no models): AcidSplash, a green spray with a little
// drifting fume where Acid Splash lands (SpellCaster). Shards are
// ShardBurst's. Each kind keeps at most a handful alive; past that the
// oldest is restarted where the new one is wanted. Presentation only.
public static class BurstFx
{
    public enum Kind { AcidSplash }

    const int PerKind = 6;
    static readonly Dictionary<Kind, List<ParticleSystem>> pools = new();
    static readonly Dictionary<Kind, int> next = new();
    static Material splashMaterial;
    static Transform root;

    /// <summary>Plays a burst at a point, <paramref name="size"/> times its usual spread.</summary>
    public static void Play(Kind kind, Vector3 at, float size = 1f)
    {
        var ps = Take(kind);
        ps.transform.position = at;
        ps.transform.localScale = Vector3.one * size;
        ps.Clear(true);
        ps.Play(true);
    }

    static ParticleSystem Take(Kind kind)
    {
        if (!pools.TryGetValue(kind, out var pool)) pools[kind] = pool = new List<ParticleSystem>();
        pool.RemoveAll(p => p == null); // a scene change destroyed them
        foreach (var p in pool)
            if (!p.IsAlive(true)) return p;
        if (pool.Count < PerKind)
        {
            var made = Make(kind);
            pool.Add(made);
            return made;
        }
        int i = next.TryGetValue(kind, out int n) ? n % pool.Count : 0;
        next[kind] = i + 1;
        return pool[i];
    }

    static ParticleSystem Make(Kind kind)
    {
        if (root == null) root = new GameObject("Bursts").transform;
        var go = new GameObject(kind.ToString());
        go.transform.SetParent(root, false);
        var ps = go.AddComponent<ParticleSystem>();
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        var look = go.GetComponent<ParticleSystemRenderer>();
        look.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        Splash(ps, look);
        return ps;
    }

    // Acid: a spray of green droplets out of the bubble's burst, and a slow pale-green fume after.
    static void Splash(ParticleSystem ps, ParticleSystemRenderer look)
    {
        var main = ps.main;
        main.duration = 0.15f;
        main.loop = false;
        main.playOnAwake = false;
        main.startLifetime = new ParticleSystem.MinMaxCurve(0.45f, 1.4f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(1.5f, 5f);
        main.startSize = new ParticleSystem.MinMaxCurve(0.14f, 0.5f);
        main.startColor = new ParticleSystem.MinMaxGradient(new Color(0.55f, 1f, 0.2f, 0.95f), new Color(0.25f, 0.75f, 0.12f, 0.75f));
        main.gravityModifier = 0.9f;
        main.scalingMode = ParticleSystemScalingMode.Hierarchy;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        var emission = ps.emission;
        emission.rateOverTime = 0f;
        emission.SetBursts(new[] { new ParticleSystem.Burst(0f, (short)90), new ParticleSystem.Burst(0.1f, (short)25) });
        var shape = ps.shape;
        shape.shapeType = ParticleSystemShapeType.Hemisphere;
        shape.radius = 0.6f;
        shape.rotation = new Vector3(-90f, 0f, 0f);
        var grow = ps.sizeOverLifetime;
        grow.enabled = true;
        grow.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 0.6f, 1f, 1.6f));
        Fade(ps, 0.4f);
        if (splashMaterial == null)
        {
            splashMaterial = UrpMaterials.NewUnlitParticles("Acid splash", Color.white, additive: false);
            var soft = ProceduralTextures.SoftPuff(64);
            if (splashMaterial.HasProperty("_BaseMap")) splashMaterial.SetTexture("_BaseMap", soft);
            splashMaterial.mainTexture = soft;
        }
        look.sharedMaterial = splashMaterial;
    }

    // Full until <paramref name="holdUntil"/> of its life, then fading out.
    static void Fade(ParticleSystem ps, float holdUntil)
    {
        var fade = ps.colorOverLifetime;
        fade.enabled = true;
        fade.color = new ParticleSystem.MinMaxGradient(new Gradient
        {
            colorKeys = new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
            alphaKeys = new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, holdUntil), new GradientAlphaKey(0f, 1f) },
        });
    }
}
