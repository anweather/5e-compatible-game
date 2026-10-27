// Adapted from Punchy by Zachary Franklin (https://github.com/Skeleman/Punchy),
// used with permission. From Punchy's CreatureData.cs: the Entities blob is a
// plain class with arrays, so it runs without Entities; the maths is his.
// Kept close to Punchy, so our style analyzers are off here.
#pragma warning disable S3776, S3973, S2234, UNT0024
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Unity.Mathematics;
using UnityEngine;

namespace Punchy.Fauna
{
    /// <summary>
    /// A species flattened for posing: every part (mirrored slots expanded into left and right copies, parents before
    /// children), every segment (index 0 is the body), and every segment's seven key poses.
    /// </summary>
    public sealed class CreatureBody
    {
        public const int PoseCount = 7;   // idle, walk reach/push/lift, run reach/push/lift
        public const int Idle = 0, WalkReach = 1, WalkPush = 2, WalkLift = 3, RunReach = 4, RunPush = 5, RunLift = 6;

        public float BodyLength, RearRadius, FrontRadius, BodyFlatten;
        public float WalkSpeed, RunSpeed;
        /// <summary>Distance the body travels per gait cycle, walking and running (from the key poses).</summary>
        public float WalkStride, RunStride;
        /// <summary>Height of the body's centre above the ground when standing (from the idle pose).</summary>
        public float StandHeight;
        public float2 Bob, BobsPerCycle;
        public float RunRock;
        /// <summary>Keeps the body level rather than tilting with the ground (two-legged walkers).</summary>
        public bool Upright;
        public float WanderRadius, FleeDistance, TurnRate;
        public float2 IdleTime, Size;
        public ActiveTime Active;
        public float Grazing, Thirst, Playfulness, Laziness, Hunger, ChaseTime;
        /// <summary>Species (indices in the caller's list) it hunts.</summary>
        public int[] Prey = Array.Empty<int>();
        /// <summary>Whether anything hunts it (so it needs to watch out for predators).</summary>
        public bool Hunted;

        public CreaturePart[] Parts;
        public CreatureSegment[] Segments;
        public float3[] Poses;   // Segments.Length × PoseCount, radians
        /// <summary>Colourings individuals choose between (at least one).</summary>
        public CreatureCoatData[] Coats;

        /// <summary>A segment's colour in one of the coats.</summary>
        public float4 SegmentColor(int segment, int coat)
        {
            // Alpha carries how much the segment glows.
            var seg = Segments[segment];
            var c = Coats[math.clamp(coat, 0, Coats.Length - 1)];
            var glow = seg.Glow + (seg.Fixed != 0 ? 0f : c.AccentGlow * seg.Accent);
            var rgb = seg.Fixed != 0 ? seg.FixedColor.xyz : math.lerp(c.Base.xyz, c.Accent.xyz, seg.Accent);
            return new float4(rgb, math.saturate(glow));
        }

        /// <summary>Picks a coat by weight from a random number in 0..1.</summary>
        public int PickCoat(float random)
        {
            var total = 0f;
            for (int i = 0; i < Coats.Length; i++)
                total += Coats[i].Weight;
            var pick = random * total;
            for (int i = 0; i < Coats.Length; i++)
            {
                pick -= Coats[i].Weight;
                if (pick <= 0f)
                    return i;
            }
            return Coats.Length - 1;
        }

        /// <summary>Blend of a part's key poses for one segment at gait phase p (0..1).</summary>
        public float3 Angles(int segment, float p, float move, float run)
        {
            var i = segment * PoseCount;
            var walk = Cycle(Poses[i + WalkReach], Poses[i + WalkPush], Poses[i + WalkLift], p);
            var running = Cycle(Poses[i + RunReach], Poses[i + RunPush], Poses[i + RunLift], p);
            return math.lerp(Poses[i + Idle], math.lerp(walk, running, run), move);
        }

        /// <summary>
        /// A gait cycle from three keys: the first half sweeps steadily from reach to push (the foot planted, the
        /// body passing over it); the second half eases back to reach with the lift added in the middle (the foot
        /// carried forward off the ground).
        /// </summary>
        public static float3 Cycle(float3 reach, float3 push, float3 lift, float p)
        {
            if (p < 0.5f)
                return math.lerp(reach, push, p * 2f);
            var t = (p - 0.5f) * 2f;
            return math.lerp(push, reach, math.smoothstep(0f, 1f, t)) + lift * math.sin(t * math.PI);
        }

