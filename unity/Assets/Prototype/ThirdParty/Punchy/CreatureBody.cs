// Adapted from Punchy by Zachary Franklin (https://github.com/Skeleman/Punchy),
// used with permission. From Punchy's CreatureData.cs: the Entities blob is a
// plain class with arrays, so it runs without Entities; the maths is his. A
// segment's Pad is ours: a crack web over 0, shadow only under 0. Individuals,
// hair and style options from upstream f3141f6, habitats from c52d9ff. Built from rows (rules/fauna.json, rules/parts.json; Chem.FaunaTables), not from assets.
// Kept close to Punchy, so our style analyzers are off here.
#pragma warning disable S3776, S3973, S2234, UNT0024
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using Chem;
using Unity.Mathematics;
using UnityEngine;

namespace Punchy.Fauna
{
    /// <summary>
    /// A species' row flattened for posing: every part (mirrored slots expanded into left and right copies, parents before
    /// children), every segment (index 0 is the body), and every segment's seven key poses.
    /// </summary>
    public sealed class CreatureBody
    {
        public const int PoseCount = 7;   // idle, walk reach/push/lift, run reach/push/lift
        public const int Idle = 0, WalkReach = 1, WalkPush = 2, WalkLift = 3, RunReach = 4, RunPush = 5, RunLift = 6;

        /// <summary>The species' row in rules/fauna.json, its name in words, and the species it's a variant of (or its own id).</summary>
        public string Id, Name, Kind;
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
        /// <summary>How many appear together (the species a variant is based on decides).</summary>
        public int2 Group;
        /// <summary>Animals per hectare out in the wilds, and whether only near water.</summary>
        public float Density;
        public bool NearWater;
        /// <summary>Whether its row has a "spawn": only what spawns can hunt.</summary>
        public bool Spawns;
        /// <summary>The folk it comes out among (a species' id, or null), and what share of them it is.</summary>
        public string Among;
        public float Share;
        public ActiveTime Active;
        public float Grazing, Thirst, Playfulness, Laziness, Hunger, ChaseTime;
        /// <summary>The species its row says it hunts, by id (any variant of one counts).</summary>
        public IReadOnlyList<string> Hunts = Array.Empty<string>();
        /// <summary>Species (indices in the caller's list) it hunts.</summary>
        public int[] Prey = Array.Empty<int>();
        /// <summary>Whether anything hunts it (so it needs to watch out for predators).</summary>
        public bool Hunted;

        public CreaturePart[] Parts;
        public CreatureSegment[] Segments;
        public float3[] Poses;   // Segments.Length × PoseCount, radians
        /// <summary>The row's colours: its coats, hair and how they vary.</summary>
        public CreaturePalette Colours = new CreaturePalette();
        /// <summary>
        /// A graft's donors whose slots wear their own coats and hair ("coat": "own", Chem.SlotDef.OwnCoatOf): a
        /// segment's <see cref="CreatureSegment.Palette"/> 1 is the first. Empty for a row of its own.
        /// </summary>
        public CreaturePalette[] Own = Array.Empty<CreaturePalette>();
        /// <summary>Colourings individuals choose between (at least one).</summary>
        public CreatureCoatData[] Coats { get => Colours.Coats; set => Colours.Coats = value; }
        /// <summary>Hair colours individuals choose between (may be empty).</summary>
        public float4[] HairColors { get => Colours.HairColors; set => Colours.HairColors = value; }
        /// <summary>How much one individual's girth differs from the next (Chem.VaryDef); shade and warmth are the palette's.</summary>
        public float Girth;
        /// <summary>Where it lives, how deep it swims and how many leaps it makes a minute.</summary>
        public Habitat Habitat;
        public float SwimDepth, Leaps;

        /// <summary>
        /// A fixed random number in -1..1 for one trait of one individual (posture, arm swing, hair…): the same for
        /// as long as the animal lives.
        /// </summary>
        public static float Trait(uint individual, int trait) =>
            math.hash(new uint2(individual, (uint)trait * 2654435761u + 17u)) / (float)uint.MaxValue * 2f - 1f;

