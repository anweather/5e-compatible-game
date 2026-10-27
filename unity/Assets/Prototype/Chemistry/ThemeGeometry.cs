// Parametric geometry for world-theme blueprints (D11): noisy ellipses
// (zones, lakes, mountains, islands) and width-profiled polylines (rivers),
// in cells. Port of PocketTactics' src/core/world/theme/geometry.ts, bit for
// bit (WorldGenTests). Only exactly rounded operations (+ - * /, Math.Sqrt)
// and the series sine, cosine and arctangent here, so a blueprint is the same
// on every machine: Math.Sin and co round differently between runtimes.
using System;
using System.Collections.Generic;

namespace Chem
{
    /// <summary>A point in cells (x east, y south).</summary>
    public readonly struct Pt
    {
        public readonly double X, Y;

        public Pt(double x, double y)
        {
            X = x;
            Y = y;
        }
    }

    /// <summary>An axis-aligned box in cells.</summary>
    public readonly struct GeoBox
    {
        public readonly double X0, Y0, X1, Y1;

        public GeoBox(double x0, double y0, double x1, double y1)
        {
            X0 = x0;
            Y0 = y0;
            X1 = x1;
            Y1 = y1;
        }

        public bool Meets(GeoBox b) => X0 <= b.X1 && b.X0 <= X1 && Y0 <= b.Y1 && b.Y0 <= Y1;
    }

    /// <summary>One wobble of a blob's outline: radius(theta) *= 1 + amp * cos(k * theta + phase).</summary>
    public readonly struct Harmonic
    {
        public readonly int K;
        public readonly double Amp, Phase;

        public Harmonic(int k, double amp, double phase)
        {
            K = k;
            Amp = amp;
            Phase = phase;
        }
    }

    /// <summary>A noisy ellipse: centre, radii, rotation of the X radius from +x, and its outline's wobble.</summary>
    public sealed class Blob
    {
        public double Cx, Cy, Rx, Ry, Angle;
        public Harmonic[] Harmonics = Array.Empty<Harmonic>();

        public Blob Scaled(double sx, double sy) =>
            new Blob { Cx = Cx, Cy = Cy, Rx = Rx * sx, Ry = Ry * sy, Angle = Angle, Harmonics = Harmonics };
    }

    /// <summary>A polyline with a half width at each point (cells).</summary>
    public sealed class GeoPath
    {
        public Pt[] Points = Array.Empty<Pt>();
        public double[] Half = Array.Empty<double>();
    }

    /// <summary>The solver's seeded stream (mulberry32). Never used per cell.</summary>
    public sealed class ThemeRng
    {
        uint s;

        public ThemeRng(uint seed) => s = seed;

        public double Next()
        {
            unchecked
            {
                s += 0x6d2b79f5u;
                uint t = s;
                t = (t ^ (t >> 15)) * (t | 1u);
                t ^= t + (t ^ (t >> 7)) * (t | 61u);
                return (t ^ (t >> 14)) / 4294967296.0;
            }
        }

        public double Range(double a, double b) => a + (b - a) * Next();
        /// <summary>A whole number in [0, n).</summary>
        public int Below(int n) => (int)Math.Floor(Next() * n);
    }

    public static class ThemeGeometry
    {
        const double Tau = 6.283185307179586;
        const double HalfPi = 1.5707963267948966;

        /// <summary>sqrt(x² + y²) from exact operations.</summary>
        public static double Hypot(double x, double y) => Math.Sqrt(x * x + y * y);

        /// <summary>JavaScript's Math.round: halves round up.</summary>
        static double RoundJs(double x) => Math.Floor(x + 0.5);

        /// <summary>sin by range reduction and a Taylor series to x^17 (error under 1e-12).</summary>
        public static double Sin(double x)
        {
            double r = x - Tau * RoundJs(x / Tau);
            if (r > HalfPi) r = Math.PI - r;
            else if (r < -HalfPi) r = -Math.PI - r;
            double r2 = r * r, term = r, sum = r;
            for (int n = 1; n <= 8; n++)
            {
                term *= -r2 / ((2 * n) * (2 * n + 1));
                sum += term;
            }
            return sum;
        }

        public static double Cos(double x) => Sin(x + HalfPi);

        /// <summary>atan by two half-angle reductions and a series (error under 1e-15).</summary>
        static double Atan(double v)
        {
            double t = v < 0 ? -v : v;
            bool inv = t > 1;
            if (inv) t = 1 / t;
            for (int i = 0; i < 2; i++) t /= 1 + Math.Sqrt(1 + t * t);
            double t2 = t * t, pow = t, sum = t;
            for (int n = 1; n <= 10; n++)
            {
                pow *= -t2;
                sum += pow / (2 * n + 1);
            }
            double a = sum * 4;
            if (inv) a = HalfPi - a;
            return v < 0 ? -a : a;
        }

