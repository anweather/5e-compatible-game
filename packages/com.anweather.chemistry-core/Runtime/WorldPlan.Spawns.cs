// Spawn points (Andrew, 2026-11-08: "more spawn points in the 3x3"): one per land square below the peaks, where a
// player can start, come back to or travel to. The valley keeps its own spawn; every other square's is a flat, dry spot
// beside one of its roads, near its inn, village or landmark (or its hub, with none), never on a peak, in the sea, by a
// river or lake or on a landmark's site. The plan only knows the square's features, so it keeps clear of where ChunkGen
// lays them (a river's curve and wander, a lake at a hub, the shore's wobble); ChunkGen then levels and clears a few
// cells round each spot. Seeded from the plan's seed and in its hash, all integers.
using System;
using System.Collections.Generic;
using System.Linq;

namespace Chem
{
    /// <summary>A spawn point of the plan: its square, its site (cells within the square) and what it stands beside.</summary>
    public sealed class PlanSpawn
    {
        public int Square, SiteX, SiteY, ChunkX, ChunkY;
        /// <summary>What it was placed near: "valley", an inn or a shop, "village", a landmark's kind, an outcrop's, or "hub".</summary>
        public string Near;
        /// <summary>Whether it stands beside a road (a square with none has its spawn in the open).</summary>
        public bool ByRoad;
    }

    public sealed partial class WorldPlan
    {
        /// <summary>How far a spawn keeps, in cells: from the square's sides, a landmark's site, a river's line and a lake's middle.</summary>
        const int SpawnEdge = 40, SpawnLandmark = 60, SpawnRiver = 72, SpawnLake = 64;
        // How far a river's curve leaves its side square on (ChunkGen.SideReach): a spawn keeps clear of that stretch too.
        const int RiverSideReach = 110;
        // How far a spawn stands off its road's edge, in cells: its clearing (ChunkGen.ClearCells) stays off the road.
        const int SpawnTries = 32, SpawnBeside = 6;

        /// <summary>Per land square below the peaks, its spawn, in square order.</summary>
        public List<PlanSpawn> Spawns = new List<PlanSpawn>();

        void PlaceSpawns()
        {
            for (int i = 0; i < Count; i++)
            {
                if (!IsLand(i) || Options[i].Sea || Band[i] >= Library.Lands - 1 || held[i] >= 0) continue; // a held square's are its own plan's
                PlanSpawn spawn;
                if (i == Valley && Inputs.ValleySpawn.HasValue)
                {
                    var (vx, vy) = Inputs.ValleySpawn.Value;
                    spawn = new PlanSpawn { Square = i, SiteX = vx, SiteY = vy, Near = "valley", ByRoad = true };
                }
                else spawn = SpawnIn(i);
                (spawn.ChunkX, spawn.ChunkY) = ChunkOf(i, spawn.SiteX, spawn.SiteY);
                Spawns.Add(spawn);
            }
        }

        PlanSpawn SpawnIn(int i)
        {
            var (ax, ay, near) = SpawnAnchor(i);
            var arms = new List<(int ax, int ay, int bx, int by, int half)>();
            var (hx, hy) = Hub[i];
            for (int s = 0; s < 4; s++)
                foreach (var f in Features[i][s])
                    if (f.Kind == EdgeKind.Road)
                    {
                        var (sx, sy) = SlotPoint(s, f.Slot);
                        arms.Add((sx, sy, hx, hy, f.Main ? 3 : 2));
                    }
            var r = new PlanRng(H(Seed ^ 0x5b0a, i, 0));
            PlanSpawn best = null;
            long bestRoom = long.MinValue;
            for (int k = 0; k < SpawnTries; k++)
            {
                int x, y;
                if (arms.Count > 0)
                {
                    // The arm nearest the anchor first, then any; a point along it near the anchor's foot, off to one side.
                    var a = k == 0 ? arms.OrderBy(m => SegmentDistance2(ax, ay, m.ax, m.ay, m.bx, m.by)).First() : arms[r.Below(arms.Count)];
                    long dx = a.bx - a.ax, dy = a.by - a.ay, len2 = Math.Max(1, dx * dx + dy * dy), len = Math.Max(1, BiomeMath.Sqrt(len2));
                    long t = Math.Max(0, Math.Min(len2, (ax - a.ax) * dx + (ay - a.ay) * dy)) * len / len2;
                    int sign = r.Below(2) == 0 ? -1 : 1;
                    t = Math.Max(0, Math.Min(len, t + sign * (30 + r.Below(70 + k * 6))));
                    int side = r.Below(2) == 0 ? -1 : 1, off = a.half + SpawnBeside;
                    x = (int)(a.ax + dx * t / len - dy * off * side / len);
                    y = (int)(a.ay + dy * t / len + dx * off * side / len);
                }
                else
                {
                    // No road: out in the open round the anchor, in one of eight directions, further each try.
                    var (ox, oy) = Around[r.Below(Around.Length)];
                    int far = 60 + r.Below(40 + k * 8);
                    x = ax + ox * far / 55;
                    y = ay + oy * far / 55;
                }
                long room = SpawnRoom(i, x, y);
                if (room > bestRoom)
                {
                    bestRoom = room;
                    best = new PlanSpawn { Square = i, SiteX = x, SiteY = y, Near = near, ByRoad = arms.Count > 0 };
                }
                if (room >= 0) return best;
            }
            // No roll found room (a road between two rivers, say): the spot every 16 cells with room nearest the road, or
            // the anchor where there's none; failing that the roomiest seen.
            long nearest = long.MaxValue;
            for (int y = SpawnEdge; y < SquareCells - SpawnEdge; y += 16)
                for (int x = SpawnEdge; x < SquareCells - SpawnEdge; x += 16)
                {
                    if (SpawnRoom(i, x, y) < 0) continue;
                    long d2 = arms.Count == 0 ? Distance2(x, y, ax, ay) : arms.Min(m => SegmentDistance2(x, y, m.ax, m.ay, m.bx, m.by));
                    if (d2 >= nearest) continue;
                    nearest = d2;
                    long off = arms.Count == 0 ? 0 : arms.Max(m => m.half) + SpawnBeside + 8;
                    best = new PlanSpawn { Square = i, SiteX = x, SiteY = y, Near = near, ByRoad = d2 <= off * off };
                }
            return best;
        }

