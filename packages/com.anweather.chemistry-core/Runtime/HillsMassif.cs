// A hills map's alpine massif (docs/design/alpine.md): a high meadow plateau in the middle of the map, ringed by
// mountains, with passes through the ring where the main roads cross it, and foothills that carry the land down from the
// mountains' foot to the rolling hills. Its rise is a pure function of a cell's position, the seed and map.json, in
// integers only, added to the hills' own land (HillsLand.At), so every peer makes the same chunk in any order.
//
// - Distances are from the massif's centre, the short way round the torus, and wander by seeded noise (Warp cells) so
//   the ring isn't a circle. Inside Meadow it's the meadow: the uplift (MeadowHeight) over the hills' own rolling.
// - Between Meadow and Foot is the ring: a ridge that rises to its crest at Crest, ridged noise on it for peaks and cols,
//   up to Peaks land units over the meadow.
// - Between Foot and Reach are the foothills: the uplift eases down to nothing, with rolling bumps of their own.
// - A pass is where a main road crosses the ring: within PassWidth cells of the road's line the ring sinks to
//   PassKeep percent of its height, a valley along the road with the col at the crest. The road's distance is read from a
//   coarse grid worked out once when the generator is made (every GridStep cells over the ring), so a cell never walks
//   the roads' lines.
using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;

namespace Chem
{
    /// <summary>The alpine massif of a hills map: its rise over the hills at any cell, in land units (0.125 m).</summary>
    public sealed class HillsMassif
    {
        const long One = BiomeMath.One;
        const int SaltWarp = 0x5a01, SaltWarp2 = 0x5a02, SaltRidge = 0x5a10, SaltFoot = 0x5a20, SaltSpring = 0x5a30, SaltGorge = 0x5a31;
        /// <summary>How far the gorge wanders either side of its line, in cells.</summary>
        public const int GorgeWander = 70;
        /// <summary>The pass grid's spacing in cells.</summary>
        public const int GridStep = 16;

        /// <summary>The massif's centre, in map cells.</summary>
        public readonly int CentreX, CentreY;
        /// <summary>Radii in cells from the centre: the meadow's edge, the crest, the mountains' foot, the foothills' end.</summary>
        public readonly int Meadow, Crest, Foot, Reach;
        /// <summary>How far the radii wander, in cells.</summary>
        public readonly int Warp;
        /// <summary>The meadow's rise over the hills, and the most the ring's peaks stand over the meadow, in land units.</summary>
        public readonly int MeadowHeight, Peaks;
        /// <summary>The foothills' own bumps, in land units at most.</summary>
        public readonly int FoothillRelief;
        /// <summary>How wide a pass's valley is either side of its road, in cells, and how much of the ring stands at its col, in percent.</summary>
        public readonly int PassWidth, PassKeep;
        readonly int seed, w, h;

        // The pass grid: the pass weight (Q16, 1 on a main road's line, 0 PassWidth away) at every GridStep cells over the
        // ring's box, from (gx0, gy0) in cells from the centre; null until the passes are set.
        long[] pass;
        int gx0, gy0, gw, gh;

        /// <summary>A massif's shape as map.json gives it (radii in cells, heights in land units).</summary>
        public sealed class Shape
        {
            public int CentreX, CentreY, Meadow = 700, Crest = 1050, Foot = 1450, Reach = 2500, Warp = 220;
            public int MeadowHeight = 1000, Peaks = 2000, FoothillRelief = 160, PassWidth = 260, PassKeep = 12;
        }

        public HillsMassif(int seed, int cellsW, int cellsH, Shape s)
        {
            if (!(0 < s.Meadow && s.Meadow < s.Crest && s.Crest < s.Foot && s.Foot < s.Reach))
                throw new FormatException($"{MapFile.FileName}: a massif's radii must grow: meadow {s.Meadow} < crest {s.Crest} < foot {s.Foot} < reach {s.Reach}");
            if (2 * (s.Reach + s.Warp) >= Math.Min(cellsW, cellsH))
                throw new FormatException($"{MapFile.FileName}: a massif reaching {s.Reach} + {s.Warp} cells doesn't fit the map's {cellsW} x {cellsH} cells");
            if (s.PassKeep < 0 || s.PassKeep > 100) throw new FormatException($"{MapFile.FileName}: a massif's passKeep is a percent, not {s.PassKeep}");
            if (s.PassWidth < 1) throw new FormatException($"{MapFile.FileName}: a massif's passWidth must be at least a cell");
            this.seed = seed;
            w = cellsW;
            h = cellsH;
            CentreX = PlanGrid.Mod(s.CentreX, cellsW);
            CentreY = PlanGrid.Mod(s.CentreY, cellsH);
            Meadow = s.Meadow;
            Crest = s.Crest;
            Foot = s.Foot;
            Reach = s.Reach;
            Warp = s.Warp;
            MeadowHeight = s.MeadowHeight;
            Peaks = s.Peaks;
            FoothillRelief = s.FoothillRelief;
            PassWidth = s.PassWidth;
            PassKeep = s.PassKeep;
        }