        /// <summary>Which option of a style group an individual shows.</summary>
        public static int Style(uint individual, int group, int count) =>
            (int)(math.hash(new uint2(individual, (uint)group * 40503u + 101u)) % (uint)math.max(count, 1));

        /// <summary>An individual's hair colour (rgb), or the segment's own if the species has none.</summary>
        public float4 HairColor(uint individual, float4 fallback) =>
            HairColors.Length == 0 ? fallback : Colours.HairOf(individual);

        /// <summary>A trait as a number in 0..1.</summary>
        public static float Unit(uint individual, int trait) => Trait(individual, trait) * 0.5f + 0.5f;

        /// <summary>
        /// One of the coats as this individual wears it: lighter or darker, warmer or cooler, its accent (a skin)
        /// picked apart from its base (a tunic) if the species mixes them. Individual 0 is the coat itself.
        /// </summary>
        public CreatureCoatData CoatOf(int coat, uint individual) => Colours.CoatOf(coat, individual);

        /// <summary>All that sets this individual apart, worked out once so posing it needn't hash it every frame.</summary>
        public CreatureTraits TraitsOf(int coat, uint individual)
        {
            var traits = new CreatureTraits
            {
                Lean = Trait(individual, 1) * 0.13f,
                ArmSwing = 1f + Trait(individual, 2) * 0.45f,
                Bounce = 1f + Trait(individual, 3) * 0.5f,
                Twist = 1f + Trait(individual, 4) * 0.6f,
                HeadTilt = new float3(Trait(individual, 6) * 0.1f, 0f, Trait(individual, 7) * 0.07f),
                Girth = GirthOf(individual),
                Worn = CoatOf(coat, individual),
                Hair = Colours.HairOf(individual),
            };
            if (Own.Length == 0)
                return traits;
            // Each donor's slots wear the coat that donor's own animal of this individual would: the same pick, the same tint.
            traits.OwnWorn = new CreatureCoatData[Own.Length];
            traits.OwnHair = new float4[Own.Length];
            for (int i = 0; i < Own.Length; i++)
            {
                traits.OwnWorn[i] = Own[i].CoatOf(individual == 0u ? 0 : Own[i].PickCoat(Unit(individual, 0)), individual);
                traits.OwnHair[i] = Own[i].HairOf(individual);
            }
            return traits;
        }

        /// <summary>How much stockier or slimmer than the row this individual is (1 for individual 0).</summary>
        public float GirthOf(uint individual) => individual == 0u ? 1f : 1f + Trait(individual, 13) * Girth;

        /// <summary>A segment's colour in a coat.</summary>
        public float4 SegmentColor(int segment, in CreatureCoatData c)
        {
            // Alpha carries how much the segment glows.
            var seg = Segments[segment];
            var glow = seg.Glow + (seg.Fixed != 0 ? 0f : c.AccentGlow * seg.Accent);
            var rgb = seg.Fixed != 0 ? seg.FixedColor.xyz : math.lerp(c.Base.xyz, c.Accent.xyz, seg.Accent);
            return new float4(rgb, math.saturate(glow));
        }

        /// <summary>
        /// A segment's colour as this individual wears it, as the animator draws it: its palette's coat (the row's, or
        /// a graft's donor's for a slot with its own coat), or that palette's hair for a hair segment.
        /// </summary>
        public float4 ColourOf(int segment, in CreatureTraits traits)
        {
            var seg = Segments[segment];
            var own = seg.Palette > 0 && traits.OwnWorn != null && seg.Palette <= traits.OwnWorn.Length;
            var worn = own ? traits.OwnWorn[seg.Palette - 1] : traits.Worn;
            var hair = own ? traits.OwnHair[seg.Palette - 1] : traits.Hair;
            return seg.Hair != 0 && hair.x >= 0f ? hair : SegmentColor(segment, in worn);
        }

        /// <summary>Picks a coat by weight from a random number in 0..1.</summary>
        public int PickCoat(float random) => Colours.PickCoat(random);

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
            public SlotDef Slot;
            public PartDef Part;
            public bool Mirrored;
            public int Parent;
        }

