// A top-down diagram of a world plan (J-080 phase 2), with no Unity: each world square coloured by its band and anchor
// biome, the sea and shorelines by the coasts, rivers, roads, ranges and canyons drawn from the slot they cross a side
// by to the square's hub, and the landmarks, crossings, waystations and outcrops marked. A filler gets a cross.
using System;
using System.Collections.Generic;

namespace Chem.Tests
{
    internal static class WorldPlanPicture
    {
        static readonly (byte r, byte g, byte b)[] Bands = { (40, 84, 150), (120, 168, 92), (150, 150, 92), (140, 116, 90), (210, 206, 210) };

        static readonly Dictionary<string, (byte r, byte g, byte b)> Biomes = new Dictionary<string, (byte, byte, byte)>
        {
            ["temperate"] = (70, 130, 60),
            ["grassland"] = (150, 180, 80),
            ["mountains"] = (120, 112, 104),
            ["tundra"] = (190, 200, 196),
            ["salt-flat"] = (226, 220, 196),
            ["foggy-marsh"] = (92, 120, 96),
            ["ashlands"] = (90, 74, 70),
            ["ocean"] = (40, 84, 150),
            ["ice-wall"] = (236, 244, 250),
            ["dune-sea"] = (222, 196, 128),
        };

        static readonly (byte r, byte g, byte b) Sea = (40, 84, 150), River = (70, 150, 230), Road = (150, 96, 40), MainRoad = (110, 50, 20),
            Range = (60, 56, 56), Canyon = (140, 40, 30), Edge = (30, 30, 30);

        internal sealed class Canvas
        {
            public readonly int W, H;
            public readonly byte[] Rgb;

            public Canvas(int w, int h)
            {
                W = w;
                H = h;
                Rgb = new byte[w * h * 3];
            }

            public void Dot(int x, int y, (byte r, byte g, byte b) c)
            {
                if (x < 0 || y < 0 || x >= W || y >= H) return;
                int k = (y * W + x) * 3;
                Rgb[k] = c.r;
                Rgb[k + 1] = c.g;
                Rgb[k + 2] = c.b;
            }

            public void Rect(int x0, int y0, int x1, int y1, (byte, byte, byte) c)
            {
                for (int y = Math.Max(0, y0); y < Math.Min(H, y1); y++)
                    for (int x = Math.Max(0, x0); x < Math.Min(W, x1); x++) Dot(x, y, c);
            }

            public void Disc(int cx, int cy, int r, (byte, byte, byte) c)
            {
                for (int y = -r; y <= r; y++)
                    for (int x = -r; x <= r; x++)
                        if (x * x + y * y <= r * r) Dot(cx + x, cy + y, c);
            }

            public void Line(int x0, int y0, int x1, int y1, int r, (byte, byte, byte) c, int dash = 0)
            {
                int steps = Math.Max(Math.Abs(x1 - x0), Math.Abs(y1 - y0));
                for (int k = 0; k <= steps; k++)
                {
                    if (dash > 0 && k / dash % 2 == 1) continue;
                    int x = steps == 0 ? x0 : x0 + (x1 - x0) * k / steps, y = steps == 0 ? y0 : y0 + (y1 - y0) * k / steps;
                    Disc(x, y, r, c);
                }
            }
        }

        /// <summary>A heights picture's colour for land <paramref name="v"/> (land units): sea blue by depth, else green to brown to white.</summary>
        internal static (int R, int G, int B) Height(int v)
        {
            if (v < ChunkGen.SeaLevel) return (30, 70 + Math.Max(0, 60 + v / 6), 150 + Math.Max(0, 60 + v / 8));
            int t = Math.Min(255, Math.Max(0, v * 255 / 1800));
            return t < 128 ? (60 + t, 130 + t / 3, 50) : (188 + (t - 128) / 2, 172 + (t - 128) * 2 / 3, 50 + (t - 128) * 3 / 2);
        }

        /// <summary>Writes <paramref name="col"/> lit by <paramref name="shade"/> into an RGB array at pixel <paramref name="at"/>.</summary>
        internal static void Put(byte[] rgb, int at, (int R, int G, int B) col, int shade)
        {
            rgb[at * 3] = (byte)Math.Max(0, Math.Min(255, col.R + shade));
            rgb[at * 3 + 1] = (byte)Math.Max(0, Math.Min(255, col.G + shade));
            rgb[at * 3 + 2] = (byte)Math.Max(0, Math.Min(255, col.B + shade));
        }

        static (byte, byte, byte) Mix((byte r, byte g, byte b) a, (byte r, byte g, byte b) b, int pctB) =>
            ((byte)((a.r * (100 - pctB) + b.r * pctB) / 100), (byte)((a.g * (100 - pctB) + b.g * pctB) / 100), (byte)((a.b * (100 - pctB) + b.b * pctB) / 100));

