// A world sketch (J-085; docs/design/world-squares.md, "Draw first"): biome shapes drawn freely on the world, the paper's
// JSON (tools/world-paper/index.html: cleanSketch, compileSketch, sketchAt) as a map.json field. Points are in world
// squares from the plan grid's north-west corner (wall rows included), one closed outline a shape, later shapes on top;
// a sketch drawn for another size stretches to fit. Read in integers: points are kept in hundredths of a world square
// (the paper rounds them so), placed in sixteenths of a cell, and the inside test and distances are longs.
using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;

namespace Chem
{
    public sealed class WorldSketch
    {
        /// <summary>The biomes a shape may be drawn as (the paper's SKETCH_BIOMES): "ocean" is a lake or the sea.</summary>
        public static readonly string[] Biomes = { "ocean", "foggy-marsh", "grassland", "temperate", "salt-flat", "ashlands", "tundra", "mountains" };

        public const int DefaultSoft = 96, MaxShapes = 64, MaxPoints = 240;

        public sealed class Shape
        {
            public string Biome;
            /// <summary>The outline's points in hundredths of a world square, x east and y south.</summary>
            public long[] X, Y;
        }

        public string Name = "";
        /// <summary>The world it was drawn for, in world squares.</summary>
        public int W = 12, H = 10;
        public bool Wrap = true;
        /// <summary>How many cells a shape's edge blends over.</summary>
        public int Soft = DefaultSoft;
        public List<Shape> Shapes = new List<Shape>();

        /// <summary>A sketch from map.json, null when there is none or it's off ("fit": false). Throws FormatException for one it can't read.</summary>
        public static WorldSketch Read(JToken token)
        {
            if (token == null || token.Type == JTokenType.Null) return null;
            if (!(token is JObject o)) throw new FormatException("sketch: not an object");
            if ((bool?)o["fit"] == false) return null;
            var s = new WorldSketch { Name = (string)o["name"] ?? "" };
            if (o["world"] is JObject world)
            {
                s.W = (int?)world["w"] ?? s.W;
                s.H = (int?)world["h"] ?? s.H;
                s.Wrap = ((string)world["wrap"] ?? "ew") == "ew";
            }
            if (s.W < 1 || s.H < 1 || s.W > 24 || s.H > 24) throw new FormatException($"sketch: a world {s.W} x {s.H} world squares is out of 1..24");
            s.Soft = (int?)o["soft"] ?? DefaultSoft;
            if (s.Soft < 24 || s.Soft > 240) throw new FormatException($"sketch: soft {s.Soft} is out of 24..240 cells");
            var shapes = o["shapes"] as JArray ?? throw new FormatException("sketch: no \"shapes\"");
            if (shapes.Count > MaxShapes) throw new FormatException($"sketch: {shapes.Count} shapes, more than {MaxShapes}");
            foreach (var t in shapes)
            {
                string biome = (string)t["biome"];
                if (Array.IndexOf(Biomes, biome) < 0) throw new FormatException($"sketch: \"{biome}\" isn't one of {string.Join(", ", Biomes)}");
                var pts = t["pts"] as JArray ?? throw new FormatException($"sketch: a {biome} shape has no \"pts\"");
                if (pts.Count < 3 || pts.Count > MaxPoints) throw new FormatException($"sketch: a {biome} shape has {pts.Count} points, not 3..{MaxPoints}");
                var shape = new Shape { Biome = biome, X = new long[pts.Count], Y = new long[pts.Count] };
                for (int k = 0; k < pts.Count; k++)
                {
                    shape.X[k] = Hundredths((double)pts[k][0]);
                    shape.Y[k] = Hundredths((double)pts[k][1]);
                }
                s.Shapes.Add(shape);
            }
            return s;
        }

        static long Hundredths(double v) => (long)Math.Round(v * 100, MidpointRounding.AwayFromZero);
    }

    /// <summary>A sketch laid on a plan: its outlines in sixteenths of a cell, and the membership of a point in each.</summary>
    internal sealed class PlacedSketch
    {
        const long One = BiomeMath.One;
        const int Sub = 16, WarpLattice = 144, WarpCells = 150, SaltWarpX = 0x5a01, SaltWarpY = 0x5a02;

        sealed class Outline
        {
            public int Biome; // a biomes.json row, or −1 for water (a lake or the sea), which shows the land field under it
            public long[] X, Y;
            public long X0, Y0, X1, Y1;
        }

        readonly Outline[] outlines;
        readonly string[] names;
        readonly int seed, soft;
        readonly long period; // cells round the world, 0 for a world that doesn't wrap