        public static double Atan2(double y, double x)
        {
            if (x > 0) return Atan(y / x);
            if (x < 0) return y >= 0 ? Atan(y / x) + Math.PI : Atan(y / x) - Math.PI;
            if (y > 0) return HalfPi;
            return y < 0 ? -HalfPi : 0;
        }

        public static double Smoothstep01(double t)
        {
            double c = Math.Min(1, Math.Max(0, t));
            return c * c * (3 - 2 * c);
        }

        // ---------- noisy ellipses ----------

        public static Blob MakeBlob(ThemeRng rng, double cx, double cy, double rx, double ry, double angle, double wobble = 0.14)
        {
            int[] ks = { 2, 3, 5 };
            var harmonics = new Harmonic[ks.Length];
            for (int i = 0; i < ks.Length; i++)
            {
                double amp = rng.Range(0.3, 1) * wobble / (i + 1);
                double phase = rng.Range(0, Math.PI * 2);
                harmonics[i] = new Harmonic(ks[i], amp, phase);
            }
            return new Blob { Cx = cx, Cy = cy, Rx = rx, Ry = ry, Angle = angle, Harmonics = harmonics };
        }

        /// <summary>Normalised radial distance: under 1 inside, 1 on the (wobbly) outline.</summary>
        public static double Rho(Blob b, double x, double y)
        {
            double dx = x - b.Cx, dy = y - b.Cy;
            double c = Cos(b.Angle), s = Sin(b.Angle);
            double lx = (dx * c + dy * s) / b.Rx;
            double ly = (-dx * s + dy * c) / b.Ry;
            double q = Hypot(lx, ly);
            if (q == 0) return 0;
            double th = Atan2(ly, lx);
            double r = 1;
            foreach (var h in b.Harmonics) r += h.Amp * Cos(h.K * th + h.Phase);
            return q / Math.Max(0.3, r);
        }

        public static double Reach(Blob b)
        {
            double w = 1;
            foreach (var h in b.Harmonics) w += Math.Abs(h.Amp);
            return Math.Max(b.Rx, b.Ry) * w;
        }

        public static GeoBox Bounds(Blob b, double pad = 0)
        {
            double r = Reach(b) + pad;
            return new GeoBox(b.Cx - r, b.Cy - r, b.Cx + r, b.Cy + r);
        }

        // ---------- polylines ----------

        public static GeoBox Bounds(GeoPath p, double pad = 0)
        {
            double x0 = double.PositiveInfinity, y0 = double.PositiveInfinity, x1 = double.NegativeInfinity, y1 = double.NegativeInfinity;
            for (int i = 0; i < p.Points.Length; i++)
            {
                var q = p.Points[i];
                double h = p.Half[i] + pad;
                x0 = Math.Min(x0, q.X - h);
                y0 = Math.Min(y0, q.Y - h);
                x1 = Math.Max(x1, q.X + h);
                y1 = Math.Max(y1, q.Y + h);
            }
            return new GeoBox(x0, y0, x1, y1);
        }

        /// <summary>Segments of a path whose padded box meets <paramref name="box"/> (indices of segment starts).</summary>
        public static List<int> SegmentsIn(GeoPath p, GeoBox box, double pad)
        {
            var output = new List<int>();
            for (int i = 0; i + 1 < p.Points.Length; i++)
            {
                var a = p.Points[i];
                var b = p.Points[i + 1];
                double h = Math.Max(p.Half[i], p.Half[i + 1]) + pad;
                if (Math.Max(a.X, b.X) + h < box.X0 || Math.Min(a.X, b.X) - h > box.X1) continue;
                if (Math.Max(a.Y, b.Y) + h < box.Y0 || Math.Min(a.Y, b.Y) - h > box.Y1) continue;
                output.Add(i);
            }
            return output;
        }

        /// <summary>Distance from (x, y) to the path's middle line over the given segments, and its half width there.</summary>
        public static (double d, double half) Distance(GeoPath p, IReadOnlyList<int> segs, double x, double y)
        {
            double best = double.PositiveInfinity, half = 0;
            foreach (int i in segs)
            {
                var a = p.Points[i];
                var b = p.Points[i + 1];
                double vx = b.X - a.X, vy = b.Y - a.Y;
                double len2 = vx * vx + vy * vy;
                if (len2 == 0) len2 = 1;
                double t = Math.Max(0, Math.Min(1, ((x - a.X) * vx + (y - a.Y) * vy) / len2));
                double d = Hypot(x - (a.X + vx * t), y - (a.Y + vy * t));
                if (d < best)
                {
                    best = d;
                    half = p.Half[i] + (p.Half[i + 1] - p.Half[i]) * t;
                }
            }
            return (best, half);
        }

