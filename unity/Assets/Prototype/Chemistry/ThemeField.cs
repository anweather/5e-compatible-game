// What a blueprint says about one cell (D11): the look the theme paints there
// (a zone, a lake's island, an influence's soft field) and whether a lake or
// river fills it. A field is built for a window of cells and keeps only the
// blueprint pieces that reach it, so each ask is a handful of cheap tests,
// and every answer is a pure function of (seed, blueprint, cell): chunk seams
// and generation order don't matter. Port of PocketTactics'
// src/core/world/theme/steer.ts. Where PocketTactics falls back to its
// procedural climate, a cell here is "filler" (Look returns null), for the
// caller's own land.
//
// Influences blend by dither: each has a weight at the cell (its strength,
// eased by its normalised distance, wobbled by noise, broken into patches,
// faded with the continent), and one low-frequency dither field picks the
// look, later influences taking the top of its range, so overlapping fields
// nest instead of speckling. Where an influence's shape passes its heart
// threshold the ground is forced to the heart terrain (Heart after Look).
using System;
using System.Collections.Generic;

namespace Chem
{
    public sealed class ThemeField
    {
        public const int MatNone = 0, MatWater = 1, MatLava = 2, MatIce = 3, MatShoreSand = 4, MatShoreAsh = 5;

        const int SaltZone = 0x5a4f4e45, SaltLake = 0x4c414b45, SaltDither = 0x44495448, SaltInfluence = 0x494e464c;
        /// <summary>The dither field's lattice (cells): look patches are roughly this size where a weight is low.</summary>
        const double DitherLattice = 7;
        const double InfluenceEdgeLattice = 14, PatchSoft = 0.05;
        // How far (cells) zone edges wander beyond the blob outline, plus a share of the radius.
        const double ZoneEdge = 7, ZoneEdgeRel = 0.07, LakeEdge = 2.5, Shore = 1.6, RiverBank = 1.3;

        sealed class LiveZone
        {
            public BpZone Z;
            public int Salt;
            public double Amp;
        }

        sealed class LiveLake
        {
            public BpLake L;
            public double Amp, ShoreRho;
        }

        sealed class LiveRiver
        {
            public BpRiver R;
            public List<int> Segs;
        }

        sealed class LiveInfluence
        {
            public BpInfluence Inf;
            public int Salt;
            public List<int> Segs;
            public double PatchQ;
        }

        readonly Blueprint bp;
        readonly List<LiveZone> zones = new List<LiveZone>();
        readonly List<LiveLake> lakes = new List<LiveLake>();
        readonly List<LiveRiver> rivers = new List<LiveRiver>();
        readonly List<LiveInfluence> influences = new List<LiveInfluence>();
        readonly double[] w;

        /// <summary>After Look: the terrain an influence's heart forces here (water, grass, forest, sand, rock...), or null.</summary>
        public string Heart { get; private set; }

        /// <summary>After Look: whether a zone (or an island) painted the cell, i.e. it isn't filler.</summary>
        public bool Painted { get; private set; }

        /// <summary>A field over the cells in <paramref name="box"/> (continent cells, x east, y south, the start at 0).</summary>
        public ThemeField(Blueprint bp, GeoBox box)
        {
            this.bp = bp;
            for (int i = 0; i < bp.Zones.Count; i++)
            {
                var z = bp.Zones[i];
                double reach = ZoneEdge + ZoneEdgeRel * Math.Max(z.Blob.Rx, z.Blob.Ry);
                if (!ThemeGeometry.Bounds(z.Blob, reach + 2).Meets(box)) continue;
                zones.Add(new LiveZone
                {
                    Z = z,
                    Salt = unchecked(SaltZone + (i + 1) * (int)0x9e3779b1u),
                    Amp = 2 * (ZoneEdge / Math.Min(z.Blob.Rx, z.Blob.Ry) + ZoneEdgeRel),
                });
            }
            foreach (var l in bp.Lakes)
            {
                if (!ThemeGeometry.Bounds(l.Blob, LakeEdge + Shore + 4).Meets(box)) continue;
                double r = Math.Min(l.Blob.Rx, l.Blob.Ry);
                lakes.Add(new LiveLake { L = l, Amp = 2 * LakeEdge / r, ShoreRho = Shore / r });
            }
            foreach (var r in bp.Rivers)
            {
                if (r.Path.Points.Length < 2 || !ThemeGeometry.Bounds(r.Path, RiverBank + 1).Meets(box)) continue;
                var segs = ThemeGeometry.SegmentsIn(r.Path, box, RiverBank + 1);
                if (segs.Count > 0) rivers.Add(new LiveRiver { R = r, Segs = segs });
            }
            foreach (var inf in bp.Influences) AddInfluence(inf, box);
            w = new double[influences.Count];
        }