        struct Expanded
        {
            public LimbSlot Slot;
            public bool Mirrored;
            public int Parent;
        }

        /// <summary>
        /// Builds a species. A variant takes its body, parts, gait and behaviour from the species it is a variant
        /// of, and only its own colours and size.
        /// </summary>
        /// <param name="prey">Indices of the species it hunts.</param>
        /// <param name="hunted">Whether any species hunts it.</param>
        public static CreatureBody Build(CreatureSpecies variant, IReadOnlyList<int> prey = null, bool hunted = false)
        {
            var species = variant.Body;
            // Expand slots: mirrored slots become left and right copies; children follow every copy of their parent.
            var parts = new List<Expanded>();
            var copies = new List<List<int>>();
            for (int i = 0; i < species.Parts.Count; i++)
            {
                var slot = species.Parts[i];
                var mine = new List<int>();
                copies.Add(mine);
                if (slot.Limb == null)
                    continue;
                var parents = slot.Parent >= 0 && slot.Parent < i ? copies[slot.Parent] : new List<int> { -1 };
                foreach (var parent in parents)
                {
                    var parentMirrored = parent >= 0 && parts[parent].Mirrored;
                    mine.Add(parts.Count);
                    parts.Add(new Expanded { Slot = slot, Mirrored = parentMirrored, Parent = parent });
                    if (slot.Mirror)
                    {
                        mine.Add(parts.Count);
                        parts.Add(new Expanded { Slot = slot, Mirrored = !parentMirrored, Parent = parent, });
                    }
                }
            }

            var segmentCount = 1;
            foreach (var p in parts)
                segmentCount += p.Slot.Limb.Segments.Count;

            var root = new CreatureBody
            {
                BodyLength = species.BodyLength,
                FrontRadius = species.BodyRadius,
                RearRadius = species.BodyRadius * species.BodyTaper,
                BodyFlatten = species.BodyFlatten,
                WalkSpeed = species.WalkSpeed,
                RunSpeed = math.max(species.RunSpeed, species.WalkSpeed * 1.3f),
                Bob = new float2(species.Bob.x, species.Bob.y),
                BobsPerCycle = new float2(species.BobsPerCycle.x, species.BobsPerCycle.y),
                RunRock = math.radians(species.RunRock),
                Upright = species.Upright,
                WanderRadius = species.WanderRadius,
                FleeDistance = species.FleeDistance,
                TurnRate = math.radians(species.TurnRate),
                IdleTime = new float2(species.IdleTime.x, species.IdleTime.y),
            };
            var sizeScale = variant != species ? variant.VariantScale : 1f;
            root.Size = new float2(species.Size.x, species.Size.y) * sizeScale;
            root.Active = variant.Active;
            root.Grazing = variant.Grazing;
            root.Thirst = variant.Thirst;
            root.Playfulness = variant.Playfulness;
            root.Laziness = variant.Laziness;
            root.Hunger = variant.Hunger;
            root.ChaseTime = variant.ChaseTime;
            root.Hunted = hunted;
            root.Prey = prey != null ? new List<int>(prey).ToArray() : Array.Empty<int>();

            root.Parts = new CreaturePart[parts.Count];
            root.Segments = new CreatureSegment[segmentCount];
            root.Poses = new float3[segmentCount * PoseCount];

            // Coats come from the variant (or the species itself): its listed coats, or just Base and Accent.
            var coats = new List<CreatureCoat>(variant.Coats);
            if (coats.Count == 0)
                coats.Add(new CreatureCoat { Base = variant.Base, Accent = variant.Accent, AccentGlow = variant.AccentGlow, Weight = 1f });
            root.Coats = new CreatureCoatData[coats.Count];
            for (int i = 0; i < coats.Count; i++)
                root.Coats[i] = new CreatureCoatData
                {
                    Base = (Vector4)coats[i].Base.linear,
                    Accent = (Vector4)coats[i].Accent.linear,
                    AccentGlow = coats[i].AccentGlow,
                    Weight = math.max(coats[i].Weight, 0.0001f)
                };

            root.Segments[0] = new CreatureSegment
            {
                Length = math.max(0f, species.BodyLength - root.RearRadius - root.FrontRadius),
                RadiusStart = root.RearRadius,
                RadiusEnd = root.FrontRadius,
                Flatten = species.BodyFlatten
            };

            var cursor = 1;
            for (int i = 0; i < parts.Count; i++)
            {
                var e = parts[i];
                var slot = e.Slot;
                var limb = slot.Limb;
                var side = e.Mirrored ? -1f : 1f;
                var socket = (float3)slot.Socket;
                if (e.Parent < 0)
                {
                    // Body units → metres: across the radius at that point along the body, along the half-length.
                    var radius = math.lerp(root.RearRadius, root.FrontRadius, socket.z * 0.5f + 0.5f);
                    socket = new float3(socket.x * radius, socket.y * radius * species.BodyFlatten, socket.z * species.BodyLength * 0.5f);
                }
                socket.x *= side;
                var dir = (float3)slot.Direction;
                root.Parts[i] = new CreaturePart
                {
                    Parent = e.Parent,
                    Socket = socket,
                    Rest = Rotation(dir, side),
                    Phase = math.frac(slot.Phase + (e.Mirrored && slot.Mirror ? slot.MirrorPhase : 0f)),
                    FirstSegment = cursor,
                    SegmentCount = limb.Segments.Count,
                    Role = limb.Role,
                    Spring = limb.Spring,
                    JointPhaseStep = limb.JointPhaseStep,
                    FollowGround = limb.FollowGround,
                    Side = side
                };

                var poses = new[] { limb.Idle, limb.WalkReach, limb.WalkPush, limb.WalkLift, limb.RunReach, limb.RunPush, limb.RunLift };
                for (int s = 0; s < limb.Segments.Count; s++)
                {
                    var seg = limb.Segments[s];
                    root.Segments[cursor] = new CreatureSegment
                    {
                        Length = seg.Length * slot.Scale,
                        RadiusStart = seg.RadiusStart * slot.Scale * slot.Thickness,
                        RadiusEnd = seg.RadiusEnd * slot.Scale * slot.Thickness,
                        Flatten = seg.Flatten,
                        Accent = seg.Accent,
                        Fixed = seg.UseFixedColor ? 1 : 0,
                        FixedColor = (Vector4)seg.FixedColor.linear,
                        Glow = seg.Glow
                    };
                    for (int k = 0; k < PoseCount; k++)
                    {
                        var a = math.radians((float3)poses[k][s]);
                        root.Poses[cursor * PoseCount + k] = new float3(a.x, a.y * side, a.z * side);
                    }
                    cursor++;
                }
            }

            MeasureGait(root, species.LeglessStride);
            return root;
        }

