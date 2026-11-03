// A species' body at rest, in metres, for describe (bodies-and-surfaces.md section 3): headless,
// Claude can't see a creature, so the tools say where each slot sits and what shape it makes.
// It follows the view's sums (Punchy's CreatureBody.Build and CreatureAnimator): body units to
// metres across the radius and along the half-length, each slot copied per copy of what it hangs
// on and again when mirrored, joints turned z, then x, then y, the idle pose. Only words come out.
using System;
using System.Collections.Generic;
using System.Linq;

namespace Chem.Mcp
{
    internal sealed class BodyShape
    {
        /// <summary>One drawn copy of a slot: where it starts and ends at rest, in metres in the body's frame (x right, y up, z ahead).</summary>
        internal sealed class Limb
        {
            public SlotDef Slot;
            public PartDef Part;
            public int Parent;
            public bool Mirrored, Shown;
            public double[] Start, End;
            public double[] EndRot;
        }

        public readonly List<Limb> Limbs = new List<Limb>();
        /// <summary>The body's extent at rest, before an animal's size: least and most x, y and z.</summary>
        public readonly double[] Min = new double[3], Max = new double[3];

        BodyShape() { }

        /// <summary>The shape of a species' body (a variant's is the species it's built on).</summary>
        public static BodyShape Of(FaunaTables tables, SpeciesDef body)
        {
            var shape = new BodyShape();
            double length = body.Length, rear = body.Radius * body.Taper, front = body.Radius;
            double half = length / 2, wide = Math.Max(rear, front);
            shape.Grow(new[] { -wide, -wide * body.Flatten, -half }, 0);
            shape.Grow(new[] { wide, wide * body.Flatten, half }, 0);
            var copies = new Dictionary<string, List<int>>(StringComparer.Ordinal);
            var firstOfStyle = new HashSet<int>();
            foreach (var slot in body.Slots)
            {
                var part = tables.Part(slot.Part);
                var mine = new List<int>();
                copies[slot.Id] = mine;
                if (part == null) continue;
                bool styleShown = slot.Style <= 0 || firstOfStyle.Add(slot.Style);
                var parents = slot.On != null && copies.TryGetValue(slot.On, out var on) ? on : new List<int> { -1 };
                foreach (int parent in parents)
                {
                    bool parentMirrored = parent >= 0 && shape.Limbs[parent].Mirrored;
                    bool parentShown = parent < 0 || shape.Limbs[parent].Shown;
                    mine.Add(shape.Add(body, slot, part, parent, parentMirrored, styleShown && parentShown));
                    if (slot.Mirror) mine.Add(shape.Add(body, slot, part, parent, !parentMirrored, styleShown && parentShown));
                }
            }
            return shape;
        }

        int Add(SpeciesDef body, SlotDef slot, PartDef part, int parent, bool mirrored, bool shown)
        {
            double rear = body.Radius * body.Taper, front = body.Radius;
            double side = mirrored ? -1 : 1;
            var socket = (double[])slot.At.Clone();
            if (parent < 0)
            {
                double across = rear + (front - rear) * (socket[2] * 0.5 + 0.5);
                socket = new[] { socket[0] * across, socket[1] * across * body.Flatten, socket[2] * body.Length * 0.5 };
            }
            socket[0] *= side;
            var origin = parent < 0 ? new double[3] : Limbs[parent].End;
            var turn = parent < 0 ? Quat.Identity : Limbs[parent].EndRot;
            var pos = Quat.Add(origin, Quat.Rotate(turn, socket));
            var rot = Quat.Mul(turn, Quat.EulerZxy(slot.Dir[0], slot.Dir[1] * side, slot.Dir[2] * side));
            var limb = new Limb { Slot = slot, Part = part, Parent = parent, Mirrored = mirrored, Shown = shown, Start = pos };
            var idle = part.Poses.Length > 0 ? part.Poses[0] : Array.Empty<double[]>();
            for (int s = 0; s < part.Segments.Count; s++)
            {
                var seg = part.Segments[s];
                var a = s < idle.Length && idle[s].Length == 3 ? idle[s] : new double[3];
                rot = Quat.Mul(rot, Quat.EulerZxy(a[0], a[1] * side, a[2] * side));
                double radius = Math.Max(seg.RadiusStart, seg.RadiusEnd) * slot.Scale * slot.Thickness;
                if (shown) Grow(pos, radius);
                pos = Quat.Add(pos, Quat.Rotate(rot, new[] { 0, 0, seg.Length * slot.Scale }));
                if (shown) Grow(pos, radius);
            }
            limb.End = pos;
            limb.EndRot = rot;
            Limbs.Add(limb);
            return Limbs.Count - 1;
        }