        /// <summary>map.json's "hills" "massif": { "centre": [x, y], "meadow", "crest", "foot", "reach", "warp", "meadowHeight", "peaks", "foothills", "passWidth", "passKeep" }, or null.</summary>
        public static HillsMassif Read(JObject o, int seed, int cellsW, int cellsH)
        {
            if (o == null) return null;
            var c = o["centre"] as JArray;
            var d = new Shape();
            int I(string key, int fallback) => (int?)o[key] ?? fallback;
            var s = new Shape
            {
                CentreX = c != null ? (int)c[0] : cellsW / 2,
                CentreY = c != null ? (int)c[1] : cellsH / 2,
                Meadow = I("meadow", d.Meadow),
                Crest = I("crest", d.Crest),
                Foot = I("foot", d.Foot),
                Reach = I("reach", d.Reach),
                Warp = I("warp", d.Warp),
                MeadowHeight = I("meadowHeight", d.MeadowHeight),
                Peaks = I("peaks", d.Peaks),
                FoothillRelief = I("foothills", d.FoothillRelief),
                PassWidth = I("passWidth", d.PassWidth),
                PassKeep = I("passKeep", d.PassKeep),
            };
            return new HillsMassif(seed, cellsW, cellsH, s);
        }

        /// <summary>The most the massif can add anywhere, in land units.</summary>
        public int Highest => MeadowHeight + Peaks + FoothillRelief;

        // A cell's offset from the centre the short way round, in cells.
        (int dx, int dy) Offset(int x, int y)
        {
            int dx = PlanGrid.Mod(x - CentreX, w), dy = PlanGrid.Mod(y - CentreY, h);
            if (dx >= w / 2) dx -= w;
            if (dy >= h / 2) dy -= h;
            return (dx, dy);
        }

        /// <summary>Whether a cell is anywhere the massif could reach (within its reach and wander of the centre): outside it, it adds nothing.</summary>
        public bool Touches(int x, int y)
        {
            var (dx, dy) = Offset(x, y);
            long far = Reach + Warp + 1;
            return (long)dx * dx + (long)dy * dy < far * far;
        }

        /// <summary>A cell's wandering distance from the centre, Q16 cells (only meaningful where it <see cref="Touches"/>).</summary>
        public long Radius(int x, int y)
        {
            var (dx, dy) = Offset(x, y);
            long px = (long)(CentreX + dx) * One + One / 2, py = (long)(CentreY + dy) * One + One / 2;
            // At most Warp cells either way: three parts broad, one part fine.
            long warp = (3 * BiomeMath.Centred(seed, SaltWarp, px, py, 640) + BiomeMath.Centred(seed, SaltWarp2, px, py, 210)) * Warp / 4;
            return BiomeMath.Length((long)dx * One, (long)dy * One) + warp;
        }

        /// <summary>The ring's weight at a radius, Q16: 0 on the meadow and past the foot, 1 on the crest.</summary>
        public long Ring(long r)
        {
            if (r <= (long)Meadow * One || r >= (long)Foot * One) return 0;
            if (r < (long)Crest * One) return BiomeMath.Smoothstep((r - (long)Meadow * One) / (Crest - Meadow));
            return BiomeMath.Smoothstep(((long)Foot * One - r) / (Foot - Crest));
        }

        /// <summary>Whether the ring stands at a cell at all (no road but a main one is laid where it does).</summary>
        public bool InRing(int x, int y) => Touches(x, y) && Ring(Radius(x, y)) > 0;