        internal static Color Rgb(double[] c) => new Color((float)c[0], (float)c[1], (float)c[2]);

        /// <summary>
        /// Builds a species from its row. A variant takes its body, parts, gait and behaviour from the species it is
        /// a variant of, and only its own colours, size and activities.
        /// </summary>
        /// <param name="prey">Indices of the species it hunts.</param>
        /// <param name="hunted">Whether any species hunts it.</param>
        public static CreatureBody Build(FaunaTables tables, string id, IReadOnlyList<int> prey = null, bool hunted = false)
        {
            var variant = tables.Species(id);
            var species = tables.BodyOf(id);
            // Expand slots: mirrored slots become left and right copies; children follow every copy of their parent.
            var parts = new List<Expanded>();
            var copies = new Dictionary<string, List<int>>();
            foreach (var slot in species.Slots)
            {
                var mine = new List<int>();
                copies[slot.Id] = mine;
                var limb = tables.Part(slot.Part);
                var parents = slot.On != null ? copies[slot.On] : new List<int> { -1 };
                foreach (var parent in parents)
                {
                    var parentMirrored = parent >= 0 && parts[parent].Mirrored;
                    mine.Add(parts.Count);
                    parts.Add(new Expanded { Slot = slot, Part = limb, Mirrored = parentMirrored, Parent = parent });
                    if (slot.Mirror)
                    {
                        mine.Add(parts.Count);
                        parts.Add(new Expanded { Slot = slot, Part = limb, Mirrored = !parentMirrored, Parent = parent });
                    }
                }
            }

            var segmentCount = 1;
            foreach (var p in parts)
                segmentCount += p.Part.Segments.Count;

            float length = (float)species.Length, radius = (float)species.Radius, flatten = (float)species.Flatten;
            float walkSpeed = (float)species.Walk;
            var root = new CreatureBody
            {
                Id = id,
                Name = variant.Name,
                Kind = species.Id,
                Hunts = variant.Hunts,
                BodyLength = length,
                FrontRadius = radius,
                RearRadius = radius * (float)species.Taper,
                BodyFlatten = flatten,
                WalkSpeed = walkSpeed,
                RunSpeed = math.max((float)species.Run, walkSpeed * 1.3f),
                Bob = new float2((float)species.Bob[0], (float)species.Bob[1]),
                BobsPerCycle = new float2((float)species.Bobs[0], (float)species.Bobs[1]),
                RunRock = math.radians((float)species.Rock),
                Upright = species.Upright,
                WanderRadius = (float)species.Wander,
                FleeDistance = (float)species.Flee,
                TurnRate = math.radians((float)species.Turn),
                IdleTime = new float2((float)species.Idle[0], (float)species.Idle[1]),
                Group = new int2((int)species.Group[0], (int)species.Group[1]),
                Density = (float)variant.Density,
                NearWater = variant.NearWater,
                Spawns = variant.Spawns,
                Among = variant.Among,
                Share = (float)variant.Share,
            };
            var sizeScale = variant != species ? (float)variant.VariantScale : 1f;
            root.Size = new float2((float)species.Size[0], (float)species.Size[1]) * sizeScale;
            root.Active = Enum.Parse<ActiveTime>(variant.Active, true);
            root.Grazing = (float)variant.Graze;
            root.Thirst = (float)variant.Thirst;
            root.Playfulness = (float)variant.Play;
            root.Laziness = (float)variant.Laze;
            root.Hunger = (float)variant.Hunger;
            root.ChaseTime = (float)variant.Chase;
            root.Hunted = hunted;
            root.Prey = prey != null ? new List<int>(prey).ToArray() : Array.Empty<int>();

            root.Parts = new CreaturePart[parts.Count];
            root.Segments = new CreatureSegment[segmentCount];
            root.Poses = new float3[segmentCount * PoseCount];

            // Coats come from the variant (or the species itself): its listed coats, or just its coat.
            root.Colours = CreaturePalette.Of(tables, id);
            // A graft's slots with their own coat wear their donor's, one palette a donor.
            var own = new List<string>();
            foreach (var slot in species.Slots)
                if (slot.OwnCoatOf != null && !own.Contains(slot.OwnCoatOf) && tables.Species(slot.OwnCoatOf) != null)
                    own.Add(slot.OwnCoatOf);
            root.Own = own.Select(d => CreaturePalette.Of(tables, d)).ToArray();

            root.Habitat = species.Habitats.Contains("swims") ? Habitat.Swims : species.Habitats.Contains("floats") ? Habitat.Floats : Habitat.Land;
            root.SwimDepth = (float)species.Depth;
            root.Leaps = (float)species.Leaps;

            root.Girth = (float)((variant.Vary ?? species.Vary)?.Girth ?? 0);

            root.Segments[0] = new CreatureSegment
            {
                Length = math.max(0f, length - root.RearRadius - root.FrontRadius),
                RadiusStart = root.RearRadius,
                RadiusEnd = root.FrontRadius,
                Flatten = flatten
            };

            var cursor = 1;
            for (int i = 0; i < parts.Count; i++)
            {
                var e = parts[i];
                var slot = e.Slot;
                var limb = e.Part;
                var side = e.Mirrored ? -1f : 1f;
                var socket = new float3((float)slot.At[0], (float)slot.At[1], (float)slot.At[2]);
                if (e.Parent < 0)
                {
                    // Body units → metres: across the radius at that point along the body, along the half-length.
                    var across = math.lerp(root.RearRadius, root.FrontRadius, socket.z * 0.5f + 0.5f);
                    socket = new float3(socket.x * across, socket.y * across * flatten, socket.z * length * 0.5f);
                }
                socket.x *= side;
                var dir = new float3((float)slot.Dir[0], (float)slot.Dir[1], (float)slot.Dir[2]);
                root.Parts[i] = new CreaturePart
                {
                    Parent = e.Parent,
                    Socket = socket,
                    Rest = Rotation(dir, side),
                    Phase = math.frac((float)slot.Phase + (e.Mirrored && slot.Mirror ? (float)slot.MirrorPhase : 0f)),
                    FirstSegment = cursor,
                    SegmentCount = limb.Segments.Count,
                    Role = Enum.Parse<LimbRole>(limb.Role, true),
                    Spring = (float)limb.Spring,
                    JointPhaseStep = (float)limb.Ripple,
                    FollowGround = limb.FollowsGround,
                    Side = side,
                    StyleGroup = slot.Style,
                    StyleIndex = StyleIndex(species, slot),
                    StyleCount = StyleCount(species, slot.Style)
                };

                float scale = (float)slot.Scale, thickness = (float)slot.Thickness;
                var palette = slot.OwnCoatOf != null ? own.IndexOf(slot.OwnCoatOf) + 1 : 0;
                for (int s = 0; s < limb.Segments.Count; s++)
                {
                    var seg = limb.Segments[s];
                    root.Segments[cursor] = new CreatureSegment
                    {
                        Length = (float)seg.Length * scale,
                        RadiusStart = (float)seg.RadiusStart * scale * thickness,
                        RadiusEnd = (float)seg.RadiusEnd * scale * thickness,
                        Flatten = (float)seg.Flatten,
                        Accent = (float)seg.Accent,
                        Fixed = seg.Colour != null ? 1 : 0,
                        FixedColor = seg.Colour != null ? (Vector4)Rgb(seg.Colour).linear : default,
                        Glow = (float)seg.Glow,
                        Hair = seg.Hair ? 1 : 0,
                        Palette = palette
                    };
                    for (int k = 0; k < PoseCount; k++)
                    {
                        var joints = limb.Poses[k];
                        var joint = s < joints.Length ? joints[s] : new double[3];
                        var a = math.radians(new float3((float)joint[0], (float)joint[1], (float)joint[2]));
                        root.Poses[cursor * PoseCount + k] = new float3(a.x, a.y * side, a.z * side);
                    }
                    cursor++;
                }
            }

            MeasureGait(root, new Vector2((float)species.Stride[0], (float)species.Stride[1]));
            return root;
        }

