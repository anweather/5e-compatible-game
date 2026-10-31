using UnityEngine;

// The casting gestures laid over the character's animation (CastGestures:
// a meteor pulled down from overhead, a fireball thrown overhand, earth
// raised from low to high or pressed down, a two-handed shove, a thrust, a
// reach). The hands go where the gesture's keys say with LimbIK, the hips
// dip with the knees bending to keep the feet planted, the spine leans and
// turns, and the casting hand glows in the spell's colour. While Cast is
// held to charge, the wind-up is held, trembling harder with each level,
// and the cast plays on from it. Runs after the Animator and after SwimPose
// (LateUpdate), so it works on any humanoid without editing the animation
// controller (swimming too, without the dip). Every machine plays it for
// every caster, from the cast announcement; only the caster's own machine
// sees the charge. A cast by elements goes through CastBeats instead, which
// lays its poses with this class's limbs (LayBody, LayHand), and the gesture
// stands aside, leaving only its timing.
[DefaultExecutionOrder(200)]
[RequireComponent(typeof(Animator))]
public class CastPose : MonoBehaviour
{
    /// <summary>How long a released wind-up is held waiting for its cast (online, the announcement comes back from the host).</summary>
    const float HoldLinger = 0.6f;
    /// <summary>Shoulder height (m) the gestures are written for; the rig's own scales them.</summary>
    const float GestureShoulders = 1.45f;

    Animator animator;
    Transform hips, spine;
    LimbIK.Limb rightArm, leftArm, rightLeg, leftLeg;
    Light glow;
    SpellCaster caster;
    SwimPose swim;
    CastBeats beats;
    Transform rightPalm, leftPalm;
    // Each finger bone of a hand with its rest rotation: the open hand (LayHand).
    (Transform bone, Quaternion open)[] rightFingers, leftFingers;
    float scale = 1f;

    CastGestures.Gesture playing;
    Vector3 target;
    float started = -1f;
    bool wound, beatLed;
    // The wind-up a held Cast shows (the caster's own machine only).
    CastGesture held;
    float heldAt = -1f;

    /// <summary>The gesture playing now, or Auto when none is.</summary>
    public CastGesture Playing { get; private set; }

    void Awake()
    {
        animator = GetComponent<Animator>();
        TryGetComponent(out caster);
        TryGetComponent(out swim);
        TryGetComponent(out beats);
        hips = animator.GetBoneTransform(HumanBodyBones.Hips);
        spine = animator.GetBoneTransform(HumanBodyBones.Spine);
        rightArm = LimbIK.Limb.Of(animator, HumanBodyBones.RightUpperArm, HumanBodyBones.RightLowerArm, HumanBodyBones.RightHand);
        leftArm = LimbIK.Limb.Of(animator, HumanBodyBones.LeftUpperArm, HumanBodyBones.LeftLowerArm, HumanBodyBones.LeftHand);
        rightLeg = LimbIK.Limb.Of(animator, HumanBodyBones.RightUpperLeg, HumanBodyBones.RightLowerLeg, HumanBodyBones.RightFoot);
        leftLeg = LimbIK.Limb.Of(animator, HumanBodyBones.LeftUpperLeg, HumanBodyBones.LeftLowerLeg, HumanBodyBones.LeftFoot);
        if (!rightArm.Valid) return;
        float shoulders = rightArm.root.position.y - transform.position.y;
        if (shoulders > 0.5f && shoulders < 5f) scale = shoulders / GestureShoulders;
        rightPalm = animator.GetBoneTransform(HumanBodyBones.RightMiddleProximal);
        leftPalm = animator.GetBoneTransform(HumanBodyBones.LeftMiddleProximal);
        rightFingers = Fingers(HumanBodyBones.RightThumbProximal, HumanBodyBones.RightLittleDistal);
        leftFingers = Fingers(HumanBodyBones.LeftThumbProximal, HumanBodyBones.LeftLittleDistal);
        glow = new GameObject("Cast glow").AddComponent<Light>();
        glow.transform.SetParent(rightArm.end, false);
        glow.type = LightType.Point;
        glow.range = 3f;
        glow.enabled = false;
    }

