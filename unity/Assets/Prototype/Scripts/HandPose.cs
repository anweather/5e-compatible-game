using Chem;
using UnityEngine;

// The Hold's hands on the body (docs/design/the-hold.md, phase 3), on every copy of every wizard, from the hands in the core. Each
// arm has its own gesture, so one can thrust while the other throws; the same gesture on both is the two-handed version. The arm
// winds up while its gesture is under way and goes out when the core fires it. The arm with no gesture stays down, out of what
// CastBeats lays. It also shows what each hand holds (HandHeld). Only the show: nothing here feeds back. After CastBeats and
// PunchPose, whose arms it takes while a hand's gesture plays, and before the robe and outfit.
[DefaultExecutionOrder(208)]
[RequireComponent(typeof(Animator), typeof(CastPose), typeof(CastBeats))]
public class HandPose : MonoBehaviour
{
    const float WindSeconds = 0.22f, OutSeconds = 0.1f, HoldSeconds = 0.22f, BackSeconds = 0.32f;
    const float FistDegrees = 70f, FlinchSeconds = 0.4f, FlinchLean = 16f, FlinchDip = 0.08f, FlinchBack = 0.12f;

    enum Phase { Rest, Wind, Out, Hold, Back }

    // The right arm's, by gesture (HandsDef); the left's are their mirror.
    static readonly Vector3[] WindUp = { new(0.26f, 1.02f, -0.12f), new(0.3f, 1.78f, -0.26f), new(0.62f, 1.22f, 0.02f) };
    static readonly Vector3[] Out = { new(0.14f, 1.3f, 0.62f), new(0.2f, 1.5f, 0.6f), new(-0.28f, 1.2f, 0.46f) };
    static readonly Vector3[] BothWindUp = { new(0.16f, 1.08f, 0.04f), new(0.12f, 1.86f, -0.14f), new(0.68f, 1.3f, 0.1f) };
    static readonly Vector3[] BothOut = { new(0.05f, 1.34f, 0.64f), new(0.06f, 1.5f, 0.64f), new(0.1f, 1.26f, 0.6f) };
    static readonly Vector3 Down = new(0.3f, 0.86f, 0.04f);
    static readonly Vector3 WoundFingers = new(0f, 1f, 0.4f), OutFingers = new(0f, 1f, 0.3f), SweptFingers = new(-1f, 0.1f, 0.6f);

    sealed class Arm
    {
        public int gesture = HandsDef.NoGesture;
        public Phase phase;
        public float inPhase, weight, fromWeight;
        public Vector3 at, from;
        public bool fist;
    }

    readonly Arm[] arms = { new(), new() };
    readonly int[] now = { HandsDef.NoGesture, HandsDef.NoGesture };
    readonly FiredCastsHeard fired = new();
    CastPose pose;
    CastBeats beats;
    SpellCaster caster;
    HandHeld held;
    bool together, aimed;
    float flinch;

    /// <summary>What's shown in a hand (HandsDef.Left, Right), or null: for the probe.</summary>
    public string Showing(int hand) => held.Showing(hand);

    /// <summary>The gesture an arm is playing, or HandsDef.NoGesture: for the probe.</summary>
    public int Playing(int hand) => arms[hand].phase == Phase.Rest ? HandsDef.NoGesture : arms[hand].gesture;

    /// <summary>The same gesture is on both arms, as one two-handed movement: for the probe.</summary>
    public bool Together => together;

    void Awake()
    {
        pose = GetComponent<CastPose>();
        beats = GetComponent<CastBeats>();
        TryGetComponent(out caster);
        held = new HandHeld(transform, pose);
    }

    void OnDestroy() => held?.Destroy();

    /// <summary>This wizard took a blow: it flinches.</summary>
    public void Flinch() => flinch = 1f;

    void LayFlinch()
    {
        if (flinch <= 0f) return;
        flinch = Mathf.Max(0f, flinch - Time.deltaTime / FlinchSeconds);
        float back = Mathf.Sin(flinch * Mathf.PI);
        pose.LayBody(FlinchDip * back, -FlinchLean * back, transform.forward, -transform.forward * (FlinchBack * back));
    }

    void LateUpdate()
    {
        LayFlinch();
        var world = caster != null && caster.grid != null ? caster.grid.World : null;
        if (world == null) return;
        if (!world.TickInProgress) Read(world);
        foreach (var arm in arms) Advance(arm);
        if (arms[0].phase != Phase.Rest || arms[1].phase != Phase.Rest) Lay();
        ShowHeld(world);
    }