        /// <summary>Rest orientation from degrees; the mirror image flips yaw and roll.</summary>
        public static quaternion Rotation(float3 degrees, float side)
        {
            var r = math.radians(degrees);
            return quaternion.EulerZXY(r.x, r.y * side, r.z * side);
        }

        /// <summary>
        /// Works out from the key poses how high the body stands and how far it travels per gait cycle, so the feet
        /// don't slide: in a cycle a planted foot sweeps back from reach to push while the body passes over it, so
        /// the body moves twice that sweep per cycle.
        /// </summary>
        static void MeasureGait(CreatureBody b, Vector2 leglessStride)
        {
            float stand = 0f, walk = 0f, run = 0f;
            var legs = 0;
            for (int i = 0; i < b.Parts.Length; i++)
            {
                var part = b.Parts[i];
                if (part.Role != LimbRole.Leg || part.Parent >= 0)
                    continue;
                stand += -Foot(b, part, Idle).y;
                walk += math.abs(Foot(b, part, WalkReach).z - Foot(b, part, WalkPush).z) * 2f;
                run += math.abs(Foot(b, part, RunReach).z - Foot(b, part, RunPush).z) * 2f;
                legs++;
            }
            b.StandHeight = legs > 0 ? stand / legs : b.FrontRadius * b.BodyFlatten;
            b.WalkStride = legs > 0 ? math.max(walk / legs, 0.05f) : math.max(leglessStride.x, 0.05f);
            b.RunStride = legs > 0 ? math.max(run / legs, 0.05f) : math.max(leglessStride.y, 0.05f);
        }

        /// <summary>End of a body-attached part's chain in the body frame, at one key pose.</summary>
        static float3 Foot(CreatureBody b, CreaturePart part, int pose)
        {
            var pos = part.Socket;
            var rot = part.Rest;
            for (int s = 0; s < part.SegmentCount; s++)
            {
                var seg = part.FirstSegment + s;
                var a = b.Poses[seg * PoseCount + pose];
                rot = math.mul(rot, quaternion.EulerZXY(a));
                pos += math.mul(rot, new float3(0f, 0f, b.Segments[seg].Length));
            }
            // The last segment's rounded end reaches a little further down.
            return pos + new float3(0f, -b.Segments[part.FirstSegment + part.SegmentCount - 1].RadiusEnd, 0f);
        }
    }

