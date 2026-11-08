using Chem;
using Punchy.Fauna;
using Unity.Mathematics;
using UnityEngine;

// How a raider's route looks (docs/design/the-hold.md, phase 4). A flyer has no body in the fauna rows: it's drawn here, a few
// segments of body and flapping wing, high up with a soft shadow on the ground below it, and it falls when the core lands it. A
// climber lies flat on the slope however steep, past where a creature's own pitch stops, so it climbs instead of gliding. The
// boss is only bigger here: it holds one cell like any raider. Only the show: the core decides everything.
public partial class DefenseField
{
    /// <summary>How high a flyer is drawn (SpellCaster's lock-on aims there).</summary>
    public const float FlyHeight = 11f;
    const float WhirlLift = 2.5f;
    const float LiftRate = 2.6f, FlapRate = 9f, FlapReach = 0.55f, Hover = 0.35f;
    const float WingSpan = 0.62f, BodyLength = 0.8f, ShadowSize = 2.2f, ShadowFade = 0.55f;
    const float BossSize = 1.9f, MostCreaturePitch = 0.45f;
    static readonly float4 Ash = new(0.2f, 0.19f, 0.2f, 1f);

    const float Rounding = 7f, Turning = 8f, LungeReach = 1.1f, LungeSeconds = 0.45f, RearAngle = 0.75f;
    static readonly float3 BlowTint = new(1f, 0.25f, 0.12f);

    // A raider's blow on the body: up on its back end, then down onto what it strikes, flushing red.
    void Blow(Shown s, int from, int to)
    {
        if (s.lunge <= 0f) return;
        float t = 1f - s.lunge;
        float rear = t < 0.4f ? t / 0.4f : -(t - 0.4f) / 0.6f * 0.6f + (1f - (t - 0.4f) / 0.6f);
        var turn = quaternion.AxisAngle(math.mul(quaternion.RotateY(s.state.Yaw), new float3(1f, 0f, 0f)), -rear * RearAngle);
        float3 about = s.at;
        float flush = Mathf.Sin(s.lunge * Mathf.PI) * 0.7f;
        for (int i = from; i < to; i++)
        {
            instances[i].Position = about + math.mul(turn, instances[i].Position - about);
            instances[i].Rotation = math.mul(turn, instances[i].Rotation);
            var c = instances[i].Color;
            instances[i].Color = new float4(math.lerp(c.xyz, BlowTint, flush), c.w);
        }
    }

    // A blow, seen: the core gives a raider its wait as it strikes, so a wait that jumps up is a blow, and the raider lunges.
    void Struck(Shown s)
    {
        int wait = s.raider?.Wait ?? 0;
        int every = s.raider != null ? World.Rules.Entity(s.raider.Kind)?.Raider?.AttackEvery ?? 0 : 0;
        if (every > 0 && wait >= every && s.waitWas < every)
        {
            s.lunge = 1f;
            FaceTheHeart(s);
        }
        s.waitWas = wait;
        s.lunge = Mathf.Max(0f, s.lunge - Time.deltaTime / LungeSeconds);
    }

    // A raider striking beside the Hold's heart turns to it: it's the crystal it's hitting (HoldCrystal), not the way it came.
    void FaceTheHeart(Shown s)
    {
        var world = World;
        if (s.raider == null || world.HoldSite == null || !world.HoldRunning) return;
        var (hx, hy) = world.HoldSite.Heart;
        if (Mathf.Max(Mathf.Abs(s.raider.X - hx), Mathf.Abs(s.raider.Y - hy)) > 1) return;
        var to = grid.CellCenter(hx, hy) - s.at;
        if (to.x * to.x + to.z * to.z > 1e-4f) s.state.Yaw = Mathf.Atan2(to.x, to.z);
    }

    Mesh shadowMesh;
    Material shadowMaterial;

    static bool Boss(World world, Raider r) => r != null && world.Rules.AdjectiveOf(r.Kind)?.Boss == true;

    /// <summary>The boss raiders drawn now: for the probe and the HUD.</summary>
    public int BossesDrawn { get; private set; }

    // How high this raider should be: eased, so one the core brings down falls out of the air.
    void Lift(Shown s, World world)
    {
        // One riding up on an Earth Spire goes where its point is (ChemGrid.SpireRide).
        float? ride = s.raider != null ? grid.SpireRide(s.raider.Id) : null;
        if (ride.HasValue)
        {
            s.lift = ride.Value;
            return;
        }
        // A flyer flies; one a whirlwind holds in its eye is lifted off its feet (World.LiftInTheEye).
        float wanted = 0f;
        if (s.raider != null && world.Flying(s.raider)) wanted = FlyHeight;
        else if (s.raider != null && s.raider.StatusOf(World.LiftedStatus) > 0) wanted = WhirlLift;
        float rise = wanted >= FlyHeight ? FlyHeight : WhirlLift * 3f * Time.deltaTime;
        s.lift = Mathf.MoveTowards(s.lift, wanted, wanted > s.lift ? rise : FlyHeight * LiftRate * Time.deltaTime);
    }