        /// <summary>How many slots before this one share its style group (its option number).</summary>
        static int StyleIndex(SpeciesDef species, SlotDef slot)
        {
            if (slot.Style <= 0)
                return 0;
            var n = 0;
            foreach (var other in species.Slots)
            {
                if (other == slot)
                    break;
                if (other.Style == slot.Style)
                    n++;
            }
            return n;
        }

        static int StyleCount(SpeciesDef species, int group)
        {
            if (group <= 0)
                return 0;
            var n = 0;
            foreach (var other in species.Slots)
                if (other.Style == group)
                    n++;
            return n;
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
        public static void MeasureGait(CreatureBody b, Vector2 leglessStride)
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
        /// <summary>Style option group (0 = always shown), this part's option in it, and how many options it has.</summary>
        public int StyleGroup, StyleIndex, StyleCount;
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
        /// <summary>Non-zero: coloured by the individual's hair colour.</summary>
        public int Hair;
        /// <summary>Whose colours it wears: 0 the row's, n the body's Own[n - 1] (a graft's donor).</summary>
        public int Palette;
    }

    /// <summary>What one individual's way of moving and looking come to (CreatureBody.TraitsOf).</summary>
    public struct CreatureTraits
    {
        public float Lean, ArmSwing, Bounce, Twist, Girth;
        public float3 HeadTilt;
        public CreatureCoatData Worn;
        /// <summary>Its hair colour; x under 0 keeps the segments' own.</summary>
        public float4 Hair;
        /// <summary>A graft's: what it wears on each donor's own-coat slots, by CreatureBody.Own; null for a row of its own.</summary>
        public CreatureCoatData[] OwnWorn;
        public float4[] OwnHair;
    }