    public struct CreaturePart
    {
        /// <summary>-1 = the body, otherwise an earlier part this one hangs from the end of.</summary>
        public int Parent;
        /// <summary>Attachment point in metres, in the parent's frame (body frame for the body).</summary>
        public float3 Socket;
        public quaternion Rest;
        public float Phase;
        public int FirstSegment, SegmentCount;
        public LimbRole Role;
        public float Spring;
        /// <summary>+1, or -1 for the mirror image (yaw and roll flipped).</summary>
        public float Side;
        /// <summary>Phase lag per joint down the chain (a travelling ripple).</summary>
        public float JointPhaseStep;
        /// <summary>Segments bend to lie on the ground.</summary>
        public bool FollowGround;
    }

    public struct CreatureSegment
    {
        public float Length, RadiusStart, RadiusEnd, Flatten;
        /// <summary>Mix from the coat's base colour (0) to its accent (1).</summary>
        public float Accent;
        /// <summary>Non-zero: always FixedColor, whatever the coat (hooves, eyes, beaks).</summary>
        public int Fixed;
        public float4 FixedColor;   // linear
        public float Glow;
    }

    public struct CreatureCoatData
    {
        public float4 Base, Accent;   // linear
        public float AccentGlow;
        public float Weight;
    }

    public enum CreatureMode : byte
    {
        Idle,
        Wander,
        Flee,
        /// <summary>Under outside control: keeps its heading and speed, ignores the player (scripts, tests).</summary>
        Puppet,
        /// <summary>Head down, feeding on the spot.</summary>
        Graze,
        /// <summary>Walks to the water's edge (stage 0), then drinks (stage 1).</summary>
        Drink,
        /// <summary>Lying down, awake.</summary>
        Rest,
        /// <summary>Lying curled up, head down; slow to notice danger.</summary>
        Sleep,
        /// <summary>Chase with another of its kind (Partner): stage 0 chasing, stage 1 running away; a tag swaps them.</summary>
        Play,
        /// <summary>Chasing prey (Partner): stalking low, then sprinting.</summary>
        Hunt
    }

    /// <summary>A creature's position, heading, gait and what it's up to.</summary>
    public struct CreatureState
    {
        public float3 Position;
        /// <summary>Heading in radians; forward is (sin, 0, cos).</summary>
        public float Yaw;
        public float Speed;
        public float Phase;
        public float YawRate;
        /// <summary>Spring state for tails and ears (lags the turns).</summary>
        public float Sway;
        /// <summary>Head turn relative to the body, radians.</summary>
        public float Look;
        public float Scale;
        public float2 Target;
        public float Timer;
        public CreatureMode Mode;
        public uint Seed;

        /// <summary>The other animal in a game of chase or a hunt, by its id (0 for none).</summary>
        public int Partner;
        /// <summary>What it's paying attention to: the threat it flees, the water it drinks from.</summary>
        public float2 Focus;
        public byte Stage;
        /// <summary>Seconds in the current mode and stage.</summary>
        public float ModeTime, StageTime;
        /// <summary>Seconds alive, for breathing and chewing.</summary>
        public float Clock;
        /// <summary>Where it lives (a den); with Leash &gt; 0 it wanders no further than that from it.</summary>
        public float2 Home;
        public float Leash;

        // Posture, 0..1, eased towards what the current activity wants (read by CreatureAnimator).
        /// <summary>Lying down: body on the ground, legs folded.</summary>
        public float Lie;
        /// <summary>Head lowered to the ground (grazing, drinking, sleeping).</summary>
        public float HeadDown;
        /// <summary>Play bow: chest down, rump up.</summary>
        public float Bow;
        /// <summary>Curled up asleep: tail wrapped round, ears back.</summary>
        public float Curl;
    }

    /// <summary>One segment as the creature shader draws it (a tapered capsule). Must match Creature.shader.</summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct SegmentInstance
    {
        public float3 Position;
        public float Length;
        public quaternion Rotation;
        public float RadiusStart, RadiusEnd, Flatten, Pad;
        public float4 Color;
    }

    /// <summary>The land creatures walk on: its height, whether they may step somewhere (dry, clear, not burning), and where water is (to drink).</summary>
    public interface ICreatureGround
    {
        float Height(float2 xz);
        bool Walkable(float2 xz);
        bool Water(float2 xz);
    }
}
