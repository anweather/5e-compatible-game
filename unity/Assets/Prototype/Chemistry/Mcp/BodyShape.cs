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
        public readonly double[] Min = new double[3], Max = new double[3];

        BodyShape() { }

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
                copies[slot.Id] = part == null ? new List<int>() : shape.Copies(body, slot, part, copies, slot.Style <= 0 || firstOfStyle.Add(slot.Style));
            }
            return shape;
        }

        List<int> Copies(SpeciesDef body, SlotDef slot, PartDef part, Dictionary<string, List<int>> copies, bool styleShown)
        {
            var mine = new List<int>();
            var parents = slot.On != null && copies.TryGetValue(slot.On, out var on) ? on : new List<int> { -1 };
            foreach (int parent in parents)
            {
                bool parentMirrored = parent >= 0 && Limbs[parent].Mirrored;
                bool shown = styleShown && (parent < 0 || Limbs[parent].Shown);
                mine.Add(Add(body, slot, part, parent, parentMirrored, shown));
                if (slot.Mirror) mine.Add(Add(body, slot, part, parent, !parentMirrored, shown));
            }
            return mine;
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

        static readonly (string role, string one, string many)[] Counted =
        {
            ("leg", "leg", "legs"), ("arm", "arm", "arms"), ("head", "head", "heads"), ("tail", "tail", "tails"),
        };

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

        public static string Metres(double[] p) => "(" + string.Join(", ", p.Select(v => Workbench.Num(Math.Round(v, 2)))) + ") m";

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