    /// <summary>Where the other hand is (the second element's orb: CastOrbs).</summary>
    public Vector3 LeftHandPosition => leftArm.end != null ? leftArm.end.position : transform.position + Vector3.up * 1.2f - transform.right * 0.3f;

    /// <summary>The hand's position, where projectiles start; the chest if the rig has no hand.</summary>
    public Vector3 HandPosition => rightArm.end != null ? rightArm.end.position : transform.position + Vector3.up * 1.3f;

    /// <summary>The middle of each palm, where what the hand holds sits.</summary>
    public Vector3 RightPalm => rightPalm != null ? rightPalm.position : HandPosition;
    public Vector3 LeftPalm => leftPalm != null ? leftPalm.position : LeftHandPosition;

    /// <summary>The rig's size against the one the poses are written for.</summary>
    public float Scale => scale;

    /// <summary>Plays a cast's gesture toward <paramref name="at"/>, on from the wind-up if a held Cast was showing it.</summary>
    public void Play(CastGesture gesture, Vector3 at, Color color)
    {
        if (gesture == CastGesture.Auto) gesture = CastGesture.Reach;
        wound = heldAt >= 0f && held == gesture && Time.time - heldAt < HoldLinger;
        beatLed = beats != null && beats.Driving;
        heldAt = -1f;
        playing = CastGestures.Of(gesture);
        Playing = gesture;
        target = at;
        started = Time.time;
        if (glow == null) return;
        glow.color = color;
        glow.enabled = true;
    }

    void LateUpdate()
    {
        if (!rightArm.Valid) return;
        if (Charging()) return;
        if (started < 0f)
        {
            Linger();
            return;
        }
        float t = Time.time - started;
        if (t > playing.Duration)
        {
            started = -1f;
            beatLed = false;
            Playing = CastGesture.Auto;
            glow.enabled = false;
            return;
        }
        // Wound up by a held Cast: stay at the wind-up until the gesture would have reached it, so the release keeps its time.
        var key = playing.At(wound && t < playing.hold ? playing.hold : t);
        Apply(playing, key, 0f);
        glow.intensity = 4f * Mathf.Max(key.weight, key.leftWeight);
    }

    // While Cast is held to charge: into the selected spell's wind-up and held there, trembling with each level.
    bool Charging()
    {
        float seconds = caster != null ? caster.ChargingSeconds : -1f;
        if (seconds < 0f || started >= 0f) return false;
        held = caster.GestureOf(caster.SelectedId);
        if (held == CastGesture.Auto) held = CastGesture.Reach;
        heldAt = Time.time;
        target = transform.position + transform.forward * 6f;
        var gesture = CastGestures.Of(held);
        var key = gesture.At(Mathf.Min(seconds, gesture.hold));
        int charge = caster.Charge;
        Apply(gesture, key, 0.006f * charge * charge * scale);
        glow.color = caster.GlowOf(caster.SelectedId);
        glow.enabled = true;
        glow.intensity = (1.5f + 1.5f * charge) * key.weight;
        return true;
    }

    // Cast let go: the wind-up stays a moment for the cast to come back from the host.
    void Linger()
    {
        if (heldAt < 0f) return;
        if (Time.time - heldAt > HoldLinger)
        {
            heldAt = -1f;
            glow.enabled = false;
            return;
        }
        var gesture = CastGestures.Of(held);
        Apply(gesture, gesture.At(gesture.hold), 0f);
    }

    // The pose: the hips down (feet kept planted), the spine leaned and turned, then the hands.
    void Apply(CastGestures.Gesture gesture, CastGestures.Key key, float tremble)
    {
        if (beatLed || (beats != null && beats.Driving)) return;
        var forward = target - transform.position;
        forward.y = 0f;
        forward = forward.sqrMagnitude > 1e-4f ? forward.normalized : transform.forward;
        var right = Vector3.Cross(Vector3.up, forward);
        Dip(key.dip, forward);
        Bend(key.lean, key.twist, right);
        var aim = gesture.aims ? Aim(forward, right) : Quaternion.identity;
        Arm(rightArm, Frame(key.right, forward, right), key.weight, right, aim, Shake(tremble, 0f));
        Arm(leftArm, Frame(key.left, forward, right), key.leftWeight, -right, aim, Shake(tremble, 5f));
    }