    public struct CreatureCoatData
    {
        public float4 Base, Accent;   // linear
        public float AccentGlow;
        public float Weight;
    }

    /// <summary>A species' colours: the coats its animals pick from, their hair, and how one animal's differ from the next.</summary>
    public sealed class CreaturePalette
    {
        /// <summary>The species whose colours these are.</summary>
        public string Id;
        /// <summary>Colourings individuals choose between (at least one).</summary>
        public CreatureCoatData[] Coats = { new CreatureCoatData { Base = new float4(1f), Accent = new float4(1f), Weight = 1f } };
        /// <summary>Hair colours individuals choose between (may be empty).</summary>
        public float4[] HairColors = Array.Empty<float4>();
        /// <summary>How one individual differs from the next (Chem.VaryDef).</summary>
        public float Shade, Warm;
        public bool MixCoats;

        /// <summary>A species' colours as its row gives them: a variant's own coats, and its hair and vary or its base's.</summary>
        public static CreaturePalette Of(FaunaTables tables, string id)
        {
            var variant = tables.Species(id);
            var species = tables.BodyOf(id);
            var coats = new List<CoatDef>(variant.Coats);
            if (coats.Count == 0)
                coats.Add(variant.Coat);
            var hair = variant.Hair.Count > 0 ? variant.Hair : species.Hair;
            var vary = variant.Vary ?? species.Vary;
            return new CreaturePalette
            {
                Id = id,
                Coats = coats.Select(c => new CreatureCoatData
                {
                    Base = (Vector4)CreatureBody.Rgb(c.Base).linear,
                    Accent = (Vector4)CreatureBody.Rgb(c.Accent).linear,
                    AccentGlow = (float)c.Glow,
                    Weight = math.max((float)c.Weight, 0.0001f)
                }).ToArray(),
                HairColors = hair.Select(h => (float4)(Vector4)CreatureBody.Rgb(h).linear).ToArray(),
                Shade = (float)(vary?.Shade ?? 0),
                Warm = (float)(vary?.Warm ?? 0),
                MixCoats = vary?.Mix ?? false,
            };
        }