        public static double Length(IReadOnlyList<Pt> pts)
        {
            double s = 0;
            for (int i = 0; i + 1 < pts.Count; i++) s += Hypot(pts[i + 1].X - pts[i].X, pts[i + 1].Y - pts[i].Y);
            return s;
        }

        /// <summary>Distance from p to the segment a-b.</summary>
        public static double SegmentDistance(Pt p, Pt a, Pt b)
        {
            double vx = b.X - a.X, vy = b.Y - a.Y;
            double l2 = vx * vx + vy * vy;
            if (l2 == 0) l2 = 1;
            double t = Math.Max(0, Math.Min(1, ((p.X - a.X) * vx + (p.Y - a.Y) * vy) / l2));
            return Hypot(p.X - (a.X + vx * t), p.Y - (a.Y + vy * t));
        }

        static double LengthOr1(double l) => l == 0 ? 1 : l;

        /// <summary>
        /// A meandering curve through <paramref name="anchors"/> (at least two), resampled every
        /// <paramref name="step"/> cells. Each leg is bent by recursive midpoint displacement (the large
        /// bends) plus a sum of sines along it (the meanders), scaled by <paramref name="meander"/>
        /// (0 straight, 1 strongly winding). Anchors are kept exactly.
        /// </summary>
        public static List<Pt> MeanderCurve(ThemeRng rng, IReadOnlyList<Pt> anchors, double meander, double step = 4)
        {
            var output = new List<Pt> { anchors[0] };
            for (int a = 0; a + 1 < anchors.Count; a++)
            {
                var p = anchors[a];
                var q = anchors[a + 1];
                double len = Hypot(q.X - p.X, q.Y - p.Y);
                if (len < 1e-6) continue;
                var ctrl = Bends(rng, p, q, meander);
                AddMeanders(rng, output, ctrl, p, q, meander, step);
            }
            return output;
        }

        // Large bends: midpoint displacement to depth 3.
        static List<Pt> Bends(ThemeRng rng, Pt p, Pt q, double meander)
        {
            var ctrl = new List<Pt> { p, q };
            double amp = 0.22 * meander;
            for (int depth = 0; depth < 3; depth++)
            {
                var next = new List<Pt> { ctrl[0] };
                for (int i = 0; i + 1 < ctrl.Count; i++)
                {
                    var u = ctrl[i];
                    var v = ctrl[i + 1];
                    double l = LengthOr1(Hypot(v.X - u.X, v.Y - u.Y));
                    double h = rng.Range(-1, 1) * amp * l;
                    next.Add(new Pt((u.X + v.X) / 2 - ((v.Y - u.Y) / l) * h, (u.Y + v.Y) / 2 + ((v.X - u.X) / l) * h));
                    next.Add(v);
                }
                ctrl = next;
                amp *= 0.6;
            }
            return ctrl;
        }

        // Resample a leg, adding sine meanders across its own direction (tapered to nothing at the anchors).
        static void AddMeanders(ThemeRng rng, List<Pt> output, List<Pt> ctrl, Pt p, Pt q, double meander, double step)
        {
            double len = Hypot(q.X - p.X, q.Y - p.Y);
            double nx = -(q.Y - p.Y) / len, ny = (q.X - p.X) / len;
            double f1 = rng.Range(2.5, 4.5), a1 = rng.Range(0.5, 1) * 0.055 * meander, ph1 = rng.Range(0, Math.PI * 2);
            double f2 = rng.Range(6, 9), a2 = rng.Range(0.5, 1) * 0.022 * meander, ph2 = rng.Range(0, Math.PI * 2);
            double ctrlLen = Length(ctrl);
            int n = Math.Max(2, (int)Math.Ceiling(ctrlLen / step));
            int seg = 0;
            double segStart = 0;
            for (int k = 1; k <= n; k++)
            {
                double s = (double)k / n * ctrlLen;
                while (seg < ctrl.Count - 2 && segStart + Hypot(ctrl[seg + 1].X - ctrl[seg].X, ctrl[seg + 1].Y - ctrl[seg].Y) < s)
                {
                    segStart += Hypot(ctrl[seg + 1].X - ctrl[seg].X, ctrl[seg + 1].Y - ctrl[seg].Y);
                    seg++;
                }
                var u = ctrl[seg];
                var v = ctrl[seg + 1];
                double l = LengthOr1(Hypot(v.X - u.X, v.Y - u.Y));
                double t = Math.Min(1, (s - segStart) / l);
                double t01 = (double)k / n;
                double env = Sin(Math.PI * t01);
                double off = Sin(t01 * Math.PI * 2 * f1 + ph1) * a1 * len * env;
                off += Sin(t01 * Math.PI * 2 * f2 + ph2) * a2 * len * env;
                output.Add(k == n ? q : new Pt(u.X + (v.X - u.X) * t + nx * off, u.Y + (v.Y - u.Y) * t + ny * off));
            }
        }
    }
}