        public PlacedSketch(WorldSketch sketch, BiomeTable table, int seed, int gridW, int gridH, bool wrap)
        {
            this.seed = seed;
            soft = sketch.Soft;
            period = wrap ? (long)gridW * WorldPlan.SquareCells : 0;
            outlines = new Outline[sketch.Shapes.Count];
            names = new string[outlines.Length];
            for (int k = 0; k < outlines.Length; k++)
            {
                var s = sketch.Shapes[k];
                names[k] = s.Biome;
                int b = s.Biome == "ocean" ? -1 : table.IndexOf(s.Biome);
                var o = new Outline { Biome = b, X = new long[s.X.Length], Y = new long[s.Y.Length], X0 = long.MaxValue, Y0 = long.MaxValue, X1 = long.MinValue, Y1 = long.MinValue };
                for (int p = 0; p < s.X.Length; p++)
                {
                    o.X[p] = s.X[p] * gridW * WorldPlan.SquareCells * Sub / (100L * sketch.W);
                    o.Y[p] = s.Y[p] * gridH * WorldPlan.SquareCells * Sub / (100L * sketch.H);
                    o.X0 = Math.Min(o.X0, o.X[p]);
                    o.X1 = Math.Max(o.X1, o.X[p]);
                    o.Y0 = Math.Min(o.Y0, o.Y[p]);
                    o.Y1 = Math.Max(o.Y1, o.Y[p]);
                }
                outlines[k] = o;
            }
        }

        public int Count => outlines.Length;

        /// <summary>Shape <paramref name="k"/>'s biome as drawn ("ocean" for water).</summary>
        public string NameOf(int k) => names[k];

        /// <summary>Shape <paramref name="k"/>'s biome row, or −1 for water.</summary>
        public int BiomeOf(int k) => outlines[k].Biome;

        /// <summary>
        /// Each shape's membership at plan cell (<paramref name="x"/>, <paramref name="y"/>) (y south) into
        /// <paramref name="into"/>, Q16: 1 well inside, 0 well outside, a smoothstep over <paramref name="blendCells"/> (at
        /// least the sketch's own soft edge) centred on the outline, after the climate-scale warp; round a world that wraps
        /// a shape is read a world east and west too.
        /// </summary>
        public void Membership(int x, int y, int blendCells, Span<long> into)
        {
            if (period > 0) x = (int)BiomeMath.Mod(x, period); // the same a world round, bit for bit
            long xq = (long)x * One + One / 2, yq = (long)y * One + One / 2;
            long wx = BiomeMath.Centred(seed, SaltWarpX, xq, yq, WarpLattice, period, 0) * WarpCells * Sub / One;
            long wy = BiomeMath.Centred(seed, SaltWarpY, xq, yq, WarpLattice, period, 0) * WarpCells * Sub / One;
            long px = (long)x * Sub + Sub / 2 + wx, py = (long)y * Sub + Sub / 2 + wy;
            long width = (long)Math.Max(soft, blendCells) * Sub, pad = width / 2 + Sub;
            for (int k = 0; k < outlines.Length; k++)
            {
                var o = outlines[k];
                long best = 0;
                for (int c = period > 0 ? -1 : 0; c <= (period > 0 ? 1 : 0); c++)
                {
                    long qx = px + c * period * Sub;
                    if (qx < o.X0 - pad || qx > o.X1 + pad || py < o.Y0 - pad || py > o.Y1 + pad) continue;
                    long m = BiomeMath.Smoothstep(BiomeMath.Clamp01(One / 2 + SignedDistance(o, qx, py) * One / width));
                    best = Math.Max(best, m);
                }
                into[k] = best;
            }
        }

        // The signed distance from a point to a closed outline, positive inside (the paper's polySD), in sixteenths of a cell.
        static long SignedDistance(Outline o, long x, long y)
        {
            bool inside = false;
            long d2 = long.MaxValue;
            int n = o.X.Length;
            for (int i = 0, j = n - 1; i < n; j = i++)
            {
                long xi = o.X[i], yi = o.Y[i], xj = o.X[j], yj = o.Y[j];
                long dx = xj - xi, dy = yj - yi;
                if ((yi > y) != (yj > y))
                {
                    // x < xi + dx (y − yi) / dy, multiplied through by dy (its sign turns the comparison).
                    long lhs = (x - xi) * dy, rhs = dx * (y - yi);
                    if (dy > 0 ? lhs < rhs : lhs > rhs) inside = !inside;
                }
                long len = dx * dx + dy * dy;
                long t = len > 0 ? BiomeMath.Clamp01(((x - xi) * dx + (y - yi) * dy) * One / len) : 0;
                long ex = xi + dx * t / One - x, ey = yi + dy * t / One - y;
                d2 = Math.Min(d2, ex * ex + ey * ey);
            }
            long d = BiomeMath.Sqrt(d2);
            return inside ? d : -d;
        }
    }
}