        /// <summary>The uplift's share at a radius, Q16: 1 to the foot, easing down to 0 at the foothills' end.</summary>
        public long Uplift(long r)
        {
            if (r <= (long)Foot * One) return One;
            if (r >= (long)Reach * One) return 0;
            // Linear with eased ends (a fifth of the way at each), so the foothills' steepest is 1.25 times their mean.
            long t = ((long)Reach * One - r) / (Reach - Foot);
            const long e = One / 5;
            if (t < e) return t * t / (2 * e) * One / (One - e);
            if (t > One - e)
            {
                long u = One - t;
                return One - u * u / (2 * e) * One / (One - e);
            }
            return (t - e / 2) * One / (One - e);
        }

        // The foothills' own bumps' weight, Q16: nothing at the foot or the end, most half way.
        long FootBand(long r)
        {
            if (r <= (long)Foot * One || r >= (long)Reach * One) return 0;
            long t = (r - (long)Foot * One) / (Reach - Foot);
            return 4 * t * (One - t) / One;
        }

        /// <summary>The massif's rise at a cell over the hills' land, in land units: 0 outside its reach (the gorge not cut).</summary>
        public int At(int x, int y) => (int)(Rise(x, y, out _, out _) / One);

        /// <summary>
        /// The land at a cell given the hills' own land there (<paramref name="rolling"/>): the massif's rise on it, and the
        /// gorge cut down to its floor where it runs.
        /// </summary>
        public int Land(int x, int y, int rolling)
        {
            long rise = Rise(x, y, out long gorge, out long along);
            int land = rolling + (int)(rise / One);
            if (gorge <= 0) return land;
            int floor = GorgeFloor(along);
            if (land <= floor) return land;
            return land - (int)((land - floor) * gorge / One);
        }

        // The rise in Q16 land units, the gorge's weight here (Q16) and how far along the gorge's line it is (Q16 cells).
        long Rise(int x, int y, out long gorge, out long along)
        {
            gorge = along = 0;
            if (!Touches(x, y)) return 0;
            var (dx, dy) = Offset(x, y);
            long px = (long)(CentreX + dx) * One + One / 2, py = (long)(CentreY + dy) * One + One / 2;
            long r = Radius(x, y);
            if (r >= (long)Reach * One) return 0;
            gorge = GorgeWeight(dx, dy, out along);
            long rise = Uplift(r) * MeadowHeight;
            long ring = Ring(r);
            if (ring > 0)
            {
                // Ridged noise: sharp crests where the noise crosses its middle, broad cols between.
                long ridge = 0;
                // Each octave on axes turned its own way, so no crest runs along the lattice.
                ridge += 55 * Ridged(SaltRidge, px, py, 360, 0);
                ridge += 30 * Ridged(SaltRidge + 1, px, py, 150, 1);
                ridge += 15 * Ridged(SaltRidge + 2, px, py, 60, 2);
                ridge /= 100;
                // 0.4 + 0.6 ridge²: the crest is a ridge everywhere, with peaks where its noise lines up.
                long shape = 2 * One / 5 + 3 * (ridge * ridge / One) / 5;
                long keep = One - PassWeight(dx, dy) * (100 - PassKeep) / 100;
                // The gorge takes the ring away along its line.
                keep = keep * (One - gorge) / One;
                rise += ring * shape / One * keep / One * Peaks;
            }
            long band = FootBand(r);
            if (band > 0 && FoothillRelief > 0) rise += band * BiomeMath.Noise(seed, SaltFoot, px, py, 300) / One * FoothillRelief;
            return rise;
        }

        // The gorge: a straight cut from the meadow's lowest point (gox, goy cells from the centre) out through the ring at
        // (ux, uy), Q16 unit, from GorgeFrom to GorgeTo cells along it; its floor is GorgeLevel (absolute land units) to the
        // crest (gorgeCrest along it), then falls GorgeFall Q16 units a cell.
        bool hasGorge;
        long gux, guy;
        int gox, goy, gorgeLevel, gorgeWidth, gorgeFrom, gorgeTo, gorgeCrest;
        long gorgeFall;

        /// <summary>The meadow's lowest point, where the gorge starts, in map cells.</summary>
        public (int x, int y) GorgeStart => (CentreX + gox, CentreY + goy);

        /// <summary>Whether a gorge is cut, its way out from the centre (Q16 unit) and its floor at the crest (land units).</summary>
        public bool HasGorge => hasGorge;
        public (long x, long y) GorgeWay => (gux, guy);
        public int GorgeLevel => gorgeLevel;
        public int GorgeWidth => gorgeWidth;