        void Grow(double[] p, double radius)
        {
            for (int i = 0; i < 3; i++)
            {
                Min[i] = Math.Min(Min[i], p[i] - radius);
                Max[i] = Math.Max(Max[i], p[i] + radius);
            }
        }

        // Horns aren't counted: a beak is one. The slots list them.
        static readonly (string role, string one, string many)[] Counted =
        {
            ("leg", "leg", "legs"), ("arm", "arm", "arms"), ("head", "head", "heads"), ("tail", "tail", "tails"),
        };

        /// <summary>The shape in a line: "4 legs in 2 pairs, 2 arms, 1 head; 2.1 m tall, 1.6 m long".</summary>
        public string Words(double size)
        {
            var shown = Limbs.Where(l => l.Shown).ToList();
            var counts = new List<string>();
            foreach (var (role, one, many) in Counted)
            {
                int n = shown.Count(l => l.Part.Role == role);
                if (n == 0) continue;
                int pairs = shown.Count(l => l.Part.Role == role && l.Slot.Mirror && !l.Mirrored);
                string paired = role == "leg" && n >= 4 && pairs * 2 == n ? $" in {pairs} pairs" : "";
                counts.Add($"{n} {(n == 1 ? one : many)}{paired}");
            }
            if (counts.Count == 0) counts.Add("no limbs");
            return $"{string.Join(", ", counts)}; {Workbench.Num(Math.Round((Max[1] - Min[1]) * size, 1))} m tall, " +
                $"{Workbench.Num(Math.Round((Max[2] - Min[2]) * size, 1))} m long, {Workbench.Num(Math.Round((Max[0] - Min[0]) * size, 1))} m wide";
        }

        /// <summary>A point as "(x, y, z) m", to the centimetre.</summary>
        public static string Metres(double[] p) => "(" + string.Join(", ", p.Select(v => Workbench.Num(Math.Round(v, 2)))) + ") m";

        // Quaternions as [x, y, z, w], in doubles; Unity.Mathematics' sums.
        static class Quat
        {
            public static readonly double[] Identity = { 0, 0, 0, 1 };

            public static double[] Mul(double[] a, double[] b) => new[]
            {
                a[3] * b[0] + a[0] * b[3] + a[1] * b[2] - a[2] * b[1],
                a[3] * b[1] - a[0] * b[2] + a[1] * b[3] + a[2] * b[0],
                a[3] * b[2] + a[0] * b[1] - a[1] * b[0] + a[2] * b[3],
                a[3] * b[3] - a[0] * b[0] - a[1] * b[1] - a[2] * b[2],
            };

            static double[] Axis(int axis, double degrees)
            {
                double half = degrees * Math.PI / 360;
                var q = new[] { 0, 0, 0, Math.Cos(half) };
                q[axis] = Math.Sin(half);
                return q;
            }

            /// <summary>Turned about z, then x, then y, as quaternion.EulerZXY.</summary>
            public static double[] EulerZxy(double x, double y, double z) => Mul(Axis(1, y), Mul(Axis(0, x), Axis(2, z)));

            public static double[] Rotate(double[] q, double[] v)
            {
                var p = Mul(Mul(q, new[] { v[0], v[1], v[2], 0 }), new[] { -q[0], -q[1], -q[2], q[3] });
                return new[] { p[0], p[1], p[2] };
            }

            public static double[] Add(double[] a, double[] b) => new[] { a[0] + b[0], a[1] + b[1], a[2] + b[2] };
        }
    }
}
