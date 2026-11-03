// Adapted from Punchy by Zachary Franklin (https://github.com/Skeleman/Punchy),
// used with permission. From Punchy's CreatureAnimator.cs: arrays and a
// reusable scratch in place of Burst's native lists, the ground through
// ICreatureGround (no caves here); the maths is his. Individuals, style
// options, hair and task poses from upstream f3141f6 (at a post: the folk set Task).
// Kept close to Punchy, so our style analyzers are off here.
#pragma warning disable S3776, S3973, S2234, UNT0024, S3358, S107
using Unity.Mathematics;

namespace Punchy.Fauna
{
    /// <summary>
    /// Poses a creature for drawing. Layers, in order:
    ///  1. gait: every part blends its key poses by its own phase in the gait cycle (walk and run keys mixed by
    ///     speed, idle mixed in as it stops);
    ///  2. body: stands at the height its legs give it, follows the slope of the ground, bobs with each step and
    ///     rocks when bounding;
    ///  3. secondary motion: tails and ears swing out of turns on a spring; the head turns to look;
    ///  4. posture (from the activity): lying down lowers the body to the ground and folds legs that hang beneath
    ///     it; head-down bends the neck and head to the ground, chewing; a play bow drops the chest; curling up to
    ///     sleep wraps the tail round and lays the ears back; resting animals breathe;
    ///  5. the individual: each animal has fixed traits (from CreatureState.Individual) for posture, arm swing,
    ///     bounce, torso twist and head tilt, shows one option per style group (hairstyles, beards) and has its own
    ///     hair colour; a worker at its post poses its arms for its task (hammering, tending…);
    ///  6. feet: legs are bent with two-bone IK so their feet meet the actual ground rather than a flat plane;
    ///     chains marked Follow Ground (snake bodies, dragging tails) lie along the terrain segment by segment.
    /// </summary>
    public sealed class CreatureAnimator
    {
        const int MaxSegments = 128;

        // Scratch, reused for every creature (posing is one at a time, on the main thread).
        readonly float3[] segPos = new float3[MaxSegments];
        readonly quaternion[] segRot = new quaternion[MaxSegments];
        readonly float3[] endPos = new float3[MaxSegments];
        readonly quaternion[] endRot = new quaternion[MaxSegments];
        // Parts this individual doesn't show (unchosen style options, and anything hanging from them): neither posed nor drawn.
        readonly bool[] partHidden = new bool[MaxSegments];
        readonly bool[] segHidden = new bool[MaxSegments];

        /// <summary>Writes the segments this individual shows from <paramref name="offset"/> on, the body first, and returns how many.</summary>
        public int Evaluate(CreatureBody b, in CreatureState s, int coat, ICreatureGround terrain,
                            SegmentInstance[] output, int offset) =>
            Evaluate(b, in s, b.TraitsOf(coat, s.Individual), terrain, output, offset);