    // Only between ticks, as CastBeats reads the cast.
    void Read(World world)
    {
        string player = beats.Player;
        bool went = false;
        foreach (var f in fired.Take(world))
        {
            if (f.Player != player || (f.Range <= 0 && f.Act == null)) continue;
            went = true;
            aimed |= f.Spell != null;
        }
        bool on = !caster.Replaying && world.HandsOn(player);
        var h = on ? world.HandsOf(player) : null;
        for (int i = 0; i < HandsDef.Count; i++) now[i] = h?.Gesture[i] ?? HandsDef.NoGesture;
        if (now[0] != HandsDef.NoGesture && now[0] == now[1]) together = true;
        else if (now[0] != HandsDef.NoGesture || now[1] != HandsDef.NoGesture) together = false;
        for (int i = 0; i < HandsDef.Count; i++) Follow(arms[i], now[i], went, h?.Holds[i] == null);
    }

    void Follow(Arm arm, int gesture, bool went, bool empty)
    {
        if (gesture != HandsDef.NoGesture)
        {
            if (arm.phase != Phase.Wind || arm.gesture != gesture)
            {
                arm.gesture = gesture;
                arm.fist = empty && gesture != HandsDef.Throw;
                Enter(arm, Phase.Wind);
            }
        }
        else if (arm.phase == Phase.Wind) Enter(arm, went ? Phase.Out : Phase.Back);
    }

    static void Enter(Arm arm, Phase phase)
    {
        arm.phase = phase;
        arm.inPhase = 0f;
        arm.from = arm.at;
        arm.fromWeight = arm.weight;
    }

    void Advance(Arm arm)
    {
        arm.inPhase += Time.deltaTime;
        switch (arm.phase)
        {
            case Phase.Wind:
                Toward(arm, (together ? BothWindUp : WindUp)[arm.gesture], 1f, WindSeconds, smooth: true);
                break;
            case Phase.Out:
                Toward(arm, (together ? BothOut : Out)[arm.gesture], 1f, OutSeconds, smooth: false);
                if (arm.inPhase >= OutSeconds) Enter(arm, Phase.Hold);
                break;
            case Phase.Hold:
                if (arm.inPhase >= HoldSeconds) Enter(arm, Phase.Back);
                break;
            case Phase.Back:
                Toward(arm, arm.from, 0f, BackSeconds, smooth: true);
                if (arm.inPhase >= BackSeconds) Rest(arm);
                break;
        }
    }

    void Rest(Arm arm)
    {
        arm.phase = Phase.Rest;
        arm.gesture = HandsDef.NoGesture;
        arm.weight = 0f;
        arm.fist = false;
        if (arms[0].phase == Phase.Rest && arms[1].phase == Phase.Rest) aimed = false;
    }

    static void Toward(Arm arm, Vector3 to, float weight, float seconds, bool smooth)
    {
        float t = Mathf.Clamp01(arm.inPhase / seconds);
        if (smooth) t = Mathf.SmoothStep(0f, 1f, t);
        arm.at = Vector3.Lerp(arm.from, to, t);
        arm.weight = Mathf.Lerp(arm.fromWeight, weight, t);
    }

    void Lay()
    {
        var forward = transform.forward;
        var aim = Quaternion.identity;
        if (aimed && (Going(arms[0]) || Going(arms[1])))
        {
            var to = beats.AimPoint - transform.position;
            to.y = 0f;
            if (to.sqrMagnitude > 1e-4f) forward = to.normalized;
            aim = pose.AimAt(beats.AimPoint, forward, Vector3.Cross(Vector3.up, forward));
        }
        var frame = new CastPose.HandFrame(transform.position, forward, Vector3.Cross(Vector3.up, forward), aim, 0f);
        LayArm(frame, HandsDef.Right, HandsDef.Left);
        LayArm(frame, HandsDef.Left, HandsDef.Right);
    }

    static bool Going(Arm arm) => arm.phase is Phase.Out or Phase.Hold or Phase.Back;

    void LayArm(in CastPose.HandFrame frame, int hand, int other)
    {
        var arm = arms[hand];
        bool isRight = hand == HandsDef.Right;
        var at = arm.phase == Phase.Rest ? Down : arm.at;
        float weight = arm.phase == Phase.Rest ? arms[other].weight : arm.weight;
        if (!isRight) at.x = -at.x;
        bool swept = arm.gesture == HandsDef.Sweep && Going(arm);
        var fingers = swept ? SweptFingers : Going(arm) ? OutFingers : WoundFingers;
        pose.LayHand(isRight, frame, at, weight, fingers, Going(arm) ? Vector3.forward : Vector3.left);
        if (arm.fist) pose.Clench(isRight, FistDegrees * arm.weight);
    }

    void ShowHeld(World world)
    {
        string player = beats.Player;
        if (caster.Replaying || !world.HandsOn(player))
        {
            held.Show(null, null);
            return;
        }
        var h = world.HandsOf(player);
        held.Show(h.Holds[HandsDef.Right], h.Holds[HandsDef.Left]);
    }
}