        /// <summary>The gorge's floor (land units) at a distance along its line, Q16 cells.</summary>
        public int GorgeFloor(long along)
        {
            long past = along - (long)gorgeCrest * One;
            if (past <= 0) return gorgeLevel;
            return gorgeLevel - (int)(past * gorgeFall / One / One);
        }

        // The gorge's weight at an offset from the centre, Q16: 1 on its line, 0 a width off it and past its ends.
        long GorgeWeight(int dx, int dy, out long along)
        {
            along = 0;
            if (!hasGorge) return 0;
            long qx = (long)(dx - gox) * One, qy = (long)(dy - goy) * One;
            along = (qx * gux + qy * guy) / One;
            long from = (long)gorgeFrom * One, to = (long)gorgeTo * One, fade = 100L * One;
            if (along <= from || along >= to) return 0;
            // It wanders either side of its line by up to GorgeWander cells (seeded noise along it), so it isn't ruled straight.
            long wander = BiomeMath.Centred(seed, SaltGorge, along, 0, 280) * GorgeWander;
            long across = Math.Abs((qx * guy - qy * gux) / One - wander);
            if (across >= (long)gorgeWidth * One) return 0;
            long weight = One - BiomeMath.Smoothstep(across / gorgeWidth);
            long ends = Math.Min(BiomeMath.Smoothstep((along - from) / 100), BiomeMath.Smoothstep((to - along) / 100));
            if (along - from >= fade && to - along >= fade) ends = One;
            return weight * ends / One;
        }

        /// <summary>
        /// Cuts the gorge: from the meadow's lowest point out the way, of the <paramref name="ways"/> ways round evenly,
        /// farthest from every pass (the middle of the widest run of ways no pass reaches), its floor <paramref name="lakeDepth"/> land units over
        /// the meadow's lowest land (so the meadow holds a lake that spills through it), falling <paramref name="fallPercent"/>
        /// percent past the crest. Call it once, after the passes and before any chunk is made.
        /// </summary>
        public void SetGorge(Func<int, int, int> rolling, int lakeDepth, int width, int fallPercent, int ways = 64)
        {
            // The meadow's lowest land: the hills' own there and the meadow's rise (no ring, no foothills inside it).
            int lowest = int.MaxValue;
            int inside = Meadow - Warp;
            for (int dy = -inside; dy <= inside; dy += 16)
                for (int dx = -inside; dx <= inside; dx += 16)
                {
                    if ((long)dx * dx + (long)dy * dy >= (long)inside * inside) continue;
                    int land = rolling(CentreX + dx, CentreY + dy) + MeadowHeight;
                    if (land >= lowest) continue;
                    lowest = land;
                    (gox, goy) = (dx, dy);
                }
            var dirs = Ways(ways);
            var free = new bool[ways];
            int reach = Foot + Warp + 200 + (int)(BiomeMath.Length((long)gox * One, (long)goy * One) / One);
            for (int k = 0; k < ways; k++)
            {
                free[k] = true;
                // Clear of the passes through the ring (a meadow road it crosses gets a ford).
                for (int t = 0; t <= reach && free[k]; t += 32)
                {
                    int px = gox + (int)(dirs[k].x * t / One), py = goy + (int)(dirs[k].y * t / One);
                    if ((long)px * px + (long)py * py < (long)(Meadow - Warp) * (Meadow - Warp)) continue;
                    free[k] = PassWeight(px, py) == 0;
                }
            }
            int best = FreestWay(free);
            if (best < 0) throw new FormatException($"{MapFile.FileName}: no way out of the massif's meadow clear of its passes for a gorge");
            (gux, guy) = dirs[best];
            gorgeLevel = lowest + lakeDepth;
            gorgeWidth = width;
            gorgeFrom = -100;
            gorgeTo = reach;
            // Along the line from its start, where it meets the crest (as near as a straight line from the centre's offset).
            gorgeCrest = Crest - (int)(((long)gox * gux + (long)goy * guy) / One);
            gorgeFall = (long)fallPercent * 12 * One / 100;
            hasGorge = true;
            GorgeIndex = best;
            wayDirs = dirs;
        }