        /// <summary>The same, for a caller that keeps each animal's traits (a crowd).</summary>
        public int Evaluate(CreatureBody b, in CreatureState s, in CreatureTraits traits, ICreatureGround terrain,
                            SegmentInstance[] output, int offset)
        {
            var scale = s.Scale;
            var move = math.smoothstep(0.03f, math.max(0.3f * b.WalkSpeed, 0.05f), s.Speed);
            var run = math.smoothstep(b.WalkSpeed * 1.1f, math.max(b.WalkSpeed * 1.2f, b.RunSpeed * 0.7f), s.Speed);

            // Body on the ground: height from the legs, pitch from the ground ahead and behind.
            var forward = new float3(math.sin(s.Yaw), 0f, math.cos(s.Yaw));
            var halfLength = b.BodyLength * 0.5f * scale;
            // Swimmers and floaters are carried at their own height (the body's centre at Position).
            var aquatic = b.Habitat != Habitat.Land;
            var ground = aquatic ? s.Position.y - b.StandHeight * scale : terrain.Height(s.Position.xz);
            var gradient = 0f;   // rise per metre forward
            // Four-legged bodies tilt with the slope; upright ones stay level (their legs take up the slope).
            if (!b.Upright && !aquatic)
            {
                var front = terrain.Height(s.Position.xz + forward.xz * halfLength);
                var back = terrain.Height(s.Position.xz - forward.xz * halfLength);
                gradient = (front - back) / (2f * halfLength);
            }
            var pitch = math.clamp(-math.atan(gradient), -0.45f, 0.45f);
            // A leaping fish noses up out of the water and dives back in nose first.
            if (s.Air > 0f)
                pitch -= math.cos(math.PI * s.Air) * 0.9f;

            var id = s.Individual;
            var girth = traits.Girth;

            var bobs = math.lerp(b.BobsPerCycle.x, b.BobsPerCycle.y, run);
            var bob = math.lerp(b.Bob.x, b.Bob.y, run) * traits.Bounce * move * scale * (0.5f - 0.5f * math.cos(2f * math.PI * bobs * s.Phase));
            pitch += b.RunRock * run * move * math.sin(2f * math.PI * s.Phase);

            // Posture: lying lowers the body onto its belly; a bow or a lowered head tips the chest down.
            var lying = math.min(b.StandHeight, b.FrontRadius * b.BodyFlatten * 0.9f);
            var height = math.lerp(b.StandHeight, lying, s.Lie) * scale;
            var lowered = b.StandHeight * scale - height;
            pitch += s.Bow * 0.3f + s.HeadDown * 0.1f * (1f - s.Lie);
            var still = 1f - move;
            var breath = 0.5f + 0.5f * math.sin(s.Clock * (s.Curl > 0.5f ? 1.3f : 2.2f));
            height += breath * 0.01f * scale * still * (0.3f + s.Lie);

            var rootRot = math.mul(quaternion.RotateY(s.Yaw), quaternion.RotateX(pitch));
            var rootPos = new float3(s.Position.x, ground + height - bob, s.Position.z);

            // Body.
            var body = b.Segments[0];
            output[offset] = Instance(rootPos + math.mul(rootRot, new float3(0f, 0f, -halfLength + body.RadiusStart * scale)), rootRot, body, scale, girth, b.ColourOf(0, in traits));

            int styleGroup = 0, styleShown = 0;
            for (int i = 0; i < b.Parts.Length && i < MaxSegments; i++)
            {
                var part = b.Parts[i];
                // A group's options stand together in the list, so one hash does for the group.
                if (part.StyleGroup != styleGroup)
                {
                    styleGroup = part.StyleGroup;
                    styleShown = styleGroup > 0 ? CreatureBody.Style(id, styleGroup, part.StyleCount) : 0;
                }
                var hidden = part.Parent >= 0 && partHidden[part.Parent] || part.StyleGroup > 0 && styleShown != part.StyleIndex;
                partHidden[i] = hidden;
                for (int j = 0; j < part.SegmentCount; j++)
                    if (part.FirstSegment + j < MaxSegments)
                        segHidden[part.FirstSegment + j] = hidden;
                if (hidden)
                    continue;
                var parentPos = part.Parent < 0 ? rootPos : endPos[part.Parent];
                var parentRot = part.Parent < 0 ? rootRot : endRot[part.Parent];
                var pos = parentPos + math.mul(parentRot, part.Socket * scale);
                var rot = math.mul(parentRot, part.Rest);
                var phase = math.frac(s.Phase + part.Phase);
                var torso = b.Upright && part.Parent < 0 && part.Role != LimbRole.Leg;
                var amplitude = part.Role == LimbRole.Arm ? traits.ArmSwing : torso ? traits.Twist : 1f;

                // Legs hanging beneath the body fold up when it lies down (front legs half-fold for a play bow);
                // splayed legs (lizards, spiders) keep their feet planted and bend at the knees instead.
                var fold = 0f;
                var folds = part.Role == LimbRole.Leg && part.Parent < 0 && math.mul(part.Rest, new float3(0f, 0f, 1f)).y < -0.7f;
                if (folds)
                    fold = math.max(s.Lie, part.Socket.z > 0f ? s.Bow * 0.5f : 0f);

                for (int j = 0; j < part.SegmentCount; j++)
                {
                    var seg = part.FirstSegment + j;
                    var a = b.Angles(seg, math.frac(phase - j * part.JointPhaseStep), move, run);
                    if (amplitude != 1f)
                    {
                        var idle = b.Poses[seg * CreatureBody.PoseCount + CreatureBody.Idle];
                        a = idle + (a - idle) * amplitude;
                    }
                    if (torso && j == 0)
                        a.x += traits.Lean;
                    if (part.Role == LimbRole.Head && j == 0)
                        a += traits.HeadTilt;
                    a += Secondary(part, in s, j, run, b.Upright);
                    a += TaskPose(part, in s, j);
                    if (fold > 0f)
                        a.x += Fold(j, part.SegmentCount) * fold;
                    rot = math.mul(rot, quaternion.EulerZXY(a));
                    if (part.FollowGround && !aquatic)
                        rot = LieOnGround(terrain, pos, rot, b.Segments[seg].Length * scale, b.Segments[seg].RadiusEnd * b.Segments[seg].Flatten * scale);
                    if (seg < MaxSegments)
                    {
                        segPos[seg] = pos;
                        segRot[seg] = rot;
                    }
                    pos += math.mul(rot, new float3(0f, 0f, b.Segments[seg].Length * scale));
                }

                if (!aquatic && part.Role == LimbRole.Leg && part.Parent < 0 && part.SegmentCount >= 2 && part.FirstSegment + part.SegmentCount <= MaxSegments)
                {
                    // Where the ground would be under this foot if it were the flat, sloped plane the body is
                    // standing on, versus where it really is: bend the leg by the difference.
                    var along = math.dot(pos.xz - s.Position.xz, forward.xz);
                    var nominal = ground + along * gradient;
                    var actual = terrain.Height(pos.xz);
                    var reach = 0f;
                    for (int j = 0; j < part.SegmentCount; j++)
                        reach += b.Segments[part.FirstSegment + j].Length * scale;
                    var correction = (actual - nominal) * (1f - fold) + (folds ? 0f : lowered);
                    correction = math.clamp(correction, -0.45f * reach, 0.45f * reach);
                    pos += PlaceFoot(b, part, new float3(0f, correction, 0f), scale);
                }

                endPos[i] = pos;
                endRot[i] = rot;
            }

            var written = 1;
            for (int seg = 1; seg < b.Segments.Length && seg < MaxSegments; seg++)
            {
                if (segHidden[seg])
                    continue;
                output[offset + written++] = Instance(segPos[seg], segRot[seg], b.Segments[seg], scale, girth, b.ColourOf(seg, in traits));
            }
            return written;
        }

