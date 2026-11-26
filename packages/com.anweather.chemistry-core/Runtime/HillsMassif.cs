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
    public sealed class HillsMassif
    {
        const long One = BiomeMath.One;
        const int SaltWarp = 0x5a01, SaltWarp2 = 0x5a02, SaltRidge = 0x5a10, SaltFoot = 0x5a20, SaltSpring = 0x5a30, SaltGorge = 0x5a31;
        public const int GorgeWander = 70;
        public const int GridStep = 16;

        public readonly int CentreX, CentreY;
        public readonly int Meadow, Crest, Foot, Reach;
        public readonly int Warp;
        public readonly int MeadowHeight, Peaks;
        public readonly int FoothillRelief;
        public readonly int PassWidth, PassKeep;
        readonly int seed, w, h;

        long[] pass;
        int gx0, gy0, gw, gh;

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

        public int Highest => MeadowHeight + Peaks + FoothillRelief;

        (int dx, int dy) Offset(int x, int y)
        {
            int dx = PlanGrid.Mod(x - CentreX, w), dy = PlanGrid.Mod(y - CentreY, h);
            if (dx >= w / 2) dx -= w;
            if (dy >= h / 2) dy -= h;
            return (dx, dy);
        }

        public bool Touches(int x, int y)
        {
            var (dx, dy) = Offset(x, y);
            long far = Reach + Warp + 1;
            return (long)dx * dx + (long)dy * dy < far * far;
        }

        public long Radius(int x, int y)
        {
            var (dx, dy) = Offset(x, y);
            long px = (long)(CentreX + dx) * One + One / 2, py = (long)(CentreY + dy) * One + One / 2;
            long warp = (3 * BiomeMath.Centred(seed, SaltWarp, px, py, 640) + BiomeMath.Centred(seed, SaltWarp2, px, py, 210)) * Warp / 4;
            return BiomeMath.Length((long)dx * One, (long)dy * One) + warp;
        }

        public long Ring(long r)
        {
            if (r <= (long)Meadow * One || r >= (long)Foot * One) return 0;
            if (r < (long)Crest * One) return BiomeMath.Smoothstep((r - (long)Meadow * One) / (Crest - Meadow));
            return BiomeMath.Smoothstep(((long)Foot * One - r) / (Foot - Crest));
        }

        public bool InRing(int x, int y) => Touches(x, y) && Ring(Radius(x, y)) > 0;

        public long Uplift(long r)
        {
            if (r <= (long)Foot * One) return One;
            if (r >= (long)Reach * One) return 0;
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

        public long FootBand(long r)
        {
            if (r <= (long)Foot * One || r >= (long)Reach * One) return 0;
            long t = (r - (long)Foot * One) / (Reach - Foot);
            return 4 * t * (One - t) / One;
        }

        public int At(int x, int y) => (int)(Rise(x, y, out _, out _) / One);

        public int Land(int x, int y, int rolling)
        {
            long rise = Rise(x, y, out long gorge, out long along);
            int land = rolling + (int)(rise / One);
            if (gorge <= 0) return land;
            int floor = GorgeFloor(along);
            if (land <= floor) return land;
            return land - (int)((land - floor) * gorge / One);
        }

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
                long ridge = 0;
                ridge += 55 * Ridged(SaltRidge, px, py, 360, 0);
                ridge += 30 * Ridged(SaltRidge + 1, px, py, 150, 1);
                ridge += 15 * Ridged(SaltRidge + 2, px, py, 60, 2);
                ridge /= 100;
                long shape = 2 * One / 5 + 3 * (ridge * ridge / One) / 5;
                long keep = One - PassWeight(dx, dy) * (100 - PassKeep) / 100;
                keep = keep * (One - gorge) / One;
                rise += ring * shape / One * keep / One * Peaks;
            }
            long band = FootBand(r);
            if (band > 0 && FoothillRelief > 0) rise += band * BiomeMath.Noise(seed, SaltFoot, px, py, 300) / One * FoothillRelief;
            return rise;
        }

        bool hasGorge;
        long gux, guy;
        int gox, goy, gorgeLevel, gorgeWidth, gorgeFrom, gorgeTo, gorgeCrest;
        long gorgeFall;

        public (int x, int y) GorgeStart => (CentreX + gox, CentreY + goy);

        public bool HasGorge => hasGorge;
        public (long x, long y) GorgeWay => (gux, guy);
        public int GorgeLevel => gorgeLevel;
        public int GorgeWidth => gorgeWidth;

        public int GorgeFloor(long along)
        {
            long past = along - (long)gorgeCrest * One;
            if (past <= 0) return gorgeLevel;
            return gorgeLevel - (int)(past * gorgeFall / One / One);
        }

        long GorgeWeight(int dx, int dy, out long along)
        {
            along = 0;
            if (!hasGorge) return 0;
            long qx = (long)(dx - gox) * One, qy = (long)(dy - goy) * One;
            along = (qx * gux + qy * guy) / One;
            long from = (long)gorgeFrom * One, to = (long)gorgeTo * One, fade = 100L * One;
            if (along <= from || along >= to) return 0;
            long wander = BiomeMath.Centred(seed, SaltGorge, along, 0, 280) * GorgeWander;
            long across = Math.Abs((qx * guy - qy * gux) / One - wander);
            if (across >= (long)gorgeWidth * One) return 0;
            long weight = One - BiomeMath.Smoothstep(across / gorgeWidth);
            long ends = Math.Min(BiomeMath.Smoothstep((along - from) / 100), BiomeMath.Smoothstep((to - along) / 100));
            if (along - from >= fade && to - along >= fade) ends = One;
            return weight * ends / One;
        }

        public void SetGorge(Func<int, int, int> rolling, int lakeDepth, int width, int fallPercent, int ways = 64)
        {
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
            gorgeCrest = Crest - (int)(((long)gox * gux + (long)goy * guy) / One);
            gorgeFall = (long)fallPercent * 12 * One / 100;
            hasGorge = true;
            GorgeIndex = best;
            wayDirs = dirs;
        }

        public int GorgeIndex { get; private set; } = -1;
        (long x, long y)[] wayDirs;

        public List<(int x, int y)> Springs(int count, int up)
        {
            var list = new List<(int, int)>();
            if (!hasGorge) return list;
            int n = wayDirs.Length, apart = n / 6;
            long r = Meadow + up;
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
            if (best < 0 && n > 0 && free[0]) best = 0;
            return best;
        }

        long Ridged(int salt, long px, long py, long lattice, int turn)
        {
            var (c, s) = Turns[turn];
            long rx = (px * c - py * s) / One, ry = (px * s + py * c) / One;
            return One - Math.Abs(2 * BiomeMath.Noise(seed, salt, rx, ry, lattice) - One);
        }

        static readonly (long c, long s)[] Turns = { (56756, 32768), (42125, -50203), (22414, 61584) };

        public void SetPasses(IReadOnlyList<(long[] x, long[] y)> lines)
        {
            int far = Foot + Warp + PassWidth;
            gx0 = -far;
            gy0 = -far;
            gw = gh = 2 * far / GridStep + 2;
            pass = new long[gw * gh];
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

        (long dx, long dy) OffsetQ(long xq, long yq)
        {
            long wq = (long)w * One, hq = (long)h * One;
            long dx = BiomeMath.Mod(xq - (long)CentreX * One, wq), dy = BiomeMath.Mod(yq - (long)CentreY * One, hq);
            if (dx >= wq / 2) dx -= wq;
            if (dy >= hq / 2) dy -= hq;
            return (dx, dy);
        }

        public static long SegmentDistance(long qx, long qy, long ax, long ay, long bx, long by)
        {
            long vx = (bx - ax) / 16, vy = (by - ay) / 16, len2 = vx * vx + vy * vy;
            long t = len2 == 0 ? 0 : Math.Max(0, Math.Min(One, ((qx - ax) / 16 * vx + (qy - ay) / 16 * vy) * One / len2));
            long px = ax + (bx - ax) * t / One, py = ay + (by - ay) * t / One;
            return BiomeMath.Length(qx - px, qy - py);
        }

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

        public long PassAt(int x, int y)
        {
            var (dx, dy) = Offset(x, y);
            return PassWeight(dx, dy);
        }
    }
}