    void Shadow(Shown s)
    {
        if (s.lift <= 0.05f) return;
        if (shadowMesh == null) MakeShadow();
        var ground = s.at;
        ground.y = grid.GroundAt(ground) + 0.08f;
        float size = ShadowSize * (1f + s.lift / FlyHeight * ShadowFade);
        Graphics.DrawMesh(shadowMesh, Matrix4x4.TRS(ground, Quaternion.Euler(90f, 0f, 0f), Vector3.one * size), shadowMaterial, 0);
    }

    void MakeShadow()
    {
        var quad = Primitives.Visual(PrimitiveType.Quad, "Flyer shadow");
        shadowMesh = quad.GetComponent<MeshFilter>().sharedMesh;
        Destroy(quad);
        shadowMaterial = UrpMaterials.NewUnlitParticles("Flyer shadow", new Color(0f, 0f, 0f, 0.6f), additive: false);
        var soft = ProceduralTextures.SoftPuff(64);
        if (shadowMaterial.HasProperty("_BaseMap")) shadowMaterial.SetTexture("_BaseMap", soft);
        shadowMaterial.mainTexture = soft;
    }

    // A body, a head, a tail and two wings of two segments, flapping in the air and folded on the ground.
    int Wings(Shown s, int at)
    {
        const int Segments = 7;
        if (instances.Length < at + Segments) System.Array.Resize(ref instances, Mathf.NextPowerOfTwo(at + Segments));
        float size = s.state.Scale;
        float aloft = Mathf.Clamp01(s.lift / FlyHeight);
        float flap = aloft > 0f ? Mathf.Sin(s.state.Clock * FlapRate) * FlapReach : -1.1f;
        var turn = quaternion.RotateY(s.state.Yaw);
        float3 forward = math.mul(turn, new float3(0f, 0f, 1f)), side = math.mul(turn, new float3(1f, 0f, 0f));
        float3 middle = (float3)(Vector3)s.at + new float3(0f, Mathf.Lerp(Hover, Mathf.Sin(s.state.Clock * FlapRate) * -0.14f, aloft) * size, 0f);
        float half = BodyLength * 0.5f * size;
        instances[at] = Segment(middle - forward * half, turn, BodyLength * size, 0.13f * size, 0.08f * size, 1f);
        instances[at + 1] = Segment(middle + forward * half, turn, 0.2f * size, 0.09f * size, 0.04f * size, 1f);
        instances[at + 2] = Segment(middle - forward * half, quaternion.LookRotation(-forward, new float3(0f, 1f, 0f)), 0.34f * size, 0.08f * size, 0.01f * size, 0.3f);
        Wing(at + 3, middle, side, forward, flap, size);
        Wing(at + 5, middle, -side, forward, flap, size);
        return at + Segments;
    }

    void Wing(int at, float3 shoulder, float3 side, float3 forward, float flap, float size)
    {
        float3 inner = math.normalize(side * Mathf.Cos(flap) + new float3(0f, Mathf.Sin(flap), 0f));
        float3 outer = math.normalize(side * Mathf.Cos(flap * 1.7f) + new float3(0f, Mathf.Sin(flap * 1.7f), 0f));
        float span = WingSpan * size;
        instances[at] = Segment(shoulder, quaternion.LookRotation(inner, forward), span, 0.16f * size, 0.13f * size, 0.22f);
        instances[at + 1] = Segment(shoulder + inner * span, quaternion.LookRotation(outer, forward), span, 0.13f * size, 0.01f * size, 0.22f);
    }

    static SegmentInstance Segment(float3 position, quaternion rotation, float length, float start, float end, float flatten) => new()
    {
        Position = position,
        Rotation = rotation,
        Length = length,
        RadiusStart = start,
        RadiusEnd = end,
        Flatten = flatten,
        Color = Ash,
    };

    // A climber's body turned the rest of the way onto the slope, about where it stands.
    void OntoTheSlope(Shown s, int from, int to)
    {
        var forward = new Vector3(Mathf.Sin(s.state.Yaw), 0f, Mathf.Cos(s.state.Yaw));
        const float Reach = 0.8f;
        float rise = grid.GroundAt(s.at + forward * Reach) - grid.GroundAt(s.at - forward * Reach);
        float slope = Mathf.Atan(rise / (2f * Reach));
        float past = slope - Mathf.Clamp(slope, -MostCreaturePitch, MostCreaturePitch);
        if (Mathf.Abs(past) < 0.01f) return;
        var turn = quaternion.AxisAngle(math.mul(quaternion.RotateY(s.state.Yaw), new float3(1f, 0f, 0f)), -past);
        float3 about = s.at;
        for (int i = from; i < to; i++)
        {
            instances[i].Position = about + math.mul(turn, instances[i].Position - about);
            instances[i].Rotation = math.mul(turn, instances[i].Rotation);
        }
    }
}