        void AddInfluence(BpInfluence inf, GeoBox box)
        {
            double pad = inf.Reach * (1 + inf.Edge) + 2;
            int salt = unchecked(SaltInfluence + (inf.Order + 1) * (int)0x9e3779b1u);
            double patchQ = inf.PatchShare > 0 ? WorldNoise.Quantile(2, 1 - inf.PatchShare) : 0;
            var live = new LiveInfluence { Inf = inf, Salt = salt, Segs = new List<int>(), PatchQ = patchQ };
            if (inf.Shape == "blob" || inf.Shape == "around")
            {
                var b = inf.Blob;
                double reach = inf.Shape == "blob" ? Math.Max(b.Rx, b.Ry) * inf.Edge + 2 : pad;
                if (ThemeGeometry.Bounds(b, reach).Meets(box)) influences.Add(live);
            }
            else if (inf.Shape == "along")
            {
                if (inf.Path.Points.Length < 2 || !ThemeGeometry.Bounds(inf.Path, pad).Meets(box)) return;
                live.Segs = ThemeGeometry.SegmentsIn(inf.Path, box, pad);
                if (live.Segs.Count > 0) influences.Add(live);
            }
            else influences.Add(live);
        }

        /// <summary>The look the theme gives a cell, or null for filler (the caller's own land); sets Heart and Painted.</summary>
        public string Look(int mx, int my)
        {
            Heart = null;
            Painted = false;
            double fade = bp.Continent.FadeAt(mx, my);
            if (fade <= 0) return null;
            double x = mx + 0.5, y = my + 0.5;
            string b = null;
            bool painted = false;
            foreach (var lz in zones)
            {
                var z = lz.Z;
                double limit = z.Start ? 1 : fade;
                double rho = ThemeGeometry.Rho(z.Blob, x, y);
                if (rho - lz.Amp * 0.5 >= limit) continue;
                double wob = (WorldNoise.Fractal(bp.Seed, lz.Salt, mx, my, 12, 2) - 0.5) * lz.Amp;
                if (rho + wob < limit)
                {
                    b = z.Look;
                    painted = true;
                }
            }
            foreach (var ll in lakes)
            {
                var l = ll.L;
                if (l.Island == null) continue;
                if (ThemeGeometry.Rho(l.Island, x, y) + LakeWobble(mx, my, ll) * 0.6 < fade && InLake(mx, my, ll, fade) == 1)
                {
                    b = l.IslandLook;
                    painted = true;
                }
            }
            Painted = painted;
            return influences.Count > 0 ? Blend(mx, my, b, painted, fade) : b;
        }

        // Influence weights at a cell, then the dithered pick of a look (and any heart).
        string Blend(int mx, int my, string b, bool painted, double fade)
        {
            int seed = bp.Seed;
            double total = 0;
            int heartAt = -1;
            for (int i = 0; i < influences.Count; i++)
            {
                w[i] = Weight(influences[i], mx, my, b, painted, fade, out bool heart);
                total += w[i];
                if (heart) heartAt = i;
            }
            if (heartAt >= 0)
            {
                Heart = influences[heartAt].Inf.HeartTerrain;
                return influences[heartAt].Inf.Look;
            }
            if (total <= 0) return b;
            // Later influences take the top of the dither range, so overlaps nest.
            double scale = total > 1 ? 1 / total : 1;
            double u = WorldNoise.ToUniform(2, WorldNoise.Fractal(seed, SaltDither, mx, my, DitherLattice, 2));
            double acc = 0;
            for (int i = influences.Count - 1; i >= 0; i--)
            {
                if (w[i] <= 0) continue;
                acc += w[i] * scale;
                if (u >= 1 - acc) return influences[i].Inf.Look;
            }
            return b;
        }

        double Weight(LiveInfluence li, int mx, int my, string b, bool painted, double fade, out bool heart)
        {
            double x = mx + 0.5, y = my + 0.5;
            heart = false;
            var inf = li.Inf;
            if (!inf.OntoAny && !(inf.OntoFiller && !painted) && !(b != null && inf.OntoLooks.Contains(b))) return 0;
            double s = Distance(li, x, y);
            if (inf.Shape != "continent" && inf.Edge > 0)
                s += (WorldNoise.Fractal(bp.Seed, li.Salt, mx, my, InfluenceEdgeLattice, 2) - 0.5) * 2 * inf.Edge;
            double e;
            if (s <= 1 - inf.Falloff) e = 1;
            else if (s >= 1) e = 0;
            else e = ThemeGeometry.Smoothstep01((1 - s) / inf.Falloff);
            if (e <= 0) return 0;
            if (inf.PatchShare > 0)
            {
                double pn = WorldNoise.Fractal(bp.Seed, li.Salt + 1, mx, my, inf.PatchLattice, 2);
                e *= ThemeGeometry.Smoothstep01((pn - li.PatchQ) / PatchSoft);
                if (e <= 0) return 0;
            }
            heart = inf.HeartTerrain != null && e >= inf.HeartAbove && !(inf.HeartTerrain == "water" && ThemeGeometry.Hypot(x, y) < ThemeBlueprint.StartWaterClear);
            return inf.Strength * e * fade;
        }

