using System.Collections.Generic;
using UnityEngine;

// Short particle bursts made in code and pooled (GPU-first: a few reused
// particle systems, no models): AcidSplash, a green spray with a little
// drifting fume where Acid Splash lands (SpellCaster); Flash, a quick orange
// blast where a gas goes up (ChemGrid.Whirls); Forge, sparks and a hot glow
// rising off the ground; SandBlast, a jet of sand along the cast; and Chips,
// bits of ground thrown up by a pick (Pick). Shards are ShardBurst's. Each kind
// keeps at most a handful alive; past that the oldest is restarted where the
// new one is wanted. Presentation only.
public static class BurstFx
{
    public enum Kind { AcidSplash, Flash, Forge, SandBlast, Chips }

    const int PerKind = 6;

    // One kind of burst: its spread of lifetimes, speeds, sizes and colours, how hard it falls,
    // how many at each moment, where they come from, how long it stays full, and its blend.
    sealed class Recipe
    {
        public Vector2 life, speed, size;
        public Color hot, cool;
        public float gravity, seconds, spread, hold, grow = 1f;
        public (float at, short count)[] bursts;
        public bool cone, additive;
    }

    static readonly Dictionary<Kind, Recipe> Recipes = new()
    {
        [Kind.AcidSplash] = new Recipe
        {
            life = new(0.45f, 1.4f), speed = new(1.5f, 5f), size = new(0.14f, 0.5f), gravity = 0.9f, seconds = 0.15f, spread = 0.6f, hold = 0.4f, grow = 1.6f,
            hot = new Color(0.55f, 1f, 0.2f, 0.95f), cool = new Color(0.25f, 0.75f, 0.12f, 0.75f), bursts = new[] { (0f, (short)90), (0.1f, (short)25) },
        },
        [Kind.Flash] = new Recipe
        {
            life = new(0.3f, 0.8f), speed = new(2f, 7f), size = new(0.5f, 1.5f), seconds = 0.1f, spread = 0.8f, hold = 0.45f, additive = true,
            hot = new Color(1f, 0.85f, 0.4f, 1f), cool = new Color(1f, 0.4f, 0.08f, 0.9f), bursts = new[] { (0f, (short)200) },
        },
        [Kind.Forge] = new Recipe
        {
            life = new(0.5f, 1.3f), speed = new(0.4f, 2.4f), size = new(0.06f, 0.42f), gravity = -0.25f, seconds = 0.4f, spread = 1.5f, hold = 0.25f, additive = true,
            hot = new Color(1f, 0.72f, 0.28f, 1f), cool = new Color(1f, 0.26f, 0.04f, 0.9f), bursts = new[] { (0f, (short)60), (0.15f, (short)40), (0.3f, (short)30) },
        },
        [Kind.SandBlast] = new Recipe
        {
            life = new(0.3f, 0.75f), speed = new(8f, 17f), size = new(0.14f, 0.6f), gravity = 0.25f, seconds = 0.3f, spread = 0.18f, hold = 0.45f, grow = 1.8f, cone = true,
            hot = new Color(0.86f, 0.72f, 0.46f, 0.9f), cool = new Color(0.68f, 0.52f, 0.3f, 0.7f), bursts = new[] { (0f, (short)80), (0.08f, (short)80), (0.16f, (short)80), (0.24f, (short)60) },
        },
        [Kind.Chips] = new Recipe
        {
            life = new(0.3f, 0.7f), speed = new(2f, 5.5f), size = new(0.05f, 0.16f), gravity = 1.6f, seconds = 0.05f, spread = 0.2f, hold = 0.7f,
            hot = Color.white, cool = Color.white, bursts = new[] { (0f, (short)34) },
        },
    };

    static readonly Dictionary<Kind, List<ParticleSystem>> pools = new();
    static readonly Dictionary<Kind, int> next = new();
    static Material softMaterial, glowMaterial;
    static Transform root;

    /// <summary>Plays a burst at a point, <paramref name="size"/> times its usual spread, turned to <paramref name="turn"/> if given, in <paramref name="tint"/> if given.</summary>
    public static void Play(Kind kind, Vector3 at, float size = 1f, Quaternion? turn = null, Color? tint = null)
    {
        var ps = Take(kind);
        ps.transform.SetPositionAndRotation(at, turn ?? Quaternion.identity);
        ps.transform.localScale = Vector3.one * size;
        if (tint != null)
        {
            var main = ps.main;
            main.startColor = new ParticleSystem.MinMaxGradient(tint.Value, tint.Value * 0.7f);
        }
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
        var recipe = Recipes[kind];
        Follow(ps, recipe);
        look.sharedMaterial = Soft(recipe.additive);
        return ps;
    }

    static void Follow(ParticleSystem ps, Recipe r)
    {
        var main = ps.main;
        main.duration = r.seconds;
        main.loop = false;
        main.playOnAwake = false;
        main.startLifetime = new ParticleSystem.MinMaxCurve(r.life.x, r.life.y);
        main.startSpeed = new ParticleSystem.MinMaxCurve(r.speed.x, r.speed.y);
        main.startSize = new ParticleSystem.MinMaxCurve(r.size.x, r.size.y);
        main.startColor = new ParticleSystem.MinMaxGradient(r.hot, r.cool);
        main.gravityModifier = r.gravity;
        main.scalingMode = ParticleSystemScalingMode.Hierarchy;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        var emission = ps.emission;
        emission.rateOverTime = 0f;
        var bursts = new ParticleSystem.Burst[r.bursts.Length];
        for (int i = 0; i < bursts.Length; i++) bursts[i] = new ParticleSystem.Burst(r.bursts[i].at, r.bursts[i].count);
        emission.SetBursts(bursts);
        var shape = ps.shape;
        if (r.cone)
        {
            shape.shapeType = ParticleSystemShapeType.Cone;
            shape.angle = 12f;
            shape.radius = r.spread;
        }
        else
        {
            shape.shapeType = ParticleSystemShapeType.Hemisphere;
            shape.radius = r.spread;
            shape.rotation = new Vector3(-90f, 0f, 0f);
        }
        if (!Mathf.Approximately(r.grow, 1f))
        {
            var grow = ps.sizeOverLifetime;
            grow.enabled = true;
            grow.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 0.6f, 1f, r.grow));
        }
        Fade(ps, r.hold);
    }

    static Material Soft(bool additive)
    {
        var material = additive ? glowMaterial : softMaterial;
        if (material != null) return material;
        material = UrpMaterials.NewUnlitParticles(additive ? "Burst glow" : "Burst", Color.white, additive);
        var soft = ProceduralTextures.SoftPuff(64);
        if (material.HasProperty("_BaseMap")) material.SetTexture("_BaseMap", soft);
        material.mainTexture = soft;
        if (additive) glowMaterial = material;
        else softMaterial = material;
        return material;
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
