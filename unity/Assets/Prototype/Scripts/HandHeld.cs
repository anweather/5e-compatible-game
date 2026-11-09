using Chem;
using UnityEngine;

// What a wizard's hands hold in the Hold (docs/design/the-hold.md, "Showing it"): fire in the hand, water round it, stones orbiting
// it, air curling off it, and nothing for an empty hand. One small particle system a hand, set up for its element when that changes,
// so a full party costs a few dozen particles. HandPose says what's held, on every copy of a wizard.
public sealed class HandHeld
{
    const int MostParticles = 32;
    const float StoneSize = 0.07f;
    static readonly Color Stone = new(0.46f, 0.42f, 0.36f);
    static Mesh stoneMesh;
    static Material stoneMaterial;

    readonly CastPose pose;
    readonly ParticleSystem[] shows = new ParticleSystem[HandsDef.Count];
    readonly string[] showing = new string[HandsDef.Count];

    public HandHeld(Transform owner, CastPose pose)
    {
        this.pose = pose;
        for (int i = 0; i < HandsDef.Count; i++) shows[i] = New(owner);
    }

    static ParticleSystem New(Transform owner)
    {
        var host = new GameObject("Hand held");
        host.transform.SetParent(owner, false);
        var ps = host.AddComponent<ParticleSystem>();
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        var main = ps.main;
        main.playOnAwake = false;
        main.loop = true;
        main.duration = 1f;
        main.maxParticles = MostParticles;
        main.cullingMode = ParticleSystemCullingMode.AlwaysSimulate;
        var look = ps.GetComponent<ParticleSystemRenderer>();
        look.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        look.receiveShadows = false;
        return ps;
    }

    /// <summary>What's shown in a hand (HandsDef.Left, Right), or null.</summary>
    public string Showing(int hand) => showing[hand];

    /// <summary>Each frame, after the arms are laid: what each hand holds, null for nothing.</summary>
    public void Show(string right, string left)
    {
        Hold(HandsDef.Right, right, pose.RightPalm);
        Hold(HandsDef.Left, left, pose.LeftPalm);
    }

    void Hold(int hand, string element, Vector3 at)
    {
        var ps = shows[hand];
        ps.transform.SetPositionAndRotation(at, Quaternion.identity);
        if (element == showing[hand]) return;
        showing[hand] = element;
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        if (element == null) return;
        Dress(ps, element);
        ps.Play(true);
    }

    static void Dress(ParticleSystem ps, string element)
    {
        var color = HudDraw.ElementColor(element);
        switch (element)
        {
            case ElementIds.Fire:
                Set(ps, color, Color.Lerp(color, Color.yellow, 0.6f), 0.5f, 0.09f, 0.2f, 44f, ParticleSystemSimulationSpace.World);
                Shaped(ps, ParticleSystemShapeType.Sphere, 0.05f);
                Moving(ps, up: 0.7f, orbit: 0f, inward: 0f);
                break;
            case ElementIds.Water:
                Set(ps, color, Color.Lerp(color, Color.white, 0.5f), 0.8f, 0.06f, 0.11f, 30f, ParticleSystemSimulationSpace.Local);
                Shaped(ps, ParticleSystemShapeType.Circle, 0.15f);
                Moving(ps, up: 0.03f, orbit: 5f, inward: 0f);
                break;
            case ElementIds.Air:
                Set(ps, new Color(color.r, color.g, color.b, 0.5f), new Color(1f, 1f, 1f, 0.7f), 0.8f, 0.06f, 0.14f, 24f, ParticleSystemSimulationSpace.Local);
                Shaped(ps, ParticleSystemShapeType.Circle, 0.12f);
                Moving(ps, up: 0.4f, orbit: 7f, inward: 0.1f);
                break;
            default:
                Set(ps, Color.white, Color.white, 1.5f, StoneSize * 0.7f, StoneSize * 1.3f, 3.4f, ParticleSystemSimulationSpace.Local);
                Shaped(ps, ParticleSystemShapeType.Circle, 0.15f);
                Moving(ps, up: 0f, orbit: 3.2f, inward: 0f);
                Stones(ps);
                return;
        }
        var look = ps.GetComponent<ParticleSystemRenderer>();
        look.renderMode = ParticleSystemRenderMode.Billboard;
        look.sharedMaterial = BurstFx.Glow;
        var rotation = ps.main;
        rotation.startRotation3D = false;
    }

    static void Set(ParticleSystem ps, Color from, Color to, float seconds, float smallest, float largest, float rate, ParticleSystemSimulationSpace space)
    {
        var main = ps.main;
        main.startColor = new ParticleSystem.MinMaxGradient(from, to);
        main.startLifetime = seconds;
        main.startSpeed = 0f;
        main.startSize = new ParticleSystem.MinMaxCurve(smallest, largest);
        main.simulationSpace = space;
        var emission = ps.emission;
        emission.rateOverTime = rate;
        var size = ps.sizeOverLifetime;
        size.enabled = true;
        size.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(new Keyframe(0f, 0.2f), new Keyframe(0.2f, 1f), new Keyframe(0.8f, 1f), new Keyframe(1f, 0f)));
    }

    static void Shaped(ParticleSystem ps, ParticleSystemShapeType type, float radius)
    {
        var shape = ps.shape;
        shape.shapeType = type;
        shape.radius = radius;
        shape.radiusThickness = 0f;
        shape.rotation = type == ParticleSystemShapeType.Circle ? new Vector3(90f, 0f, 0f) : Vector3.zero;
    }

    static void Moving(ParticleSystem ps, float up, float orbit, float inward)
    {
        var moving = ps.velocityOverLifetime;
        moving.enabled = true;
        moving.space = ParticleSystemSimulationSpace.Local;
        moving.x = 0f;
        moving.y = up;
        moving.z = 0f;
        moving.orbitalX = 0f;
        moving.orbitalY = orbit;
        moving.orbitalZ = 0f;
        moving.radial = -inward;
    }

    static void Stones(ParticleSystem ps)
    {
        if (stoneMesh == null)
        {
            var cube = Primitives.Visual(PrimitiveType.Cube, "Hand stone");
            stoneMesh = cube.GetComponent<MeshFilter>().sharedMesh;
            Object.Destroy(cube);
        }
        // A Play mode's end destroys the material and not the built-in mesh: each is made again on its own.
        if (stoneMaterial == null) stoneMaterial = UrpMaterials.NewLit("Hand stone", Stone);
        var look = ps.GetComponent<ParticleSystemRenderer>();
        look.renderMode = ParticleSystemRenderMode.Mesh;
        look.mesh = stoneMesh;
        look.sharedMaterial = stoneMaterial;
        var main = ps.main;
        main.startRotation3D = true;
        main.startRotationX = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
        main.startRotationY = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
        main.startRotationZ = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
    }

    public void Destroy()
    {
        foreach (var ps in shows)
            if (ps != null) Object.Destroy(ps.gameObject);
    }
}
