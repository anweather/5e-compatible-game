using System;
using UnityEngine;

// Every pose and timing of the cast's beats (CastBeats, docs/SPELL_COMBOS.md),
// in one asset to adjust by feel: Assets/Prototype/Settings/CastFeel.asset.
// Changes show at once in Play mode. A beat's pose is where the hands go, in
// metres from between the feet (x right, y up, z ahead, the rig's size scaling
// it, as in CastGestures), with how the palm faces and what the body does; the
// beat moves there over its seconds along its curve from wherever it was. The
// impact (CastImpact, CastCamera, PadRumble) is here too; the player's Shake and
// rumble setting (GameFeel) scales all of that, 100% being these numbers.
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
        [Tooltip("The shoulders' turn to the right (degrees): a punch's; a cast's have none.")]
        public float twist;
        [Tooltip("How far the body is pushed back from the aim (m). Only drawn: the wizard itself stays put.")]
        public float recoil;
        [Range(0f, 1f), Tooltip("0: each element in its hand. 1: gathered between the hands.")]
        public float merge;
        [Tooltip("How long the move into this pose takes (s).")]
        public float seconds = 0.2f;
        [Tooltip("The move's shape, 0 to 1. Starting steep is no ease-in.")]
        public AnimationCurve curve = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);
    }

    /// <summary>A pulse on the pad: each motor's strength (0 to 1) and how long it takes to die away.</summary>
    [Serializable]
    public struct Thump
    {
        [Range(0f, 1f), Tooltip("The heavy motor, the light one.")]
        public float low, high;
        public float seconds;

        public Thump(float low, float high, float seconds)
        {
            this.low = low;
            this.high = high;
            this.seconds = seconds;
        }
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

    [Header("The uncharged cast: read by a tap only, so these can't touch a charged cast")]
    [Tooltip("The anticipation: the arms high and wide, the body rearing back. It starts windUpTicks from the end of the meter.")]
    public BeatPose windUp = new()
    {
        rightHand = new Vector3(0.5f, 1.38f, -0.1f), leftHand = new Vector3(-0.5f, 1.38f, -0.1f), seconds = 0.2f,
        fingers = new Vector3(0.3f, 1f, -0.2f), palm = Vector3.left, lean = -7f,
    };
    [Tooltip("A tap's slam: down from the wind-up, heavier and further than a hold's, no ease-in.")]
    public BeatPose tapSlam = new()
    {
        rightHand = new Vector3(0.03f, 1.14f, 0.4f), leftHand = new Vector3(-0.03f, 1.14f, 0.4f), seconds = 0.13f, curve = Snap(),
        fingers = new Vector3(0f, 1f, 0.4f), palm = Vector3.left, dip = 0.13f, lean = 12f, merge = 1f,
    };
    [Tooltip("A tap's thrust.")]
    public BeatPose tapRelease = new()
    {
        rightHand = new Vector3(0.05f, 1.34f, 0.41f), leftHand = new Vector3(-0.05f, 1.34f, 0.41f), seconds = 0.15f, curve = Snap(),
        fingers = new Vector3(0f, 1f, 0.3f), palm = Vector3.forward, lean = -10f, recoil = 0.45f, merge = 1f,
    };
    [Min(0f), Tooltip("A hand's tap (hold.json hands.tapTicks) goes off two ticks after its press, leaving about a tick for the wind-up: it plays the windUp pose in this long instead (s), so the arms are up before the slam. 0: windUp's own seconds.")]
    public float handTapWindUpSeconds = 0.1f;
    [Min(1), Tooltip("A tap's wind-up starts with this many ticks of the meter left (a tick is 1/6 s). With slamTicks or fewer left it's skipped, for a cast on the heels of the last.")]
    public int windUpTicks = 3;
    [Tooltip("Like thrustLead and thrustHold, for a tap.")]
    public float tapThrustLead = 0.15f, tapThrustHold = 0.11f;
    [Tooltip("Like slamStop and releaseStop, for a tap: 5 frames and 4 at 60 fps.")]
    public float tapSlamStop = 0.084f, tapReleaseStop = 0.067f;
    [Tooltip("Like slamKick and slamKickSeconds, for a tap.")]
    public float tapSlamKick = 0.09f, tapSlamKickSeconds = 0.2f;
    public Thump tapSlamRumble = new(0.8f, 0.45f, 0.14f);

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

    [Header("Hit-stop (this wizard's animation and the camera, never the game)")]
    [Tooltip("How long the slam and the release stand still (s). A 60 fps frame is 0.0167 s: two frames and three.")]
    public float slamStop = 0.034f, releaseStop = 0.05f;
    [Tooltip("How long the camera takes to catch up after a stop (s), so a wizard walking doesn't make it jump.")]
    public float thawSeconds = 0.05f;

    [Header("Camera impulses (m at the lens, short and sharp)")]
    [Tooltip("The slam's kick, down.")]
    public float slamKick = 0.05f;
    public float slamKickSeconds = 0.14f;
    [Tooltip("The release's kick along the cast, from a spell of size 0 to one of size 1.")]
    public float releaseKickSmall = 0.09f, releaseKickBig = 0.24f;
    public float releaseKickSeconds = 0.2f;
    [Tooltip("How much it also kicks up: along the view alone a kick barely shows.")]
    public float releaseLift = 0.6f;
    [Tooltip("A landing's kick, right beside it, from size 0 to size 1.")]
    public float landKickSmall = 0.05f, landKickBig = 0.3f;
    public float landKickSeconds = 0.3f;
    [Tooltip("How far a landing is felt (m), from size 0 to size 1. It falls off with the square of the way there.")]
    public float reachSmall = 14f, reachBig = 60f;

    [Header("How big a spell is")]
    [Tooltip("The spell level that counts as full size, and how many levels each charge past the first adds.")]
    public float fullSizeLevel = 7f, levelsPerCharge = 2f;

    [Header("Charging")]
    [Tooltip("The lens's tightening at full charge (degrees), and how long it takes to come and go (s).")]
    public float chargeTighten = 3f, chargeEase = 0.3f;
    [Tooltip("The fine shake at full charge (m), and how quick it is. It grows with the square of the charge.")]
    public float chargeShake = 0.016f, chargeShakeHertz = 9f;
    [Tooltip("After this long in the charge (s) the shake eases over a second to this share, so a held cast stays comfortable.")]
    public float chargeShakeTires = 3f, chargeShakeHeld = 0.4f;

    [Header("The punch (PunchPose, ItemHolder.Punch.cs, CastImpact): the fist lands at the end of punchDraw and punchOut")]
    [Tooltip("How hard it hits a prop or item, in pushes (Forces.ImpulsePerPush): 1 sends a crate off at 2.5 m/s. Online the host takes it, up to NetProp.MaxKick.")]
    public float punchShove = 2.5f;
    [Tooltip("The fist drawn back and the shoulders turned away.")]
    public BeatPose punchDraw = new()
    {
        rightHand = new Vector3(0.24f, 1.16f, -0.1f), leftHand = new Vector3(-0.2f, 1.3f, 0.26f), leftWeight = 0.8f, seconds = 0.09f,
        fingers = Vector3.forward, palm = Vector3.left, dip = 0.04f, lean = -3f, twist = 24f,
    };
    [Tooltip("The fist out and the shoulders through. rightHand is for a miss: a hit goes to what it hits. A negative recoil is the body going forward.")]
    public BeatPose punchOut = new()
    {
        rightHand = new Vector3(0.1f, 1.32f, 0.72f), leftHand = new Vector3(-0.22f, 1.22f, 0.16f), leftWeight = 0.8f, seconds = 0.07f, curve = Snap(),
        fingers = Vector3.forward, palm = Vector3.down, dip = 0.05f, lean = 10f, recoil = -0.14f, twist = -34f,
    };
    [Tooltip("Back to neutral: the weights fall to nothing, handing the arms back to the animation.")]
    public BeatPose punchBack = new()
    {
        rightHand = new Vector3(0.26f, 1f, 0.12f), leftHand = new Vector3(-0.26f, 1f, 0.1f), rightWeight = 0f, leftWeight = 0f, seconds = 0.22f, curve = Snap(),
        fingers = Vector3.forward, palm = Vector3.left,
    };
    [Tooltip("How long the fist stays out (s), and from one punch to the next.")]
    public float punchHold = 0.05f, punchCooldown = 0.4f;
    [Tooltip("How long a hit stands still (s); only a hit. 3 frames at 60 fps.")]
    public float punchStop = 0.05f;
    [Tooltip("The camera's kick along the punch on a hit (m), and how long it takes to die away (s).")]
    public float punchKick = 0.035f, punchKickSeconds = 0.12f;
    public Thump punchRumble = new(0.6f, 0.4f, 0.1f);
    [Tooltip("From the hand bone to the knuckles (m).")]
    public float punchFist = 0.09f;
    [Tooltip("The most the body goes after something beyond the arm (m).")]
    public float punchLunge = 0.25f;
    [Tooltip("The most the shoulders turn to something off to the side (degrees).")]
    public float punchTurnMost = 45f;
    [Tooltip("How far each finger joint curls (degrees).")]
    public float punchClench = 70f;
    [Tooltip("A target lower than this (m) bends the body to it, and at the ground by this much more lean (degrees) and dip (m).")]
    public float punchLowFrom = 1f, punchLowLean = 22f, punchLowDip = 0.14f;
    [Tooltip("How long a strike's knock-back takes the wizard struck to its new cell (s).")]
    public float punchKnockSeconds = 0.15f;
    [Range(0f, 1f), Tooltip("How big being struck feels, as a spell's size for CastImpact.Felt.")]
    public float punchFelt = 0.15f;

    [Header("Rumble (pulses, never a drone)")]
    public Thump gatherRumble = new(0f, 0.22f, 0.06f);
    public Thump slamRumble = new(0.55f, 0.35f, 0.1f);
    [Tooltip("At full charge; lower levels get their share.")]
    public Thump levelRumble = new(0.3f, 0.5f, 0.09f);
    [Tooltip("At size 1; smaller spells get a share, from smallRumble up.")]
    public Thump releaseRumble = new(0.85f, 0.6f, 0.16f);
    [Tooltip("Right beside a landing of size 1.")]
    public Thump landRumble = new(1f, 0.6f, 0.24f);
    [Range(0f, 1f), Tooltip("The share of a release's or landing's rumble that a spell of size 0 gets.")]
    public float smallRumble = 0.45f;

    /// <summary>The pose a beat moves to (Neutral: as Recovery's, nothing is held).</summary>
    public BeatPose Of(Beat beat) => beat switch
    {
        Beat.First => first,
        Beat.Second => second,
        Beat.WindUp => windUp,
        Beat.Slam => slam,
        Beat.Charge => charge,
        Beat.Release => release,
        Beat.Fizzle => fizzle,
        _ => recovery,
    };

    /// <summary>The pose a beat moves to in a tap's cast or any other.</summary>
    public BeatPose Of(Beat beat, bool tap) => !tap ? Of(beat) : beat switch
    {
        Beat.Slam => tapSlam,
        Beat.Release => tapRelease,
        _ => Of(beat),
    };

    /// <summary>How long a thrust starts ahead of the spell leaving the hands (s).</summary>
    public float LeadOf(bool tap) => tap ? tapThrustLead : thrustLead;

    /// <summary>How long a thrust is held at its end (s).</summary>
    public float HoldOf(bool tap) => tap ? tapThrustHold : thrustHold;
}