    Vector3 Frame(Vector3 local, Vector3 forward, Vector3 right) =>
        transform.position + (right * local.x + Vector3.up * local.y + forward * local.z) * scale;

    // Pointing up or down at the target from the chest, as well as toward it (at most 50 degrees).
    Quaternion Aim(Vector3 forward, Vector3 right)
    {
        var chest = transform.position + Vector3.up * (GestureShoulders * scale);
        var to = Vector3.ProjectOnPlane(target - chest, right);
        if (to.sqrMagnitude < 1e-4f) return Quaternion.identity;
        return Quaternion.AngleAxis(Mathf.Clamp(Vector3.SignedAngle(forward, to, right), -50f, 50f), right);
    }

    void Arm(LimbIK.Limb arm, Vector3 hand, float weight, Vector3 outward, Quaternion aim, Vector3 shake)
    {
        if (weight <= 0f || !arm.Valid) return;
        var shoulder = arm.root.position;
        hand = shoulder + aim * (hand - shoulder) + shake;
        // The elbow out to the side, down when the hand is low and level with the shoulder when it is high.
        float raised = Mathf.Clamp01((hand.y - shoulder.y) / (0.5f * scale));
        var elbow = shoulder + (outward * 0.45f + Vector3.up * Mathf.Lerp(-0.3f, 0.1f, raised)) * scale;
        LimbIK.Solve(arm, hand, elbow, weight);
    }

    // The hips lowered and the legs bent to keep the feet where the animation put them; not while swimming.
    void Dip(float dip, Vector3 forward)
    {
        if (dip <= 0.001f || hips == null || (swim != null && swim.Weight > 0.1f)) return;
        if (!rightLeg.Valid || !leftLeg.Valid) return;
        Vector3 rightFoot = rightLeg.end.position, leftFoot = leftLeg.end.position;
        Quaternion rightTurn = rightLeg.end.rotation, leftTurn = leftLeg.end.rotation;
        hips.position += Vector3.down * (dip * scale);
        Plant(rightLeg, rightFoot, rightTurn, forward);
        Plant(leftLeg, leftFoot, leftTurn, forward);
    }

    void Plant(LimbIK.Limb leg, Vector3 foot, Quaternion turn, Vector3 forward)
    {
        LimbIK.Solve(leg, foot, leg.mid.position + forward * (0.5f * scale), 1f);
        leg.end.rotation = turn;
    }

    void Bend(float lean, float twist, Vector3 right)
    {
        if (spine == null || Mathf.Abs(lean) + Mathf.Abs(twist) < 0.01f) return;
        spine.rotation = Quaternion.AngleAxis(lean, right) * Quaternion.AngleAxis(twist, Vector3.up) * spine.rotation;
    }

    // --- for CastBeats ------------------------------------------------------------------

    /// <summary>Sinks the hips with the feet planted, leans the spine, then moves all of the body by <paramref name="shift"/>.</summary>
    public void LayBody(float dip, float lean, Vector3 forward, Vector3 shift)
    {
        if (!rightArm.Valid) return;
        Dip(dip, forward);
        Bend(lean, 0f, Vector3.Cross(Vector3.up, forward));
        if (hips != null) hips.position += shift;
    }

    /// <summary>The turn from level to the target, as a gesture that aims has.</summary>
    public Quaternion AimAt(Vector3 at, Vector3 forward, Vector3 right)
    {
        target = at;
        return Aim(forward, right);
    }

    /// <summary>The body's frame a hand is laid in: from a point, x right, y up, z ahead, turned by an aim, trembling (m).</summary>
    public readonly struct HandFrame
    {
        public readonly Vector3 origin, forward, right;
        public readonly Quaternion aim;
        public readonly float tremble;

        public HandFrame(Vector3 origin, Vector3 forward, Vector3 right, Quaternion aim, float tremble)
        {
            this.origin = origin;
            this.forward = forward;
            this.right = right;
            this.aim = aim;
            this.tremble = tremble;
        }
    }

