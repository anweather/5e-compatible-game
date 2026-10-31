using System;
using UnityEngine;

// Every pose and timing of the cast's beats (CastBeats, docs/SPELL_COMBOS.md),
// in one asset to adjust by feel: Assets/Prototype/Settings/CastFeel.asset.
// Changes show at once in Play mode. A beat's pose is where the hands go, in
// metres from between the feet (x right, y up, z ahead, the rig's size scaling
// it, as in CastGestures), with how the palm faces and what the body does; the
// beat moves there over its seconds along its curve from wherever it was.
[CreateAssetMenu(menuName = "Prototype/Cast Feel")]
public class CastFeel : ScriptableObject
{
    [Serializable]
    public class BeatPose
    {
        [Tooltip("Where each hand goes (m, the hand bone).")]
        public Vector3 rightHand, leftHand;
        [Range(0f, 1f), Tooltip("How much each arm follows this pose over the animation; 0 leaves it to the animation.")]
        public float rightWeight = 1f, leftWeight = 1f;
        [Tooltip("The way the right hand's fingers point and its palm faces; the left hand's are the mirror of them.")]
        public Vector3 fingers = Vector3.down, palm = Vector3.forward;
        [Tooltip("How far the hips sink, knees bent and feet planted (m), and the spine's lean ahead (degrees).")]
        public float dip, lean;
        [Tooltip("How far the body is pushed back from the aim (m). Only drawn: the wizard itself stays put.")]
        public float recoil;
        [Range(0f, 1f), Tooltip("0: each element in its hand. 1: gathered between the hands.")]
        public float merge;
        [Tooltip("How long the move into this pose takes (s).")]
        public float seconds = 0.2f;
        [Tooltip("The move's shape, 0 to 1. Starting steep is no ease-in.")]
        public AnimationCurve curve = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);
    }

    static AnimationCurve Snap() => new(new Keyframe(0f, 0f, 3.5f, 3.5f), new Keyframe(1f, 1f, 0f, 0f));

    [Header("The six beats, and a cancel")]
    public BeatPose first = new()
    {
        rightHand = new Vector3(0.42f, 0.95f, -0.2f), leftWeight = 0f, seconds = 0.26f,
        fingers = new Vector3(0.25f, -1f, 0f), palm = Vector3.forward, dip = 0.02f, lean = -2f,
    };
    public BeatPose second = new()
    {
        rightHand = new Vector3(0.46f, 0.98f, -0.14f), leftHand = new Vector3(-0.46f, 0.98f, -0.14f), seconds = 0.22f,
        fingers = new Vector3(0.25f, -1f, 0f), palm = Vector3.forward, dip = 0.06f, lean = -3f,
    };
    public BeatPose slam = new()
    {
        rightHand = new Vector3(0.03f, 1.2f, 0.36f), leftHand = new Vector3(-0.03f, 1.2f, 0.36f), seconds = 0.1f, curve = Snap(),
        fingers = new Vector3(0f, 1f, 0.4f), palm = Vector3.left, dip = 0.05f, lean = 6f, merge = 1f,
    };
    public BeatPose charge = new()
    {
        rightHand = new Vector3(0.03f, 1.06f, 0.36f), leftHand = new Vector3(-0.03f, 1.06f, 0.36f), seconds = 0.25f,
        fingers = new Vector3(0f, 1f, 0.5f), palm = Vector3.left, dip = 0.16f, lean = 14f, merge = 1f,
    };
    public BeatPose release = new()
    {
        rightHand = new Vector3(0.05f, 1.34f, 0.4f), leftHand = new Vector3(-0.05f, 1.34f, 0.4f), seconds = 0.09f, curve = Snap(),
        fingers = new Vector3(0f, 1f, 0.3f), palm = Vector3.forward, lean = -7f, recoil = 0.35f, merge = 1f,
    };
    [Tooltip("Back to neutral: the weights fall to nothing, handing the arms back to the animation.")]
    public BeatPose recovery = new()
    {
        rightHand = new Vector3(0.28f, 0.95f, 0.1f), leftHand = new Vector3(-0.28f, 0.95f, 0.1f), rightWeight = 0f, leftWeight = 0f, seconds = 0.24f, curve = Snap(),
    };
    [Tooltip("A cancelled cast: the hands drop.")]
    public BeatPose fizzle = new()
    {
        rightHand = new Vector3(0.3f, 0.8f, 0.05f), leftHand = new Vector3(-0.3f, 0.8f, 0.05f), rightWeight = 0f, leftWeight = 0f, seconds = 0.3f,
    };

    [Header("Timing")]
    [Min(1), Tooltip("A tap's slam starts with this many ticks of the meter left (a tick is 1/6 s), so the hands meet as the cast goes off.")]
    public int slamTicks = 1;
    [Tooltip("The thrust starts this long before the spell leaves the hands (s), so it arrives with the spell.")]
    public float thrustLead = 0.08f;
    [Tooltip("The thrust is held at its end this long before the recovery (s): a stop, to read as a blow.")]
    public float thrustHold = 0.07f;
    [Tooltip("How long hands stay together for a fired cast's announcement before it counts as a fizzle (s). Online it comes back from the host.")]
    public float announceWait = 1.2f;

    [Header("The energy between the hands")]
    [Tooltip("The orbs' growth for each charge level past the first.")]
    public float growPerLevel = 0.6f;
    [Tooltip("The extra size of the kick at the slam and at each charge level, and how long it takes to die away (s).")]
    public float pulse = 0.9f, pulseSeconds = 0.2f;
    [Tooltip("The hands' tremble while charging (m), times the level squared.")]
    public float tremble = 0.004f;

    /// <summary>The pose a beat moves to (Neutral: as Recovery's, nothing is held).</summary>
    public BeatPose Of(Beat beat) => beat switch
    {
        Beat.First => first,
        Beat.Second => second,
        Beat.Slam => slam,
        Beat.Charge => charge,
        Beat.Release => release,
        Beat.Fizzle => fizzle,
        _ => recovery,
    };
}
