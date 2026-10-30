using UnityEngine;

// Glass breaking: a spray of glinting shards that skitter and settle, for a
// glow ball or a lantern that shatters (Fragile, PhysicsProp.ShowBreak) and
// anything else brittle that breaks. GPU-first: one particle system for the
// whole game, made on first use, and every burst is particles emitted into it
// at a point (EmitParams), never a new object. Presentation only: each peer
// shows the shards of the breaks it sees.
public static class ShardBurst
{
    // Most shards alive at once across every burst (the oldest go first past it).
    const int MaxShards = 600;

    static ParticleSystem shards;
    static Material material;

    /// <summary>A burst of <paramref name="count"/> shards at <paramref name="at"/>, tinted toward <paramref name="tint"/> (a glow ball's light).</summary>
    public static void At(Vector3 at, Color tint, int count = 28)
    {
        var ps = Shards();
        var glass = new Color(0.86f, 0.95f, 1f, 0.95f);
        var shine = Color.Lerp(glass, tint, 0.55f);
        var emit = new ParticleSystem.EmitParams { applyShapeToPosition = false };
        for (int i = 0; i < count; i++)
        {
            var dir = Random.onUnitSphere;
            dir.y = Mathf.Abs(dir.y) * 0.9f + 0.2f;
            emit.position = at + dir * 0.06f;
            emit.velocity = dir * Random.Range(1.2f, 4.2f);
            emit.startSize3D = new Vector3(Random.Range(0.05f, 0.11f), Random.Range(0.09f, 0.2f), 1f);
            emit.rotation = Random.Range(0f, 360f);
            emit.angularVelocity = Random.Range(-720f, 720f);
            emit.startLifetime = Random.Range(1.2f, 2.2f);
            emit.startColor = Color.Lerp(glass, shine, Random.value);
            ps.Emit(emit, 1);
        }
    }

    // A sharp sliver: a thin triangle, its point at the top, clear round it.
    static Texture2D Sliver(int size)
    {
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false) { name = "Glass sliver", wrapMode = TextureWrapMode.Clamp };
        var pixels = new Color32[size * size];
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float u = (x + 0.5f) / size, v = (y + 0.5f) / size;
                bool inside = Mathf.Abs(u - 0.5f + 0.15f * (v - 0.5f)) < 0.42f * (1f - v);
                pixels[y * size + x] = new Color32(255, 255, 255, inside ? (byte)255 : (byte)0);
            }
        tex.SetPixels32(pixels);
        tex.Apply(false, true);
        return tex;
    }

    // The one system, built in code: small sharp slivers that fall, bounce off the ground and fade.
    static ParticleSystem Shards()
    {
        if (shards != null) return shards;
        var go = new GameObject("Glass shards");
        Object.DontDestroyOnLoad(go);
        shards = go.AddComponent<ParticleSystem>();
        shards.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        var main = shards.main;
        main.loop = true; // always playing: bursts are emitted into it, it never emits on its own
        main.playOnAwake = false;
        main.maxParticles = MaxShards;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.gravityModifier = 1.4f;
        main.startSize3D = true;
        main.startRotation3D = false;
        var emission = shards.emission;
        emission.enabled = false;
        var shape = shards.shape;
        shape.enabled = false;
        var collision = shards.collision;
        collision.enabled = true;
        collision.type = ParticleSystemCollisionType.World;
        collision.mode = ParticleSystemCollisionMode.Collision3D;
        collision.quality = ParticleSystemCollisionQuality.Low;
        collision.bounce = 0.3f;
        collision.dampen = 0.55f;
        collision.lifetimeLoss = 0f;
        var spin = shards.rotationOverLifetime;
        spin.enabled = false;
        var fade = shards.colorOverLifetime;
        fade.enabled = true;
        fade.color = new ParticleSystem.MinMaxGradient(new Gradient
        {
            colorKeys = new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
            alphaKeys = new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, 0.7f), new GradientAlphaKey(0f, 1f) },
        });
        if (material == null)
        {
            material = UrpMaterials.NewUnlitParticles("Glass shard", Color.white, additive: false);
            var sliver = Sliver(32);
            if (material.HasProperty("_BaseMap")) material.SetTexture("_BaseMap", sliver);
            material.mainTexture = sliver;
        }
        var look = go.GetComponent<ParticleSystemRenderer>();
        look.sharedMaterial = material;
        look.renderMode = ParticleSystemRenderMode.Billboard;
        look.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        look.receiveShadows = false;
        shards.Play();
        return shards;
    }
}
