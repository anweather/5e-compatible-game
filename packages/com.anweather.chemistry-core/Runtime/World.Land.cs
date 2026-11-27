// Land stamps (docs/design/maps-as-data.md, section 3): the one way land moves. A stamp is a row, a centre, a radius, a
// profile and a rise, and every peer works its cells out alike from the row, in integers: an act lays it, undo_act takes
// the same integers off, so it can be taken back in any order. landMoved is what the stamps add up to, null until the
// first, and the drawn ground reads it (CellHeightfield), so ground and cells stay one thing. Canyons and cliffs are the
// stamp's own shapes, walls that wander in bands of rock: land has one height a point, so a true overhang is the rocks'
// job (LandRocks), and a wall stands as steep as a cell's width allows. A stamp keeps off water
// (J-073), and moves land only: raiders, sight and spells pass over it as they do any hill. The dragon's reset takes all
// stamps back.
using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace Chem
{
    public sealed partial class World
    {
        /// <summary>A stamp as an act: its kind and its what.</summary>
        public const string ActLand = "land";

        /// <summary>A stamp's slope this steep, in land units across a cell, is bare rock: about 30 degrees.</summary>
        public const int LandSteep = 7;

        /// <summary>A steep face this tall, in land units (5 m), is a crag: bare rock with boulders. A shorter one is a bank of earth, so a small stamp reads as a bump (#148).</summary>
        public const int LandCrag = 40;

        /// <summary>How far round a cell, in cells, its face is measured (LandFace).</summary>
        public const int LandFaceReach = 3;

        /// <summary>The valley's drawable range: how low and high land can be stamped, in land units, inside what a chunk's terrain can draw with room for a cell's own height.</summary>
        public const int LandLowest = MapFile.LandLowest, LandHighest = MapFile.LandHighest;

        /// <summary>This world's drawable range: its map's (map.json), every peer's the same.</summary>
        public (int low, int high) LandRange { get; private set; } = (LandLowest, LandHighest);

        /// <summary>Sets the drawable range, before the world starts.</summary>
        public void SetLandRange((int low, int high) range) => LandRange = range;

        readonly List<LandStamp> landStamps = new List<LandStamp>();
        // What the stamps and the map moved, a chunk at a time and only where they reached (ChunkCells); null until any did.
        ChunkCells<int> landMoved;
        ChunkCells<int> landLaid;

        ChunkCells<int> NewMoved() => new ChunkCells<int>(W, H, WrapX, WrapY);

        /// <summary>Land the map itself lays when the world is made, a road's cut: under the stamps, and no reset takes it.</summary>
        public void LayLand(IEnumerable<(int x, int y, int by)> cells)
        {
            landMoved ??= NewMoved();
            landLaid ??= NewMoved();
            foreach (var (x, y, by) in cells)
            {
                landMoved.Cell(x, y) += by;
                landLaid.Cell(x, y) += by;
                MoveLand(x, y, by);
            }
            DropLandPeek();
        }

        ChunkCells<int> landCrag;

        /// <summary>
        /// Land the map lays as a crag's face (a row's pad dropping off as rock, RowPad): moved as a stamp's land is, so it's
        /// bare rock where it's steep and tall, dressed with boulders, and a tree on its face goes; but no undo or reset takes it.
        /// </summary>
        public void LayCrag(IEnumerable<(int x, int y, int by)> cells)
        {
            var list = cells.ToList();
            if (list.Count == 0) return;
            landMoved ??= NewMoved();
            landCrag ??= NewMoved();
            foreach (var (x, y, by) in list)
            {
                landMoved.Cell(x, y) += by;
                landCrag.Cell(x, y) += by;
                MoveLand(x, y, by);
            }
            DropLandPeek();
            foreach (var (x, y, _) in list)
                for (int v = y - LandFaceReach; v <= y + LandFaceReach; v++)
                    for (int u = x - LandFaceReach; u <= x + LandFaceReach; u++)
                        if (InBounds(u, v) && At(u, v).T == StandingTree && IsStampedRock(u, v)) Paint(u, v, ClearedGround);
        }

        /// <summary>The cells the map laid as a crag's face (LayCrag), row by row.</summary>
        public IEnumerable<(int x, int y)> LandCragCells()
        {
            return landCrag?.Cells() ?? Enumerable.Empty<(int, int)>();
        }

        readonly List<LandStamp> mapStamps = new List<LandStamp>();
        // A map stamp's crags and deep floors lose their trees to grass.
        const string StandingTree = "tree", ClearedGround = "grass";

        /// <summary>The map's own stamps (MapStamps), in the order they were laid.</summary>
        public IReadOnlyList<LandStamp> MapStampsLaid => mapStamps;

        /// <summary>
        /// The map's own land stamps (its stamps.json), laid when the world is made, before its rows (whose pads are cut on
        /// the stamped land): stamped land like any stamp's, rock where it's a crag, but part of the map, so no undo or reset
        /// takes them. A tree they leave on a crag's face or a deep cut's floor goes, so the forest stands on the tops and not
        /// on the walls. Each refused says why, and is left off; one that would move a row's or a road's laid land is
        /// refused (none is laid yet when the map's go down), as is one on water.
        /// </summary>
        public List<string> LayMapStamps(IEnumerable<LandStamp> stamps)
        {
            var refused = new List<string>();
            var laid = new List<LandStamp>();
            foreach (var s in stamps)
            {
                string why = s == null || !s.Known ? "that isn't a land stamp" : LandRefusal(s) ?? LaidRefusal(s);
                if (why != null)
                {
                    refused.Add(s == null ? why : $"{s.Text}: {why}");
                    continue;
                }
                Press(s, 1);
                mapStamps.Add(s);
                laid.Add(s);
            }
            // Round each, to as far as a face is measured past what it moved.
            foreach (var s in laid)
            {
                int reach = s.Bound + LandFaceReach + 1;
                var (xFrom, xTo) = XSpan(s.X - reach, s.X + reach);
                var (yFrom, yTo) = YSpan(s.Y - reach, s.Y + reach);
                for (int y = yFrom; y <= yTo; y++)
                    for (int x = xFrom; x <= xTo; x++)
                        if (At(x, y).T == StandingTree && IsStampedRock(x, y)) Paint(x, y, ClearedGround);
            }
            return refused;
        }

        // The cells a bare map stamp painted (MapStamps' "bare"), null until one does: map data, the same on every peer.
        ChunkCells<bool> bareCells;

        internal void MarkBare(int x, int y)
        {
            bareCells ??= new ChunkCells<bool>(W, H, WrapX, WrapY);
            bareCells.Cell(x, y) = true;
        }

        /// <summary>Whether a bare map stamp left this cell bare (a volcano's rock): no tree is drawn on it.</summary>
        public bool IsStampedBare(int x, int y) => bareCells != null && InBounds(x, y) && bareCells[x, y];

        /// <summary>Whether the stamps left a cell bare rock: a crag's face, or a floor cut a crag deep (CellPaint paints them so).</summary>
        public bool IsStampedRock(int x, int y) =>
            landMoved != null && (LandStampedAt(x, y) <= -LandCrag || (LandDrop(x, y) >= LandSteep && LandFace(x, y) >= LandCrag));

        string LaidRefusal(LandStamp s) =>
            landLaid != null && Reach(s).Any(c => landLaid[c.x, c.y] != 0) ? "that would move a row's or a road's laid land" : null;

        /// <summary>A stamp moved the land, or one was taken back: the middle and the reach of the cells to draw again.</summary>
        public event Action<int, int, int> LandMoved;

        /// <summary>For the netcode checksum, beside CellHash: the stamps in force, "" until land is stamped.</summary>
        public string LandHash() => string.Concat(landStamps.Select(s => s.Text));

        /// <summary>Whether any stamp is in force: drawing costs nothing until one is.</summary>
        public bool LandHasMoved => landMoved != null;

        /// <summary>How far the stamps have moved a cell's land, in land units; 0 off the world.</summary>
        public int LandMovedAt(int x, int y) => landMoved != null && InBounds(x, y) ? landMoved[x, y] : 0;

        /// <summary>The most the stamps put between a cell and a cell beside it, in land units; 0 until land is stamped. What the map laid is no stamp: a road's bank is grass.</summary>
        public int LandDrop(int x, int y)
        {
            if (landMoved == null || !InBounds(x, y)) return 0;
            int here = Stamped(x, y);
            return Math.Max(Math.Max(Beside(x + 1, y, here), Beside(x - 1, y, here)), Math.Max(Beside(x, y + 1, here), Beside(x, y - 1, here)));
        }

        /// <summary>How tall the face a cell is on: the most the stamps put between it and any cell within LandFaceReach, in land units; 0 until land is stamped.</summary>
        public int LandFace(int x, int y)
        {
            if (landMoved == null || !InBounds(x, y)) return 0;
            int here = Stamped(x, y), most = 0;
            for (int v = y - LandFaceReach; v <= y + LandFaceReach; v++)
                for (int u = x - LandFaceReach; u <= x + LandFaceReach; u++)
                    most = Math.Max(most, Beside(u, v, here));
            return most;
        }

        int Beside(int x, int y, int here) => InBounds(x, y) ? Math.Abs(Stamped(x, y) - here) : 0;

        /// <summary>What the stamps alone have moved a cell by, in land units: the map's laid land left out.</summary>
        public int LandStampedAt(int x, int y) => landMoved != null && InBounds(x, y) ? Stamped(x, y) : 0;

        /// <summary>The cells the map's laid land moved, row by row.</summary>
        public IEnumerable<(int x, int y)> LandLaidCells()
        {
            return landLaid?.Cells() ?? Enumerable.Empty<(int, int)>();
        }

        /// <summary>Whether a road's cut leaves this cell a bank: the most it moved past a cell beside it is steep.</summary>
        public bool LandBank(int x, int y)
        {
            if (landLaid == null || !InBounds(x, y)) return false;
            int here = landLaid[x, y];
            for (int v = -1; v <= 1; v++)
                for (int u = -1; u <= 1; u++)
                    if (InBounds(x + u, y + v) && Math.Abs(landLaid[x + u, y + v] - here) >= LandSteep) return true;
            return false;
        }

        int Stamped(int x, int y) => landMoved[x, y] - (landLaid?[x, y] ?? 0);

        // Null when it's stamped; why not otherwise.
        string StampLand(ActDone act, JObject ask)
        {
            var stamp = LandStamp.Read(ask["stamp"]);
            if (stamp == null || !stamp.Known) return "that isn't a land stamp";
            if (!hasLand) SetLand((_, row) => Array.Clear(row, 0, row.Length));
            string why = LandRefusal(stamp);
            if (why != null) return why;
            Press(stamp, 1);
            landStamps.Add(stamp);
            act.Land = stamp;
            LandMoved?.Invoke(stamp.X, stamp.Y, stamp.Bound);
            return null;
        }

        const string PastTheLand = "that goes past how low or high the land can be drawn";

        /// <summary>Why the land as it stands can't take this stamp, or null. Its seed has no say.</summary>
        public string LandRefusal(LandStamp s)
        {
            var (low, high) = s.Extent;
            foreach (var (x, y, _) in Reach(s))
            {
                int now = LandNow(x, y);
                if (now == NoLand) return Wet;
                if (now + low < LandRange.low || now + high > LandRange.high) return PastTheLand;
            }
            return null;
        }

        // Why a stamp in force can't come off: taking it back moves what it really moved. All or nothing, so undo is exact.
        string UndoRefusal(LandStamp s)
        {
            foreach (var (x, y, by) in Reach(s))
            {
                int now = LandAtCell(x, y);
                if (now == NoLand) return Wet;
                if (by == 0) continue;
                int then = now - by;
                if (then < LandRange.low || then > LandRange.high) return PastTheLand;
            }
            return null;
        }

        // A cell's land in land units, NoLand on water: before the first stamp sets the land, read as SetLand would mark it,
        // so a stamp asked of a dry world is refused on the lake as it would be after (#156: a spot search took one there).
        int LandNow(int x, int y)
        {
            if (hasLand) return LandAtCell(x, y);
            return IsWaterBody(At(x, y)) ? NoLand : landMoved?[x, y] ?? 0;
        }

        const string Wet = "that would cross water, and land stamps keep off it for now";

        // The cells a stamp reaches, on the world, row by row, and what it adds at each.
        IEnumerable<(int x, int y, int by)> Reach(LandStamp s)
        {
            if (s.Path.Count > 0) return ReachAlong(s);
            return ReachRound(s);
        }

        // Along a path: the box round it, the cells it moves.
        IEnumerable<(int x, int y, int by)> ReachAlong(LandStamp s)
        {
            int x0 = s.X + s.Path.Min(p => p.x) - s.Radius, x1 = s.X + s.Path.Max(p => p.x) + s.Radius;
            int y0 = s.Y + s.Path.Min(p => p.y) - s.Radius, y1 = s.Y + s.Path.Max(p => p.y) + s.Radius;
            var (xFrom, xTo) = XSpan(x0, x1);
            var (yFrom, yTo) = YSpan(y0, y1);
            for (int y = yFrom; y <= yTo; y++)
                for (int x = xFrom; x <= xTo; x++)
                {
                    int by = s.At(x - s.X, y - s.Y);
                    if (by != 0) yield return (FoldX(x), FoldY(y), by);
                }
        }

        IEnumerable<(int x, int y, int by)> ReachRound(LandStamp s)
        {
            var (xFrom, xTo) = XSpan(s.X - s.Radius, s.X + s.Radius);
            var (yFrom, yTo) = YSpan(s.Y - s.Radius, s.Y + s.Radius);
            for (int y = yFrom; y <= yTo; y++)
                for (int x = xFrom; x <= xTo; x++)
                    if ((long)(x - s.X) * (x - s.X) + (long)(y - s.Y) * (y - s.Y) <= (long)s.Radius * s.Radius)
                        yield return (FoldX(x), FoldY(y), s.At(x - s.X, y - s.Y));
        }

        void Press(LandStamp s, int sign)
        {
            landMoved ??= NewMoved();
            foreach (var (x, y, by) in Reach(s))
            {
                landMoved.Cell(x, y) += sign * by;
                MoveLand(x, y, sign * by);
            }
            DropLandPeek();
        }

        // Taken back, in any order: the same integers off. Null when it's done, else why not.
        string TakeLand(ActDone act)
        {
            string why = UndoRefusal(act.Land);
            if (why != null) return why;
            TakeStamp(act.Land);
            return null;
        }

        void TakeStamp(LandStamp s)
        {
            Press(s, -1);
            landStamps.RemoveAt(landStamps.FindLastIndex(k => k.Text == s.Text));
            if (landStamps.Count == 0 && landLaid == null && landCrag == null && mapStamps.Count == 0) landMoved = null;
            LandMoved?.Invoke(s.X, s.Y, s.Bound);
        }

        // The dragon's reset: the land as the map has it. Acts that stamped it have nothing left to take back.
        internal void ForgetLandStamps()
        {
            for (int k = landStamps.Count - 1; k >= 0; k--) TakeStamp(landStamps[k]);
            acts.RemoveAll(a => a.Land != null);
            foreach (var firing in acts) firing.Parts.RemoveAll(p => p.Land != null);
            acts.RemoveAll(a => a.Kind == ActCalled && a.Parts.Count == 0);
        }

        void SaveLand(JObject save)
        {
            if (landStamps.Count > 0) save["land"] = new JArray(landStamps.Select(s => (object)s.Row).ToArray());
            if (mapStamps.Count > 0) save["mapStamps"] = new JArray(mapStamps.Select(s => (object)s.Row).ToArray());
            if (landLaid != null) save["landLaid"] = new JArray(LandLaidCells().SelectMany(c => new object[] { c.x, c.y, landLaid[c.x, c.y] }).ToArray());
            if (landCrag != null) save["landCrag"] = new JArray(LandCragCells().SelectMany(c => new object[] { c.x, c.y, landCrag[c.x, c.y] }).ToArray());
            SaveLandSet(save);
        }

        // Land given by SetLand (a baked map's land.json, or flat land a stamp needed): not the land itself, which is the
        // map's, but the hash of the rows it was given and the cells it marked NoLand for their water then, as runs of
        // [first Idx, how many]. With the same rows, a load makes the same land whatever water stands where now (#119).
        void SaveLandSet(JObject save)
        {
            LandSetLater();
            if (landRowsHash == null) return;
            var marks = new JArray();
            for (int k = 0; k < landMarks.Count;)
            {
                int first = landMarks[k], n = 1;
                while (k + n < landMarks.Count && landMarks[k + n] == first + n) n++;
                marks.Add(first);
                marks.Add(n);
                k += n;
            }
            save["landSet"] = new JObject { ["rows"] = landRowsHash, ["marks"] = marks };
        }

        // After a load's state and cells (the stamps have pressed landMoved): the land SetLand made, from the rows given
        // (the map's land.json) or, if the save's land was flat, from none, with the save's marks. Refused if the rows
        // aren't the ones it was saved over.
        void LoadLandSet(JObject s, int[] land)
        {
            if (!(s["landSet"] is JObject set)) return;
            string rows = (string)set["rows"];
            var flat = new int[W];
            if (land == null)
            {
                uint hash = LandRowsSeed;
                for (int y = 0; y < H; y++) hash = HashLandRow(hash, flat);
                if (hash.ToString("x8", System.Globalization.CultureInfo.InvariantCulture) != rows)
                    throw new SaveException("This save doesn't fit: it was saved with the map's land, so it loads given it");
            }
            else if (land.Length != W * H) throw new SaveException($"This save doesn't fit: land for {W * H} cells, not {land.Length}");
            var marked = new HashSet<int>();
            var saved = set["marks"] as JArray ?? new JArray();
            for (int k = 0; k + 1 < saved.Count; k += 2)
                for (int n = 0; n < (int)saved[k + 1]; n++) marked.Add((int)saved[k] + n);
            SpreadLand(land == null ? (_, row) => Array.Copy(flat, row, W) : (y, row) => Array.Copy(land, y * W, row, 0, W), (i, _) => marked.Contains(i));
            if (landRowsHash != rows) throw new SaveException("This save doesn't fit: saved over other land");
        }

        void LoadLand(JObject s)
        {
            if (s["landLaid"] is JArray laid)
                LayLand(Enumerable.Range(0, laid.Count / 3).Select(k => ((int)laid[3 * k], (int)laid[3 * k + 1], (int)laid[3 * k + 2])));
            if (s["landCrag"] is JArray crag)
                LayCrag(Enumerable.Range(0, crag.Count / 3).Select(k => ((int)crag[3 * k], (int)crag[3 * k + 1], (int)crag[3 * k + 2])));
            // The map's own stamps, as they were laid: the save's cells already have what they cleared.
            foreach (var row in s["mapStamps"] as JArray ?? new JArray())
            {
                var stamp = LandStamp.Read(row);
                mapStamps.Add(stamp);
                Press(stamp, 1);
            }
            foreach (var row in s["land"] as JArray ?? new JArray())
            {
                var stamp = LandStamp.Read(row);
                landStamps.Add(stamp);
                Press(stamp, 1);
            }
        }
    }
}
