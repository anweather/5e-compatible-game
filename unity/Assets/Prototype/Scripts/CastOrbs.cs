using UnityEngine;

// The loaded elements in the wizard's hands (docs/SPELL_COMBOS.md): a small
// glowing orb in each, built from two simple spheres, motes drawn in to a
// hand while it gathers, and a fizzle when a cast is dropped. Only the show:
// CastBeats says what's loaded and how it looks, on every copy of a wizard.
public sealed class CastOrbs
{
    const float Size = 0.16f, Glow = 0.9f;
    const float MoteReach = 0.5f, MoteSeconds = 0.32f;
    const int MoteRate = 55, MostMotes = 40;

    readonly CastPose pose;
    readonly Renderer[] orbs = new Renderer[2];
    readonly Material[] materials = new Material[2];
    readonly string[] showing = new string[2];
    readonly ParticleSystem[] motes = new ParticleSystem[2];
    readonly string[] drawing = new string[2];

    public CastOrbs(Transform owner, CastPose pose)
    {
        this.pose = pose;
        for (int i = 0; i < 2; i++)
        {
            var orb = Primitives.Visual(PrimitiveType.Sphere, "Cast orb");
            orb.transform.SetParent(owner, false);
            orb.transform.localScale = Vector3.one * Size;
            orbs[i] = orb.GetComponent<Renderer>();
            orbs[i].shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            materials[i] = UrpMaterials.NewLit("Cast orb", Color.white, emission: Color.white);
            orbs[i].sharedMaterial = materials[i];
            orb.SetActive(false);
            motes[i] = NewMotes(owner);
        }
    }

    // Motes that start out around the hand and fall in to it.
    static ParticleSystem NewMotes(Transform owner)
    {
        var host = new GameObject("Cast motes");
        host.transform.SetParent(owner, false);
        var ps = host.AddComponent<ParticleSystem>();
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        var main = ps.main;
        main.playOnAwake = false;
        main.loop = true;
        main.duration = 1f;
        main.maxParticles = MostMotes;
        main.startLifetime = MoteSeconds;
        main.startSpeed = 0f;
        main.startSize = new ParticleSystem.MinMaxCurve(0.03f, 0.08f);
        main.simulationSpace = ParticleSystemSimulationSpace.Local;
        var emission = ps.emission;
        emission.rateOverTime = MoteRate;
        var shape = ps.shape;
        shape.shapeType = ParticleSystemShapeType.Sphere;
        shape.radius = MoteReach;
        shape.radiusThickness = 0f;
        var pull = ps.velocityOverLifetime;
        pull.enabled = true;
        pull.space = ParticleSystemSimulationSpace.Local;
        pull.radial = -MoteReach / MoteSeconds;
        var fade = ps.colorOverLifetime;
        fade.enabled = true;
        var gradient = new Gradient();
        gradient.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) }, new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.4f), new GradientAlphaKey(0.6f, 1f) });
        fade.color = gradient;
        var look = ps.GetComponent<ParticleSystemRenderer>();
        look.sharedMaterial = BurstFx.Glow;
        look.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        return ps;
    }

    /// <summary>Each frame: which element, if any, is being drawn in to each hand.</summary>
    public void Motes(string right, string left)
    {
        if (pose == null) return;
        Draw(0, right, pose.RightPalm);
        Draw(1, left, pose.LeftPalm);
    }

    void Draw(int i, string element, Vector3 at)
    {
        motes[i].transform.position = at;
        if (element == drawing[i]) return;
        drawing[i] = element;
        if (element == null)
        {
            motes[i].Stop(true, ParticleSystemStopBehavior.StopEmitting);
            return;
        }
        var color = HudDraw.ElementColor(element);
        var main = motes[i].main;
        main.startColor = new ParticleSystem.MinMaxGradient(color, Color.Lerp(color, Color.white, 0.5f));
        motes[i].Play(true);
    }

    /// <summary>
    /// Each frame, after the hands are placed: what's loaded (null for an empty hand) at
    /// <paramref name="size"/> times the usual, <paramref name="merge"/> of the way between the hands.
    /// </summary>
    public void Show(string first, string second, float size, float merge)
    {
        if (pose == null) return;
        var (right, left) = (pose.RightPalm, pose.LeftPalm);
        var middle = (right + left) / 2f;
        Hold(0, first, Vector3.Lerp(right, middle, merge), size);
        Hold(1, second, Vector3.Lerp(left, middle, merge), size);
    }

    /// <summary>The dropped elements go out at the hands.</summary>
    public void Fizzle(string first, string second)
    {
        if (pose == null) return;
        if (first != null) BurstFx.Play(BurstFx.Kind.Fizzle, pose.RightPalm, 1f, null, HudDraw.ElementColor(first));
        if (second != null) BurstFx.Play(BurstFx.Kind.Fizzle, pose.LeftPalm, 1f, null, HudDraw.ElementColor(second));
    }

    void Hold(int i, string element, Vector3 at, float size)
    {
        orbs[i].gameObject.SetActive(element != null);
        if (element == null)
        {
            showing[i] = null;
            return;
        }
        if (element != showing[i])
        {
            showing[i] = element;
            var color = HudDraw.ElementColor(element);
            UrpMaterials.Configure(materials[i], color, null, color * Glow);
        }
        orbs[i].transform.position = at;
        orbs[i].transform.localScale = Vector3.one * (Size * size);
    }

    public void Destroy()
    {
        for (int i = 0; i < 2; i++)
        {
            if (orbs[i] != null) Object.Destroy(orbs[i].gameObject);
            if (motes[i] != null) Object.Destroy(motes[i].gameObject);
            Object.Destroy(materials[i]);
        }
    }
}