        /// <summary>One of the coats as this individual wears it (CreatureBody.CoatOf).</summary>
        public CreatureCoatData CoatOf(int coat, uint individual)
        {
            var c = Coats[math.clamp(coat, 0, Coats.Length - 1)];
            if (individual == 0u)
                return c;
            if (MixCoats)
                c.Accent = Coats[CreatureBody.Style(individual, 9967, Coats.Length)].Accent;
            c.Base = Tinted(c.Base, individual, 8);
            c.Accent = Tinted(c.Accent, individual, 11);
            return c;
        }

        float4 Tinted(float4 colour, uint individual, int trait)
        {
            var shade = 1f + CreatureBody.Trait(individual, trait) * Shade;
            var warm = CreatureBody.Trait(individual, trait + 1) * Warm;
            return new float4(colour.xyz * new float3(shade * (1f + warm), shade, shade * (1f - warm)), colour.w);
        }

        /// <summary>This individual's hair colour (rgb); x is -1 when the species has none, so segments keep their own.</summary>
        public float4 HairOf(uint individual) =>
            HairColors.Length == 0 ? new float4(-1f) : new float4(HairColors[CreatureBody.Style(individual, 9973, HairColors.Length)].xyz, 0f);

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
    }

    public enum LimbRole : byte
    {
        /// <summary>Touches the ground: its foot is placed on the terrain by IK, and its stride sets the gait speed.</summary>
        Leg,
        Arm,
        /// <summary>Swings out as the creature turns (spring lag).</summary>
        Tail,
        Neck,
        /// <summary>Turns towards whatever the creature is looking at.</summary>
        Head,
        /// <summary>Flicks with a little spring lag.</summary>
        Ear,
        Horn,
        Eye,
        /// <summary>A static body feature: hump, mane, stripe, antler tine, shell ridge…</summary>
        Feature
    }

    public enum Habitat : byte
    {
        Land,
        /// <summary>Swims under the surface; keeps to water deep enough to swim in.</summary>
        Swims,
        /// <summary>Floats on the surface (waterfowl).</summary>
        Floats
    }

    public enum ActiveTime : byte
    {
        /// <summary>Sleeps at night.</summary>
        Day,
        /// <summary>Sleeps by day.</summary>
        Night,
        /// <summary>Awake at all hours, taking naps.</summary>
        Always
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

    /// <summary>What a worker does at its post (posed by CreatureAnimator).</summary>
    public enum CreatureTask : byte
    {
        None,
        /// <summary>Swings a hammer at an anvil (right arm), holding the work with the left.</summary>
        Hammer,
        /// <summary>Swings an axe down with both arms (chopping wood).</summary>
        Chop,
        /// <summary>Leans on a counter, now and then raising a hand (a vendor).</summary>
        Gesture,
        /// <summary>Both hands working in front, slowly (drawing water, stirring).</summary>
        Tend,
        /// <summary>Stands keeping watch, hands behind the back (a gatekeeper).</summary>
        Watch
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
        /// <summary>A step within the mode: Play's chasing and running, Drink's going and lapping, Flee's running, veering and milling about.</summary>
        public byte Stage;
        /// <summary>Seconds in the current mode and stage.</summary>
        public float ModeTime, StageTime;
        /// <summary>Seconds alive, for breathing and chewing.</summary>
        public float Clock;
        /// <summary>Where it lives (a den); with Leash &gt; 0 it goes no further than that from it, even fleeing.</summary>
        public float2 Home;
        public float Leash;
        /// <summary>Fixed for life: picks this individual's traits (posture, gait, hair). Set at spawn.</summary>
        public uint Individual;
        /// <summary>A leap out of the water: progress 0..1 (0 = not leaping), and how high it goes.</summary>
        public float Air, AirHeight;
        /// <summary>A worker's job while at its post (None when it's not); the view's folk set it.</summary>
        public CreatureTask Task;

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

    /// <summary>The land creatures walk on: its height, whether they may step somewhere (dry, clear, not burning), and where water is (to drink, or to live in).</summary>
    public interface ICreatureGround
    {
        float Height(float2 xz);
        bool Walkable(float2 xz);
        bool Water(float2 xz);
        /// <summary>How deep the water is here in metres; below 0 where there's none to swim in.</summary>
        float WaterDepth(float2 xz) => -1f;
    }
}