        /// <summary>Which of the ways round the gorge runs.</summary>
        public int GorgeIndex { get; private set; } = -1;
        (long x, long y)[] wayDirs;

        /// <summary>
        /// <paramref name="count"/> springs on the ring's inner flank, <paramref name="up"/> cells out from the meadow's edge,
        /// each where no pass reaches, a sixth of the way round or more from the gorge and each other, chosen by the seed.
        /// </summary>
        public List<(int x, int y)> Springs(int count, int up)
        {
            var list = new List<(int, int)>();
            if (!hasGorge) return list;
            int n = wayDirs.Length, apart = n / 6;
            long r = Meadow + up;
            // A way is free for a spring where no pass reaches its spot on the flank.
            var free = new bool[n];
            for (int k = 0; k < n; k++) free[k] = PassWeight((int)(wayDirs[k].x * r / One), (int)(wayDirs[k].y * r / One)) == 0;
            var taken = new List<int> { GorgeIndex };
            for (int s = 0; s < count; s++)
            {
                var ok = new List<int>();
                for (int k = 0; k < n; k++)
                {
                    if (!free[k]) continue;
                    bool far = true;
                    foreach (int t in taken)
                    {
                        int d = Math.Abs(k - t) % n;
                        if (Math.Min(d, n - d) < apart) far = false;
                    }
                    if (far) ok.Add(k);
                }
                if (ok.Count == 0) break;
                int pick = ok[(int)(WorldHash.Hash4(seed, SaltSpring, s, 0) % (uint)ok.Count)];
                taken.Add(pick);
                list.Add((CentreX + (int)(wayDirs[pick].x * r / One), CentreY + (int)(wayDirs[pick].y * r / One)));
            }
            return list;
        }

        // n ways round the centre, as Q16 units from points round a square (integers only).
        static (long x, long y)[] Ways(int n)
        {
            var dirs = new (long, long)[n];
            int side = n / 4;
            for (int k = 0; k < n; k++)
            {
                int q = k / side, f = k % side, half = side / 2;
                long ax = 0, ay = 0;
                switch (q)
                {
                    case 0: ax = half; ay = f - half; break;
                    case 1: ax = half - f; ay = half; break;
                    case 2: ax = -half; ay = half - f; break;
                    default: ax = f - half; ay = -half; break;
                }
                long len = BiomeMath.Length(ax * One, ay * One);
                dirs[k] = (ax * One * One / len, ay * One * One / len);
            }
            return dirs;
        }

        // The middle of the longest run of free ways (round the ring), or -1 for none.
        static int FreestWay(bool[] free)
        {
            int n = free.Length, bestLen = 0, best = -1;
            for (int k = 0; k < n; k++)
            {
                if (!free[k] || free[(k + n - 1) % n]) continue;
                int len = 0;
                while (len < n && free[(k + len) % n]) len++;
                if (len > bestLen)
                {
                    bestLen = len;
                    best = (k + len / 2) % n;
                }
            }
            if (best < 0 && n > 0 && free[0]) best = 0; // free all round
            return best;
        }

        // 1 − |2n − 1| for value noise n: Q16 [0, 1], 1 where the noise is at its middle.
        long Ridged(int salt, long px, long py, long lattice, int turn)
        {
            var (c, s) = Turns[turn];
            long rx = (px * c - py * s) / One, ry = (px * s + py * c) / One;
            return One - Math.Abs(2 * BiomeMath.Noise(seed, salt, rx, ry, lattice) - One);
        }

        // cos and sin of 30°, −50° and 70°, Q16.
        static readonly (long c, long s)[] Turns = { (56756, 32768), (42125, -50203), (22414, 61584) };

