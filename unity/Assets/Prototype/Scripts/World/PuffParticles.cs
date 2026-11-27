using UnityEngine;

// A particle system of soft white puffs that a caller feeds with Emit (never emitting on its own), in world space,
// fading out over each puff's life: the spray and surf mist where water breaks (WaterSplashes) and the foam round a
// rock in the river (RockWake).
public static class PuffParticles
{
    public static ParticleSystem Make(GameObject go, string name, float gravity)
    {
        var ps = go.AddComponent<ParticleSystem>();
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        var main = ps.main;
        main.loop = false;
        main.playOnAwake = false;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.gravityModifier = gravity;
        main.maxParticles = 600;
        var emission = ps.emission;
        emission.enabled = false;
        var fade = ps.colorOverLifetime;
        fade.enabled = true;
        fade.color = new ParticleSystem.MinMaxGradient(new Gradient
        {
            alphaKeys = new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0f, 1f) },
            colorKeys = new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
        });
        var look = go.GetComponent<ParticleSystemRenderer>();
        var mat = UrpMaterials.NewUnlitParticles(name, Color.white, additive: false);
        var puff = ProceduralTextures.SoftPuff(64);
        if (mat.HasProperty("_BaseMap")) mat.SetTexture("_BaseMap", puff);
        mat.mainTexture = puff;
        look.sharedMaterial = mat;
        look.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        look.receiveShadows = false;
        ps.Play();
        return ps;
    }
}
