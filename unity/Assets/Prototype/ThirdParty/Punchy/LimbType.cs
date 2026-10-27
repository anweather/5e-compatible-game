// Adapted from Punchy by Zachary Franklin (https://github.com/Skeleman/Punchy),
// used with permission. Unchanged but for this header.
using System;
using System.Collections.Generic;
using UnityEngine;

namespace Punchy.Fauna
{
    public enum LimbRole : byte
    {
        [Tooltip("Touches the ground: its foot is placed on the terrain by IK, and its stride sets the gait speed.")] Leg,
        Arm,
        [Tooltip("Swings out as the creature turns (spring lag).")] Tail,
        Neck,
        [Tooltip("Turns towards whatever the creature is looking at.")] Head,
        [Tooltip("Flicks with a little spring lag.")] Ear,
        Horn,
        Eye,
        [Tooltip("A static body feature: hump, mane, stripe, antler tine, shell ridge…")] Feature
    }

    /// <summary>
    /// A kind of body part: a chain of tapered segments (thigh, shin, foot; or tail pieces; or a single head), and a
    /// handful of key poses. Animation interpolates between the keys by gait phase, so a whole walk cycle is three
    /// poses (reach, push, lift) and a run another three; idle is one more. Parts are mixed and matched on creatures
    /// (CreatureSpecies), so one leg type can carry a deer, a goat and anything else built from it.
    /// </summary>
    [CreateAssetMenu(menuName = "Punchy/Limb Type", fileName = "Limb")]
    public class LimbType : ScriptableObject
    {
        public LimbRole Role = LimbRole.Leg;
        [Tooltip("From the root outwards. Each segment points along its own forward axis.")]
        public List<LimbSegment> Segments = new List<LimbSegment>();
        [Tooltip("How strongly the part lags and swings with the body's turns and bobs (tails, ears).")]
        [Range(0f, 1f)] public float Spring;
        [Tooltip("Each joint runs this far behind the one before it in the gait cycle (0..1), so a ripple travels " +
                 "down the chain: a snake's body, a swishing tail.")]
        [Range(0f, 0.5f)] public float JointPhaseStep;
        [Tooltip("Each segment bends to lie on the ground (snake bodies, dragging tails).")]
        public bool FollowGround;

        [Header("Key poses: joint angles in degrees (x pitch, y yaw, z roll), one per segment")]
        [Tooltip("Standing still.")]
        public LimbPose Idle = new LimbPose();
        [Tooltip("Walking: the part at its furthest forward (foot touching down).")]
        public LimbPose WalkReach = new LimbPose();
        [Tooltip("Walking: the part at its furthest back (foot pushing off).")]
        public LimbPose WalkPush = new LimbPose();
        [Tooltip("Walking: added mid-swing, as the part is carried forward (lifting the foot).")]
        public LimbPose WalkLift = new LimbPose();
        public LimbPose RunReach = new LimbPose();
        public LimbPose RunPush = new LimbPose();
        public LimbPose RunLift = new LimbPose();
    }

    [Serializable]
    public class LimbSegment
    {
        [Min(0f)] public float Length = 0.2f;
        [Min(0.001f)] public float RadiusStart = 0.04f;
        [Min(0f)] public float RadiusEnd = 0.03f;
        [Tooltip("Height of the cross-section relative to its width (below 1 = flat, like a paw, ear or tail fan).")]
        [Range(0.05f, 3f)] public float Flatten = 1f;
        [Tooltip("0 = the creature's base colour, 1 = its accent colour.")]
        [Range(0f, 1f)] public float Accent;
        [Tooltip("Use this colour instead (hooves, claws, eyes, beaks).")]
        public bool UseFixedColor;
        public Color FixedColor = new Color(0.1f, 0.08f, 0.07f);
        [Tooltip("Glows by itself (eye sockets, embers, toxic markings).")]
        [Range(0f, 1f)] public float Glow;
    }

    [Serializable]
    public class LimbPose
    {
        public List<Vector3> Joints = new List<Vector3>();

        public Vector3 this[int i] => i < Joints.Count ? Joints[i] : Vector3.zero;

        public static LimbPose Of(params Vector3[] joints) => new LimbPose { Joints = new List<Vector3>(joints) };
    }
}
