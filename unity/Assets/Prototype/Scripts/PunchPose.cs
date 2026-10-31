using UnityEngine;

// The punch on the bones, laid over the animation with LimbIK, CastPose's limbs and
// CastFeel's poses: the fist drawn back to the ribs with the shoulders turned away,
// then out at what's being punched (ItemHolder.PunchLanding's point, or straight
// ahead on a miss) with the shoulders turning through and the body going after it,
// a moment held there, and back to the animation. The other hand comes up to guard.
// A hit-stop (CastBeats.HitStop, from CastImpact) stands this clock still too. After
// CastBeats, which it never fights: the moment a punch may no longer happen
// (ItemHolder.MayPunchNow) it lets go. Only the puncher's machine plays it, as a throw.
[DefaultExecutionOrder(207)]
[RequireComponent(typeof(Animator), typeof(CastPose), typeof(CastBeats))]
[RequireComponent(typeof(ItemHolder))]
public class PunchPose : MonoBehaviour
{
    enum Phase { Rest, Draw, Out, Hold, Back }

    CastBeats beats;
    CastPose pose;
    ItemHolder hands;
    ItemHolder.PunchLanding aimed;
    CastBeats.Blend from, body;
    Phase phase;
    float inPhase;

    /// <summary>A punch is on the bones.</summary>
    public bool Playing => phase != Phase.Rest;
    /// <summary>Where this punch's fist is going.</summary>
    public Vector3 Goal { get; private set; }
    /// <summary>How near the hand has come to the goal this punch (m), for the probe.</summary>
    public float NearestFist { get; private set; } = float.MaxValue;

    void Awake()
    {
        beats = GetComponent<CastBeats>();
        pose = GetComponent<CastPose>();
        hands = GetComponent<ItemHolder>();
        hands.PunchStarted += Started;
        hands.PunchLanded += landing => aimed = landing;
    }

    void Started(ItemHolder.PunchLanding landing)
    {
        aimed = landing;
        NearestFist = float.MaxValue;
        if (phase == Phase.Rest) body = CastBeats.Blend.Of(beats.feel.punchBack);
        Enter(Phase.Draw);
    }

    void Enter(Phase next)
    {
        phase = next;
        inPhase = 0f;
        from = body;
    }

    float Seconds => phase switch
    {
        Phase.Draw => beats.feel.punchDraw.seconds,
        Phase.Out => beats.feel.punchOut.seconds,
        Phase.Hold => beats.feel.punchHold,
        _ => beats.feel.punchBack.seconds,
    };

    CastFeel.BeatPose Pose => phase switch
    {
        Phase.Draw => beats.feel.punchDraw,
        Phase.Back => beats.feel.punchBack,
        _ => beats.feel.punchOut,
    };

    void LateUpdate()
    {
        if (phase == Phase.Rest) return;
        if (!hands.MayPunchNow)
        {
            phase = Phase.Rest;
            return;
        }
        if (!beats.Stopped) inPhase += Time.deltaTime;
        if (inPhase >= Seconds) Enter(phase == Phase.Back ? Phase.Rest : phase + 1);
        if (phase == Phase.Rest) return;

        var p = Pose;
        bool striking = phase is Phase.Out or Phase.Hold;
        var goal = Aimed(p, striking);
        float t = phase == Phase.Hold || p.seconds <= 0f ? 1f : Mathf.Clamp01(inPhase / p.seconds);
        body = CastBeats.Blend.Lerp(from, goal, p.curve.Evaluate(t));
        Lay(striking);
        if (striking) NearestFist = Mathf.Min(NearestFist, Vector3.Distance(pose.HandPosition, Goal));
    }

    // The pose, with the fist on what's being punched: the hand bone a fist short of it, and the body bent down to something low.
    CastBeats.Blend Aimed(CastFeel.BeatPose p, bool striking)
    {
        var blend = CastBeats.Blend.Of(p);
        if (!striking) return blend;
        var feel = beats.feel;
        var forward = aimed.Way;
        var right = Vector3.Cross(Vector3.up, forward);
        var origin = transform.position;
        if (!aimed.Reached)
        {
            Goal = origin + (right * p.rightHand.x + Vector3.up * p.rightHand.y + forward * p.rightHand.z) * pose.Scale;
            return blend;
        }
        Goal = aimed.Point;
        var hand = (aimed.Point - origin) / pose.Scale - forward * feel.punchFist;
        blend.right = new Vector3(Vector3.Dot(hand, right), hand.y, Vector3.Dot(hand, forward));
        float low = Mathf.Clamp01(1f - hand.y / feel.punchLowFrom);
        blend.lean += feel.punchLowLean * low;
        blend.dip += feel.punchLowDip * low;
        return blend;
    }

    // The body, then the hands on it, as CastBeats lays a beat.
    void Lay(bool striking)
    {
        var feel = beats.feel;
        var forward = aimed.Way;
        // The shoulders round to what's off to the side: the wizard itself stays facing as it was.
        float turn = Mathf.Clamp(Vector3.SignedAngle(transform.forward, forward, Vector3.up), -feel.punchTurnMost, feel.punchTurnMost) * body.rightWeight;
        pose.LayBody(body.dip, body.lean, forward, -forward * (body.recoil * pose.Scale), body.twist + turn);
        if (striking && aimed.Reached)
        {
            // Further than the arm goes: the body goes after it, a little.
            float shortBy = Vector3.Distance(pose.RightShoulder, Goal) - pose.ArmLength;
            if (shortBy > 0f) pose.LayBody(0f, 0f, forward, forward * (Mathf.Min(shortBy, feel.punchLunge * pose.Scale) * body.rightWeight));
        }
        var frame = new CastPose.HandFrame(transform.position, forward, Vector3.Cross(Vector3.up, forward), Quaternion.identity, 0f);
        pose.LayHand(true, frame, body.right, body.rightWeight, body.fingers, body.palm);
        pose.LayHand(false, frame, body.left, body.leftWeight, body.fingers, body.palm);
        pose.Clench(true, feel.punchClench * body.rightWeight);
        pose.Clench(false, feel.punchClench * body.leftWeight);
    }
}