        // An influence's normalised distance: 0 at its heart, 1 where it ends.
        static double Distance(LiveInfluence li, double x, double y)
        {
            var inf = li.Inf;
            switch (inf.Shape)
            {
                case "blob":
                    return ThemeGeometry.Rho(inf.Blob, x, y);
                case "around":
                {
                    var blob = inf.Blob;
                    double rho = blob.Harmonics.Length > 0 ? ThemeGeometry.Rho(blob, x, y) : ThemeGeometry.Hypot((x - blob.Cx) / blob.Rx, (y - blob.Cy) / blob.Ry);
                    return rho <= 1 ? 0 : ThemeGeometry.Hypot(x - blob.Cx, y - blob.Cy) * (1 - 1 / rho) / inf.Reach;
                }
                case "along":
                {
                    var (d, half) = ThemeGeometry.Distance(inf.Path, li.Segs, x, y);
                    return Math.Max(0, d - half) / inf.Reach;
                }
                default:
                    return 0;
            }
        }

        double LakeWobble(int mx, int my, LiveLake ll) => (WorldNoise.Fractal(bp.Seed, SaltLake, mx, my, 6, 2) - 0.5) * ll.Amp;

        // 0 outside, 1 inside the lake's outline (island included), 2 on its shore ring.
        int InLake(int mx, int my, LiveLake ll, double fade)
        {
            double rho = ThemeGeometry.Rho(ll.L.Blob, mx + 0.5, my + 0.5);
            if (rho - ll.Amp >= fade + ll.ShoreRho) return 0;
            double v = rho + LakeWobble(mx, my, ll);
            if (v < fade) return 1;
            return v < fade + ll.ShoreRho ? 2 : 0;
        }

        static int MaterialCode(string m) => m switch
        {
            "lava" => MatLava,
            "ice" => MatIce,
            _ => MatWater,
        };

        /// <summary>What fills a cell: a lake's or river's water, lava or ice, a shore ring (Mat* codes), or nothing.</summary>
        public int Material(int mx, int my)
        {
            if (zones.Count == 0 && lakes.Count == 0 && rivers.Count == 0) return MatNone;
            double fade = bp.Continent.FadeAt(mx, my);
            double x = mx + 0.5, y = my + 0.5;
            int lake = LakeMaterial(mx, my, fade, out int shore, out bool lakeHere);
            if (lake >= 0) return lake;
            foreach (var lr in rivers)
            {
                var (d, half) = ThemeGeometry.Distance(lr.R.Path, lr.Segs, x, y);
                if (d < half) return MaterialCode(lr.R.Material);
                if (lr.R.Material == "lava" && d < half + RiverBank && !lakeHere) shore = MatShoreAsh;
            }
            return shore;
        }

        // A lake's fill at a cell (MatNone on an island), or -1 outside every lake; shore: the ring a
        // lake's shore puts there (sand by water, ash by lava), MatNone for none; onShore: on any lake's shore ring.
        int LakeMaterial(int mx, int my, double fade, out int shore, out bool onShore)
        {
            shore = MatNone;
            onShore = false;
            foreach (var ll in lakes)
            {
                int where = InLake(mx, my, ll, fade);
                if (where == 1) return OnIsland(ll, mx, my, fade) ? MatNone : MaterialCode(ll.L.Material);
                if (where != 2) continue;
                onShore = true;
                if (ll.L.Material != "ice") shore = ll.L.Material == "water" ? MatShoreSand : MatShoreAsh;
            }
            return -1;
        }

        bool OnIsland(LiveLake ll, int mx, int my, double fade) =>
            ll.L.Island != null && ThemeGeometry.Rho(ll.L.Island, mx + 0.5, my + 0.5) + LakeWobble(mx, my, ll) * 0.6 < fade;

        /// <summary>How far (cells) a cell is from the nearest river's bank (0 in it), or infinity with none near.</summary>
        public double RiverBankDistance(int mx, int my)
        {
            double best = double.PositiveInfinity;
            foreach (var lr in rivers)
            {
                var (d, half) = ThemeGeometry.Distance(lr.R.Path, lr.Segs, mx + 0.5, my + 0.5);
                best = Math.Min(best, Math.Max(0, d - half));
            }
            return best;
        }
    }
}