    /// <summary>
    /// Puts a hand at a point of the body's frame (scaled), its fingers and palm turned as given (for the
    /// right hand; the left's are their mirror) and open. Returns where the hand was put.
    /// </summary>
    public Vector3 LayHand(bool isRight, in HandFrame body, Vector3 local, float weight, Vector3 fingers, Vector3 palm)
    {
        var arm = isRight ? rightArm : leftArm;
        var at = body.origin + (body.right * local.x + Vector3.up * local.y + body.forward * local.z) * scale;
        if (weight <= 0f || !arm.Valid) return at;
        var shoulder = arm.root.position;
        var aim = body.aim;
        var shake = Shake(body.tremble, isRight ? 0f : 5f);
        Arm(arm, at, weight, isRight ? body.right : -body.right, aim, shake);
        var frame = Quaternion.LookRotation(body.forward);
        float side = isRight ? 1f : -1f;
        Turn(arm.end, isRight, aim * frame * new Vector3(fingers.x * side, fingers.y, fingers.z), aim * frame * new Vector3(palm.x * side, palm.y, palm.z), weight);
        foreach (var (bone, open) in isRight ? rightFingers : leftFingers) bone.localRotation = Quaternion.Slerp(bone.localRotation, open, weight);
        return shoulder + aim * (at - shoulder) + shake;
    }

    // Turns the hand so the fingers and palm point the given ways, measured from the rig's own finger bones.
    void Turn(Transform hand, bool isRight, Vector3 fingers, Vector3 palm, float weight)
    {
        var middle = isRight ? rightPalm : leftPalm;
        var index = animator.GetBoneTransform(isRight ? HumanBodyBones.RightIndexProximal : HumanBodyBones.LeftIndexProximal);
        var little = animator.GetBoneTransform(isRight ? HumanBodyBones.RightLittleProximal : HumanBodyBones.LeftLittleProximal);
        if (middle == null || index == null || little == null) return;
        var along = middle.position - hand.position;
        var facing = Vector3.Cross(along, index.position - little.position) * (isRight ? 1f : -1f);
        if (along.sqrMagnitude < 1e-6f || facing.sqrMagnitude < 1e-8f || fingers.sqrMagnitude < 1e-6f || palm.sqrMagnitude < 1e-6f) return;
        var turn = Quaternion.LookRotation(fingers, palm) * Quaternion.Inverse(Quaternion.LookRotation(along, facing));
        hand.rotation = Quaternion.Slerp(hand.rotation, turn * hand.rotation, weight);
    }

    // A hand's finger bones with their rotation in the rig's T-pose (flat and open), from the avatar.
    (Transform, Quaternion)[] Fingers(HumanBodyBones firstBone, HumanBodyBones lastBone)
    {
        var rest = new System.Collections.Generic.Dictionary<string, Quaternion>();
        if (animator.avatar != null)
            foreach (var bone in animator.avatar.humanDescription.skeleton) rest[bone.name] = bone.rotation;
        var fingers = new System.Collections.Generic.List<(Transform, Quaternion)>();
        for (var b = firstBone; b <= lastBone; b++)
        {
            var bone = animator.GetBoneTransform(b);
            if (bone != null && rest.TryGetValue(bone.name, out var open)) fingers.Add((bone, open));
        }
        return fingers.ToArray();
    }

    // A tremble of the hands (m), each hand its own (seed).
    static Vector3 Shake(float amount, float seed)
    {
        if (amount <= 0f) return Vector3.zero;
        float t = Time.time * 16f;
        return 2f * amount * new Vector3(Mathf.PerlinNoise(t, seed) - 0.5f, Mathf.PerlinNoise(seed + 1.7f, t) - 0.5f, Mathf.PerlinNoise(t, seed + 3.1f) - 0.5f);
    }
}
