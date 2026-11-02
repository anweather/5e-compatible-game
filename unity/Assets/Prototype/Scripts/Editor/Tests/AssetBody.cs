// The builder CreatureBody had while species were Editor assets, word for word, kept only so
// (with upstream f3141f6's hair and styles) CreatureRowTests can prove a body from its row equals the body from its asset. Goes with the assets.
#pragma warning disable S3776, S3973, S2234, UNT0024
using System;
using System.Collections.Generic;
using Punchy.Fauna;
using Unity.Mathematics;
using UnityEngine;

static class AssetBody
{
    struct Expanded
    {
        public LimbSlot Slot;
        public bool Mirrored;
        public int Parent;
        public int SlotIndex;
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
                parts.Add(new Expanded { Slot = slot, Mirrored = parentMirrored, Parent = parent, SlotIndex = i });
                if (slot.Mirror)
                {
                    mine.Add(parts.Count);
                    parts.Add(new Expanded { Slot = slot, Mirrored = !parentMirrored, Parent = parent, SlotIndex = i });
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
        root.Poses = new float3[segmentCount * CreatureBody.PoseCount];

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

        var hair = variant.HairColors.Count > 0 ? variant.HairColors : species.HairColors;
        root.HairColors = new float4[hair.Count];
        for (int i = 0; i < hair.Count; i++)
            root.HairColors[i] = (Vector4)hair[i].linear;

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
                Side = side,
                StyleGroup = slot.StyleGroup,
                StyleIndex = StyleIndex(species, e.SlotIndex),
                StyleCount = StyleIndex(species, species.Parts.Count, slot.StyleGroup)
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
                    Glow = seg.Glow,
                    Hair = seg.Hair ? 1 : 0
                };
                for (int k = 0; k < CreatureBody.PoseCount; k++)
                {
                    var a = math.radians((float3)poses[k][s]);
                    root.Poses[cursor * CreatureBody.PoseCount + k] = new float3(a.x, a.y * side, a.z * side);
                }
                cursor++;
            }
        }

        CreatureBody.MeasureGait(root, species.LeglessStride);
        return root;
    }

    // How many slots before slotIndex share its style group (its option number).
    static int StyleIndex(CreatureSpecies species, int slotIndex, int group = -1)
    {
        if (group < 0)
            group = species.Parts[slotIndex].StyleGroup;
        var n = 0;
        for (int i = 0; i < slotIndex; i++)
            if (species.Parts[i].Limb != null && species.Parts[i].StyleGroup == group && group > 0)
                n++;
        return n;
    }

    static quaternion Rotation(float3 degrees, float side) => CreatureBody.Rotation(degrees, side);
}