        /// <summary>The plan at <paramref name="px"/> pixels a world square.</summary>
        internal static Canvas Draw(WorldPlan plan, int px)
        {
            var g = plan.Grid;
            var cv = new Canvas(g.W * px, g.H * px);
            int P(int cells) => cells * px / WorldPlan.SquareCells;
            for (int i = 0; i < plan.Count; i++)
            {
                int x0 = g.X(i) * px, y0 = g.Y(i) * px;
                var t = plan.Options[i].Template;
                var band = plan.Wall[i] != 0 ? Biomes[t.Biome] : Bands[Math.Min(plan.Band[i], Bands.Length - 1)];
                var colour = Biomes.TryGetValue(t.Biome ?? "", out var bc) ? Mix(band, bc, 55) : band;
                cv.Rect(x0, y0, x0 + px, y0 + px, colour);
                var c = plan.Coasts[i];
                for (int s = 0; s < 4; s++)
                {
                    if (!c.Open[s]) continue;
                    int d = P(c.Depth[s]);
                    if (s == 0) cv.Rect(x0, y0, x0 + px, y0 + d, Sea);
                    else if (s == 1) cv.Rect(x0 + px - d, y0, x0 + px, y0 + px, Sea);
                    else if (s == 2) cv.Rect(x0, y0 + px - d, x0 + px, y0 + px, Sea);
                    else cv.Rect(x0, y0, x0 + d, y0 + px, Sea);
                }
                for (int k = 0; k < 4; k++)
                {
                    if (c.Notch[k] == 0) continue;
                    int cx = x0 + (PlanGrid.Cdx[k] > 0 ? px : 0), cy = y0 + (PlanGrid.Cdy[k] > 0 ? px : 0);
                    cv.Disc(cx, cy, P(c.Notch[k]), Sea);
                }
            }
            // Features from each side's slot to the hub, the ones under the roads first.
            foreach (var kind in new[] { EdgeKind.Canyon, EdgeKind.Range, EdgeKind.River, EdgeKind.Road })
                for (int i = 0; i < plan.Count; i++)
                {
                    int x0 = g.X(i) * px, y0 = g.Y(i) * px;
                    var (hx, hy) = plan.Hub[i];
                    for (int s = 0; s < 4; s++)
                        foreach (var f in plan.Features[i][s])
                        {
                            if (f.Kind != kind) continue;
                            var (sx, sy) = WorldPlan.Inside(s, f.Slot * WorldPlan.ChunkCells + WorldPlan.ChunkCells / 2, 0);
                            int r = Math.Max(1, px / 48);
                            (byte, byte, byte) col = kind switch
                            {
                                EdgeKind.River => River,
                                EdgeKind.Range => Range,
                                EdgeKind.Canyon => Canyon,
                                _ => f.Main ? MainRoad : Road,
                            };
                            if (kind == EdgeKind.Range || kind == EdgeKind.Canyon) r = Math.Max(2, px / 24);
                            if (kind == EdgeKind.Road && f.Main) r = Math.Max(2, px / 32);
                            cv.Line(x0 + P(sx), y0 + P(sy), x0 + P(hx), y0 + P(hy), r, col, kind == EdgeKind.Road && f.Trail ? Math.Max(2, px / 16) : 0);
                        }
                }
            // Grid lines between squares, and a cross on each filler.
            for (int i = 0; i < plan.Count; i++)
            {
                int x0 = g.X(i) * px, y0 = g.Y(i) * px;
                cv.Line(x0, y0, x0 + px - 1, y0, 0, Edge);
                cv.Line(x0, y0, x0, y0 + px - 1, 0, Edge);
                if (plan.Options[i].Filler)
                {
                    int m = px / 6;
                    cv.Line(x0 + m, y0 + m, x0 + px - m, y0 + px - m, 0, Edge);
                    cv.Line(x0 + px - m, y0 + m, x0 + m, y0 + px - m, 0, Edge);
                }
            }
            int mark = Math.Max(3, px / 12);
            foreach (var x in plan.Crossings)
                if (x.Used) Spot(cv, plan, (x.Square, x.SiteX, x.SiteY), mark - 1, (255, 255, 255), px);
            foreach (var w in plan.Waystations) Spot(cv, plan, (w.Square, w.SiteX, w.SiteY), mark - 1, (250, 210, 40), px);
            foreach (var o in plan.Outcrops) Spot(cv, plan, (o.Square, o.SiteX, o.SiteY), Math.Max(2, mark / 2), (220, 60, 200), px);
            foreach (var l in plan.Landmarks)
            {
                (byte, byte, byte) col = l.Kind switch
                {
                    "castle" => (230, 30, 30),
                    "spire" => (255, 255, 255),
                    "cave" => (10, 10, 10),
                    "mountains" => (255, 120, 20),
                    "foggy-marsh" => (40, 230, 230),
                    _ => (30, 90, 20),
                };
                Spot(cv, plan, (l.Square, l.SiteX, l.SiteY), mark + 1, (0, 0, 0), px);
                Spot(cv, plan, (l.Square, l.SiteX, l.SiteY), mark, col, px);
                if (l.Port) Spot(cv, plan, (l.Square, l.SiteX, l.SiteY), mark / 2, (255, 255, 255), px);
            }
            return cv;
        }

        static void Spot(Canvas cv, WorldPlan plan, (int i, int x, int y) at, int r, (byte, byte, byte) c, int px) =>
            cv.Disc(plan.Grid.X(at.i) * px + at.x * px / WorldPlan.SquareCells, plan.Grid.Y(at.i) * px + at.y * px / WorldPlan.SquareCells, r, c);
    }
}