        // What a square's spawn is placed near: its inn or shop, its village or landmark, a sight on its road, or its hub.
        (int x, int y, string near) SpawnAnchor(int i)
        {
            var w = Waystations.FirstOrDefault(s => s.Square == i);
            if (w != null) return (w.SiteX, w.SiteY, w.Kind);
            var l = Landmarks.FirstOrDefault(m => m.Square == i);
            if (l != null) return (l.SiteX, l.SiteY, l.Intent == VillageIntent ? "village" : l.Kind);
            var o = Outcrops.FirstOrDefault(s => s.Square == i);
            if (o != null) return (o.SiteX, o.SiteY, o.Kind);
            return (Hub[i].x, Hub[i].y, "hub");
        }

        /// <summary>
        /// How much room a site (cells within square <paramref name="i"/>) has to spare, in cells: the least of its distances
        /// past what a spawn keeps from the sides, the shore, a landmark's site, a river and a lake. Negative where it's too near.
        /// </summary>
        public long SpawnRoom(int i, int x, int y)
        {
            int[] from = { y, SquareCells - 1 - x, SquareCells - 1 - y, x };
            long room = from.Min() - SpawnEdge;
            var c = Coasts[i];
            // The shore wobbles up to 42% of its depth inland; the beach and a margin past that.
            for (int s = 0; s < 4; s++)
                if (c.Open[s]) room = Math.Min(room, from[s] - c.Depth[s] * 3 / 2 - SpawnEdge);
            for (int k = 0; k < 4; k++)
            {
                if (c.Notch[k] == 0) continue;
                int a = PlanGrid.CornerSides[k][0], b = PlanGrid.CornerSides[k][1];
                room = Math.Min(room, BiomeMath.Sqrt((long)from[a] * from[a] + (long)from[b] * from[b]) - c.Notch[k] * 3 / 2 - SpawnEdge);
            }
            foreach (var l in Landmarks.Where(m => m.Square == i))
                room = Math.Min(room, BiomeMath.Sqrt(Distance2(x, y, l.SiteX, l.SiteY)) - SpawnLandmark);
            var (hx, hy) = Hub[i];
            if (Lake[i]) room = Math.Min(room, BiomeMath.Sqrt(Distance2(x, y, hx, hy)) - SpawnLake);
            // A river leaves its slot square on, then curves to the hub; it wanders either side of that.
            for (int s = 0; s < 4; s++)
                foreach (var f in Features[i][s])
                {
                    if (f.Kind != EdgeKind.River) continue;
                    var (sx, sy) = SlotPoint(s, f.Slot);
                    var (ix, iy) = Inward(s);
                    int bx = sx + ix * RiverSideReach, by = sy + iy * RiverSideReach;
                    long d2 = Math.Min(SegmentDistance2(x, y, sx, sy, bx, by), SegmentDistance2(x, y, bx, by, hx, hy));
                    d2 = Math.Min(d2, SegmentDistance2(x, y, sx, sy, hx, hy));
                    room = Math.Min(room, BiomeMath.Sqrt(d2) - SpawnRiver);
                }
            return room;
        }

        static long Distance2(long ax, long ay, long bx, long by) => (ax - bx) * (ax - bx) + (ay - by) * (ay - by);

        // The squared distance from (px, py) to the segment from a to b, whole cells.
        static long SegmentDistance2(long px, long py, long ax, long ay, long bx, long by)
        {
            long dx = bx - ax, dy = by - ay, len2 = dx * dx + dy * dy, dot = (px - ax) * dx + (py - ay) * dy;
            if (len2 == 0 || dot <= 0) return Distance2(px, py, ax, ay);
            if (dot >= len2) return Distance2(px, py, bx, by);
            long cross = dx * (py - ay) - dy * (px - ax);
            return cross * cross / len2;
        }
    }
}