        /// <summary>
        /// A worker's arms at its post. Arms hang down from the shoulder: negative pitch raises them forwards,
        /// positive swings them back; mirrored arms keep the same pitch.
        /// </summary>
        static float3 TaskPose(CreaturePart part, in CreatureState s, int joint)
        {
            if (part.Role != LimbRole.Arm || s.Task == CreatureTask.None || joint > 1)
                return float3.zero;
            var right = part.Side > 0f;
            var t = s.Clock + (s.Individual % 100) * 0.13f;
            switch (s.Task)
            {
                case CreatureTask.Hammer:
                case CreatureTask.Chop:
                {
                    // Raise slowly, strike fast; a hammer is one-handed (the other hand holds the work), an axe two.
                    var swinging = right || s.Task == CreatureTask.Chop;
                    if (!swinging)
                        return new float3(-0.7f, 0f, 0f);
                    var p = math.frac(t * (s.Task == CreatureTask.Chop ? 0.55f : 0.85f));
                    var lift = p < 0.7f ? math.smoothstep(0f, 0.7f, p) : 1f - math.smoothstep(0.7f, 0.8f, p);
                    return new float3(joint == 0 ? -(0.45f + 1.8f * lift) : -(0.35f + 0.9f * lift), 0f, 0f);
                }
                case CreatureTask.Gesture:
                {
                    // Hands resting on the counter; every so often one rises to beckon or point.
                    var p = math.frac(t * 0.21f + (right ? 0f : 0.5f));
                    var raise = math.smoothstep(0f, 0.06f, p) * (1f - math.smoothstep(0.14f, 0.24f, p));
                    return new float3(joint == 0 ? -0.45f - 1.2f * raise : -0.9f + 0.4f * raise, 0f, 0f);
                }
                case CreatureTask.Tend:
                    return new float3(joint == 0 ? -0.65f + 0.22f * math.sin(t * 1.5f + (right ? 0f : 1.4f)) : -0.55f, 0f, 0f);
                case CreatureTask.Watch:
                    return new float3(joint == 0 ? 0.35f : -0.75f, 0f, 0f);   // hands clasped behind the back
                default:
                    return float3.zero;
            }
        }

        /// <summary>
        /// Tilts a segment up or down (keeping its heading) so its far end rests on the ground, for chains that lie
        /// along the terrain. The next segment then starts from there, so the chain drapes over bumps.
        /// </summary>
        static quaternion LieOnGround(ICreatureGround terrain, float3 start, quaternion rot, float length, float radius)
        {
            if (length < 1e-4f)
                return rot;
            var dir = math.mul(rot, new float3(0f, 0f, 1f));
            var flat = math.normalizesafe(new float3(dir.x, 0f, dir.z), dir);
            var end = start + flat * length;
            var rise = math.clamp(terrain.Height(end.xz) + radius - start.y, -0.9f * length, 0.9f * length);
            var wanted = flat * math.sqrt(length * length - rise * rise) + new float3(0f, rise, 0f);
            return math.mul(FromTo(dir, wanted / length), rot);
        }

        /// <summary>
        /// A folded leg's extra pitch per joint: the thigh swings back, the shin folds forward under it, the foot
        /// tucks back (positive pitch swings a leg backwards).
        /// </summary>
        static float Fold(int joint, int count)
        {
            if (count == 1)
                return 0.9f;
            if (count == 2)
                return joint == 0 ? 1.1f : -2.2f;
            return joint == 0 ? 1f : joint == 1 ? -2.1f : joint == 2 ? 1.4f : 0f;
        }