        /// <summary>
        /// Sets the passes: the main roads' lines (Q16 map cells, each a list of points, unwrapped near the massif), from
        /// which the pass grid is worked out once. Call it once, before any chunk is made.
        /// </summary>
        public void SetPasses(IReadOnlyList<(long[] x, long[] y)> lines)
        {
            int far = Foot + Warp + PassWidth;
            gx0 = -far;
            gy0 = -far;
            gw = gh = 2 * far / GridStep + 2;
            pass = new long[gw * gh];
            // Each line's segments that can reach the ring (none wholly inside the meadow's least radius less a pass's width,
            // none wholly past the grid), in cells from the centre (Q16); each sets the nodes within a pass's width of it.
            long width = (long)PassWidth * One, inner = (long)Math.Max(0, Meadow - Warp - PassWidth) * One, lim = (long)(far + PassWidth) * One;
            var best = new long[gw * gh];
            for (int k = 0; k < best.Length; k++) best[k] = width;
            foreach (var (lx, ly) in lines)
                for (int k = 0; k + 1 < lx.Length; k++)
                {
                    var (ax, ay) = OffsetQ(lx[k], ly[k]);
                    long bx = ax + (lx[k + 1] - lx[k]), by = ay + (ly[k + 1] - ly[k]);
                    if (Math.Min(Math.Abs(ax), Math.Abs(bx)) > lim || Math.Min(Math.Abs(ay), Math.Abs(by)) > lim) continue;
                    if (Math.Max(BiomeMath.Length(ax, ay), BiomeMath.Length(bx, by)) < inner) continue;
                    int i0 = Math.Max(0, (int)BiomeMath.FloorDiv(Math.Min(ax, bx) - width, (long)GridStep * One) - gx0 / GridStep);
                    int i1 = Math.Min(gw - 1, (int)BiomeMath.FloorDiv(Math.Max(ax, bx) + width, (long)GridStep * One) - gx0 / GridStep + 1);
                    int j0 = Math.Max(0, (int)BiomeMath.FloorDiv(Math.Min(ay, by) - width, (long)GridStep * One) - gy0 / GridStep);
                    int j1 = Math.Min(gh - 1, (int)BiomeMath.FloorDiv(Math.Max(ay, by) + width, (long)GridStep * One) - gy0 / GridStep + 1);
                    for (int j = j0; j <= j1; j++)
                        for (int i = i0; i <= i1; i++)
                        {
                            long qx = (long)(gx0 + i * GridStep) * One, qy = (long)(gy0 + j * GridStep) * One;
                            long d = SegmentDistance(qx, qy, ax, ay, bx, by);
                            if (d < best[j * gw + i]) best[j * gw + i] = d;
                        }
                }
            for (int k = 0; k < best.Length; k++) pass[k] = One - BiomeMath.Smoothstep(best[k] / PassWidth);
        }

        // A point's offset from the centre in Q16 cells, the short way round.
        (long dx, long dy) OffsetQ(long xq, long yq)
        {
            long wq = (long)w * One, hq = (long)h * One;
            long dx = BiomeMath.Mod(xq - (long)CentreX * One, wq), dy = BiomeMath.Mod(yq - (long)CentreY * One, hq);
            if (dx >= wq / 2) dx -= wq;
            if (dy >= hq / 2) dy -= hq;
            return (dx, dy);
        }

        /// <summary>The distance in Q16 from a point to a segment, all Q16.</summary>
        public static long SegmentDistance(long qx, long qy, long ax, long ay, long bx, long by)
        {
            long vx = (bx - ax) / 16, vy = (by - ay) / 16, len2 = vx * vx + vy * vy;
            long t = len2 == 0 ? 0 : Math.Max(0, Math.Min(One, ((qx - ax) / 16 * vx + (qy - ay) / 16 * vy) * One / len2));
            long px = ax + (bx - ax) * t / One, py = ay + (by - ay) * t / One;
            return BiomeMath.Length(qx - px, qy - py);
        }

        /// <summary>The pass weight at a cell offset from the centre, Q16: 1 on a main road's line in the ring, 0 PassWidth away.</summary>
        public long PassWeight(int dx, int dy)
        {
            if (pass == null) return 0;
            int fx = dx - gx0, fy = dy - gy0;
            if (fx < 0 || fy < 0) return 0;
            int i = fx / GridStep, j = fy / GridStep;
            if (i + 1 >= gw || j + 1 >= gh) return 0;
            long tx = (long)(fx - i * GridStep) * One / GridStep, ty = (long)(fy - j * GridStep) * One / GridStep;
            long a = pass[j * gw + i], b = pass[j * gw + i + 1], c = pass[(j + 1) * gw + i], d = pass[(j + 1) * gw + i + 1];
            long top = a + (b - a) * tx / One, bottom = c + (d - c) * tx / One;
            return top + (bottom - top) * ty / One;
        }

        /// <summary>The pass weight at a map cell.</summary>
        public long PassAt(int x, int y)
        {
            var (dx, dy) = Offset(x, y);
            return PassWeight(dx, dy);
        }
    }
}