        /// <summary>
        /// Spring, look and posture offsets added to a part's joint angles. An upright body's neck and head point up
        /// rather than forward, so turning them to look is a roll about their own length, not a yaw (which would
        /// tilt the head sideways).
        /// </summary>
        static float3 Secondary(CreaturePart part, in CreatureState s, int joint, float run, bool upright)
        {
            // Chewing or lapping while the head is down.
            var chew = math.sin(s.Clock * 7f) * 0.06f * s.HeadDown * (1f - s.Curl);
            switch (part.Role)
            {
                case LimbRole.Tail:
                    // Each joint swings a little more than the last, so the tail curls out of the turn; asleep, it
                    // wraps round the body.
                    return new float3(s.Curl * 0.1f, (s.Sway * part.Spring + s.Curl * 0.3f) * (1f + joint * 0.5f), 0f);
                case LimbRole.Ear:
                    return new float3(-0.5f * run * part.Spring - s.Curl * 0.6f, s.Sway * part.Spring * 0.5f * part.Side, 0f);
                case LimbRole.Head:
                    return upright ? new float3(s.HeadDown * 0.5f + chew, 0f, -s.Look * 0.6f)
                                   : new float3(s.HeadDown * 0.5f + chew, s.Look * 0.6f, 0f);
                case LimbRole.Neck:
                    var n = math.max(part.SegmentCount, 1);
                    return upright ? new float3(s.HeadDown * 1.5f / n, 0f, -s.Look * 0.4f / n)
                                   : new float3(s.HeadDown * 1.5f / n, s.Look * 0.4f / n, 0f);
                default:
                    return float3.zero;
            }
        }

        /// <summary>
        /// Two-bone IK on a leg's first two segments: moves the end of the second segment by delta, keeping the knee
        /// bending the way it did. The rest of the leg (foot, toes) keeps its orientation and shifts with it.
        /// Returns how far the end of the leg moved.
        /// </summary>
        float3 PlaceFoot(CreatureBody b, CreaturePart part, float3 delta, float scale)
        {
            int s0 = part.FirstSegment, s1 = s0 + 1;
            var l0 = b.Segments[s0].Length * scale;
            var l1 = b.Segments[s1].Length * scale;
            var hip = segPos[s0];
            var knee = segPos[s1];
            var ankle = knee + math.mul(segRot[s1], new float3(0f, 0f, l1));
            var target = ankle + delta;

            var toTarget = target - hip;
            var d = math.clamp(math.length(toTarget), math.abs(l0 - l1) + 1e-3f, (l0 + l1) * 0.999f);
            var dir = math.normalizesafe(toTarget, math.mul(segRot[s0], new float3(0f, 0f, 1f)));
            target = hip + dir * d;

            // Keep the knee in the plane it already bends in, on the same side.
            var normal = math.cross(knee - hip, ankle - knee);
            normal = math.lengthsq(normal) > 1e-10f ? math.normalize(normal) : math.mul(segRot[s0], new float3(1f, 0f, 0f));
            var cosA = math.clamp((l0 * l0 + d * d - l1 * l1) / (2f * l0 * d), -1f, 1f);
            var sinA = math.sqrt(1f - cosA * cosA);
            var perp = math.normalizesafe(math.cross(normal, dir));
            var kneeA = hip + l0 * (dir * cosA + perp * sinA);
            var kneeB = hip + l0 * (dir * cosA - perp * sinA);
            var newKnee = math.distancesq(kneeA, knee) < math.distancesq(kneeB, knee) ? kneeA : kneeB;

            segRot[s0] = math.mul(FromTo(math.normalizesafe(knee - hip), math.normalizesafe(newKnee - hip)), segRot[s0]);
            segRot[s1] = math.mul(FromTo(math.normalizesafe(ankle - knee), math.normalizesafe(target - newKnee)), segRot[s1]);
            segPos[s1] = newKnee;

            var shift = target - ankle;
            for (int j = 2; j < part.SegmentCount; j++)
                segPos[s0 + j] += shift;
            return shift;
        }

        static quaternion FromTo(float3 a, float3 b)
        {
            var w = 1f + math.dot(a, b);
            if (w < 1e-5f)
            {
                var axis = math.abs(a.x) > 0.9f ? new float3(0f, 1f, 0f) : new float3(1f, 0f, 0f);
                return quaternion.AxisAngle(math.normalize(math.cross(a, axis)), math.PI);
            }
            var c = math.cross(a, b);
            return math.normalize(new quaternion(c.x, c.y, c.z, w));
        }

        static SegmentInstance Instance(float3 position, quaternion rotation, CreatureSegment seg, float scale, float girth, float4 color) => new SegmentInstance
        {
            Position = position,
            Rotation = rotation,
            Length = seg.Length * scale,
            RadiusStart = seg.RadiusStart * scale * girth,
            RadiusEnd = seg.RadiusEnd * scale * girth,
            Flatten = seg.Flatten,
            Color = color
        };
    }
}
