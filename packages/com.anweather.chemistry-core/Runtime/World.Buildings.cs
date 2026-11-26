// Buildings in a running world (docs/design/builders.md "Through the room"; G3 and
// G5 of docs/design/world-tools.md). The rows in force come from the rules
// (Rules.ModBuildings over the world's own), so a mod switching on or off, a late
// joiner composing it again and a save's room all go through one path: ApplyRules
// rasterises the rows that changed, old and new, and lays the difference. A changed
// cell is laid fresh, as the dragon freshens (no fire, cover, gas or water); a
// wall taken away goes back to what stood under it. The starting state follows, so
// the dragon puts the building back and the audit doesn't count it as damage. No
// rule changed, nothing runs, so a world without such mods hashes as it did.
//
// Not a port: the sandbox has no buildings.
using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace Chem
{
    public sealed class BuildingsChange
    {
        internal HashSet<int> Covered = new HashSet<int>();
        readonly int w;

        internal BuildingsChange(int w, IReadOnlyList<string> ids)
        {
            this.w = w;
            Ids = ids;
        }

        public IReadOnlyList<string> Ids { get; }

        public bool Covers(int x, int y) => Covered.Contains(y * w + x);
    }

    public sealed partial class World
    {
        const int PushReach = 48;

        List<JObject> baseBuildings;
        readonly SortedDictionary<int, (string terrain, int height)> underBuildings = new SortedDictionary<int, (string, int)>();

        public event Action<BuildingsChange> BuildingsChanged;

        internal IReadOnlyList<JObject> BaseBuildings => baseBuildings ??= (roomBudgets?.Buildings ?? WorldBuildings.Empty).RowsOn(StartTerrain, Rules.Buildings);

        internal void NoteBaseBuildings() => _ = BaseBuildings;

        internal void NoteBaseBuildingsAgain()
        {
            if (startSource == null) return;
            baseBuildings = null;
            NoteBaseBuildings();
        }

        string StartTerrain(int x, int y)
        {
            if (!InBounds(x, y)) return null;
            return startSource != null ? StartCell(x, y).T : At(x, y).T;
        }

        public IReadOnlyList<BuildingRow> BuildingsNow => RowsUnder(Rules);

        public bool IsBaseBuilding(string id) => BaseBuildings.Any(r => (string)r["id"] == id);

        public List<BuildingRow> RowsDrawn()
        {
            var laid = new HashSet<string>((roomBudgets?.Buildings ?? WorldBuildings.Empty).LaidRows.Select(r => (string)r["id"]), StringComparer.Ordinal);
            return BuildingsNow.Where(r => (laid.Contains(r.Id) && IsBaseBuilding(r.Id)) || Rules.ModBuildings.Any(m => m.Id == r.Id)).ToList();
        }

        public BuildingRow BuildingRowOf(string id) => RowOf(id, Rules);

        public List<BuildingCell> BuildingCellsOf(BuildingRow row) => BuildingRaster.Cells(row, Rules.Buildings);

        List<BuildingRow> RowsUnder(Rules rules)
        {
            var mine = rules.ModBuildings.ToDictionary(r => r.Id, StringComparer.Ordinal);
            var rows = new List<BuildingRow>();
            foreach (var row in BaseBuildings)
            {
                string id = (string)row["id"];
                rows.Add(mine.TryGetValue(id, out var edited) ? edited : BuildingRow.Read(id, row, rules.Buildings));
                mine.Remove(id);
            }
            rows.AddRange(rules.ModBuildings.Where(r => mine.ContainsKey(r.Id)));
            return rows;
        }

        BuildingRow RowOf(string id, Rules rules)
        {
            var mod = rules.ModBuildings.FirstOrDefault(r => r.Id == id);
            if (mod != null) return mod;
            var row = BaseBuildings.FirstOrDefault(r => (string)r["id"] == id);
            return row == null ? null : BuildingRow.Read(id, row, rules.Buildings);
        }

        static bool MayChangeBuildings(Rules was, Rules now) =>
            was.ModBuildings.Count > 0 || now.ModBuildings.Count > 0 || !JToken.DeepEquals(was.Buildings.Json, now.Buildings.Json);

        List<string> ChangedRows(Rules was, Rules now)
        {
            var ids = new SortedSet<string>(was.ModBuildings.Select(r => r.Id).Concat(now.ModBuildings.Select(r => r.Id)), StringComparer.Ordinal);
            if (!JToken.DeepEquals(was.Buildings.Json, now.Buildings.Json)) ids.UnionWith(BaseBuildings.Select(r => (string)r["id"]));
            return ids.Where(id => !SameCells(RowOf(id, was), was, RowOf(id, now), now)).ToList();
        }

        static bool SameCells(BuildingRow a, Rules was, BuildingRow b, Rules now)
        {
            if (a == null || b == null) return a == null && b == null;
            var x = BuildingRaster.Cells(a, was.Buildings);
            var y = BuildingRaster.Cells(b, now.Buildings);
            return x.Count == y.Count && x.Zip(y, (p, q) => p.X == q.X && p.Y == q.Y && p.Terrain == q.Terrain).All(same => same);
        }

        SortedDictionary<int, string> CellsOf(IEnumerable<string> ids, Rules rules)
        {
            var cells = new SortedDictionary<int, string>();
            foreach (var id in ids)
            {
                var row = RowOf(id, rules);
                if (row == null) continue;
                foreach (var c in BuildingRaster.Cells(row, rules.Buildings))
                    if (InBounds(c.X, c.Y)) cells[Idx(c.X, c.Y)] = c.Terrain;
            }
            return cells;
        }

        void LayBuildings(Rules was, Rules now)
        {
            if (!MayChangeBuildings(was, now)) return;
            NoteBaseBuildings();
            var ids = ChangedRows(was, now);
            var oldCells = CellsOf(ids, was);
            var newCells = CellsOf(ids, now);
            var change = new BuildingsChange(W, ids);
            var touched = new SortedSet<(int y, int x)>();
            var baseCells = BaseCellSet();
            foreach (var old in oldCells.Where(c => !newCells.ContainsKey(c.Key)))
            {
                ClearBuilding(old.Key, now);
                touched.Add(ChunkKey(old.Key));
            }
            foreach (var cell in newCells)
            {
                if (oldCells.TryGetValue(cell.Key, out var had) && had == cell.Value) continue;
                LayBuildingCell(cell.Key, cell.Value, baseCells);
                touched.Add(ChunkKey(cell.Key));
                change.Covered.Add(cell.Key);
            }
            foreach (var key in touched) FreshenBuilt(key.x, key.y);
            PushRaiders(change);
            BuildingsChanged?.Invoke(change);
        }

        HashSet<int> BaseCellSet()
        {
            var cells = new HashSet<int>();
            foreach (var row in BaseBuildings)
                foreach (var c in BuildingRaster.Cells(BuildingRow.Read((string)row["id"], row, Rules.Buildings), Rules.Buildings))
                    if (InBounds(c.X, c.Y)) cells.Add(Idx(c.X, c.Y));
            return cells;
        }

        (int y, int x) ChunkKey(int index) => (Chunks.ChunkOf(index / W), Chunks.ChunkOf(index % W));

        void LayBuildingCell(int index, string terrain, HashSet<int> baseCells)
        {
            var c = CellAt(index);
            if (!baseCells.Contains(index) && !underBuildings.ContainsKey(index))
            {
                var start = startSource != null ? StartCell(index) : c;
                underBuildings[index] = (start.T, start.Height);
            }
            c.Reset(terrain);
            c.ForgetDef();
            KeepInStart(index, c);
        }

        void ClearBuilding(int index, Rules now)
        {
            var c = CellAt(index);
            if (!underBuildings.TryGetValue(index, out var under)) under = (now.Buildings.Ground, 0);
            underBuildings.Remove(index);
            c.Reset(now.Terrain.ContainsKey(under.terrain) ? under.terrain : now.Buildings.Ground);
            c.Height = under.height;
            c.ForgetDef();
            KeepInStart(index, c);
        }

        void KeepInStart(int index, Cell c)
        {
            if (startSource == null) return;
            int x = index % W, y = index / W;
            var ch = FindChunk(Chunks.ChunkOf(x), Chunks.ChunkOf(y));
            ch.Start = WithChange(ch.Start, LocalIndex(x, y), c, x, y, startSource);
        }

        void FreshenBuilt(int cx, int cy)
        {
            var ch = FindChunk(cx, cy);
            if (ch == null) return;
            ch.SnapValid = false;
            if (startSource != null) Recount(ch);
            ChunkFreshened?.Invoke(new ChunkFresh(cx, cy));
        }

        void PushRaiders(BuildingsChange change)
        {
            foreach (var r in raiders.Where(r => r.Alive && change.Covers(r.X, r.Y)).OrderBy(r => r.Id))
            {
                var to = NearestFree(r.X, r.Y);
                if (to == null) continue;
                AddLog(string.Format(System.Globalization.CultureInfo.InvariantCulture, "A {0} is pushed out of the new walls, from ({1}, {2}) to ({3}, {4})", r.Kind, r.X, r.Y, to.Value.x, to.Value.y), LogKind.Info);
                (r.X, r.Y) = to.Value;
            }
        }

        public (int x, int y)? NearestFree(int x, int y)
        {
            var built = BuiltIndexes();
            for (int reach = 1; reach <= PushReach; reach++)
            {
                var best = NearestFreeAt(x, y, reach, built);
                if (best != null) return best;
            }
            return null;
        }

        HashSet<int> BuiltIndexes()
        {
            var built = new HashSet<int>();
            foreach (var row in RowsUnder(Rules))
                foreach (var c in BuildingRaster.Cells(row, Rules.Buildings))
                    if (InBounds(c.X, c.Y)) built.Add(Idx(c.X, c.Y));
            return built;
        }

        (int x, int y)? NearestFreeAt(int x, int y, int reach, HashSet<int> built)
        {
            (int x, int y)? best = null;
            long bestDistance = long.MaxValue;
            for (int ny = y - reach; ny <= y + reach; ny++)
                for (int nx = x - reach; nx <= x + reach; nx++)
                {
                    if (Math.Max(Math.Abs(nx - x), Math.Abs(ny - y)) != reach || !InBounds(nx, ny) || built.Contains(Idx(nx, ny))) continue;
                    var c = At(nx, ny);
                    if (IsBlocking(c) || c.Water > 0) continue;
                    long d = (long)(nx - x) * (nx - x) + (long)(ny - y) * (ny - y);
                    if (d < bestDistance)
                    {
                        bestDistance = d;
                        best = (nx, ny);
                    }
                }
            return best;
        }

        public string SiteProblem(Rules next, string title = null)
        {
            var leaving = LeavingThings(next);
            string things = ThingSiteProblem(next, title, leaving);
            if (things != null) return things;
            if (Rules.ModBuildings.Count == 0 && next.ModBuildings.Count == 0) return null;
            NoteBaseBuildings();
            var ids = ChangedRows(Rules, next);
            foreach (var id in ids)
            {
                var row = RowOf(id, next);
                if (row == null) continue;
                var had = new HashSet<(int, int)>();
                var old = RowOf(id, Rules);
                if (old != null) had.UnionWith(BuildingRaster.Cells(old, Rules.Buildings).Select(c => (c.X, c.Y)));
                var others = OtherBuildings(next, id);
                foreach (var c in BuildingRaster.Cells(row, next.Buildings).Where(c => !had.Contains((c.X, c.Y))))
                {
                    string why = CellProblem(c.X, c.Y, others, leaving);
                    if (why != null) return $"{title ?? "that mod"} can't build {id} at cell ({c.X}, {c.Y}): {why}";
                }
            }
            return OverlapProblem(next, title);
        }

        public string CellProblem(int x, int y, IReadOnlyDictionary<(int, int), string> others = null, ISet<(string, int, int)> leaving = null)
        {
            if (!InBounds(x, y)) return "it's off the world";
            if (others != null && others.TryGetValue((x, y), out var building)) return $"{building} stands there";
            var thing = ThingsNow.At(x, y);
            if (thing != null && leaving?.Contains((thing.Kind, thing.CellX, thing.CellY)) != true) return $"a {thing.Kind} ({thing.Id}) stands there";
            var c = At(x, y);
            if (c.Water > 0 || c.Frozen) return "there's water there";
            if (!HasTag(c, "ground") || IsBlocking(c)) return $"it's {TerrainOf(c).Name}, not open ground";
            return null;
        }

        Dictionary<(int, int), string> OtherBuildings(Rules rules, string except)
        {
            var cells = new Dictionary<(int, int), string>();
            foreach (var row in RowsUnder(rules).Where(r => r.Id != except))
                foreach (var c in BuildingRaster.Cells(row, rules.Buildings)) cells[(c.X, c.Y)] = row.Id;
            return cells;
        }

        string OverlapProblem(Rules next, string title)
        {
            if (next.ModBuildings.Count == 0) return null;
            NoteBaseBuildings();
            var cells = new Dictionary<(int, int), string>();
            foreach (var row in RowsUnder(next))
                foreach (var c in BuildingRaster.Cells(row, next.Buildings))
                {
                    if (cells.TryGetValue((c.X, c.Y), out var other) && other != row.Id)
                        return $"{title ?? "that mod"} leaves {other} and {row.Id} on the same cell ({c.X}, {c.Y})";
                    cells[(c.X, c.Y)] = row.Id;
                }
            return null;
        }

        public string SiteProblemOf(string patchJson, string title = null, string replacing = null)
        {
            if (RoomRules == null) return null;
            const string standIn = "\0site";
            var mods = RoomRules.IsOn(replacing) ? RoomRules.Replaced(new ActiveMod(replacing, patchJson)) : RoomRules.Mods.Append(new ActiveMod(standIn, patchJson));
            string misfit = Compose(mods, $"{title ?? "that mod"} doesn't fit the rules", out var next);
            return misfit != null ? misfit.Replace(standIn + ": ", "") : SiteProblem(next.Effective, title);
        }

        void SaveBuildings(JObject room)
        {
            if (underBuildings.Count > 0) room["under"] = new JArray(underBuildings.Select(u => new JArray(u.Key, u.Value.terrain, u.Value.height)));
        }

        void LoadBuildings(JObject room)
        {
            foreach (var u in room["under"] ?? new JArray()) underBuildings[(int)u[0]] = ((string)u[1], (int)u[2]);
        }

        internal void BuildingsIntoStart()
        {
            if (startSource == null || Rules.ModBuildings.Count == 0) return;
            var plain = RoomRules?.Base ?? Rules;
            var ids = ChangedRows(plain, Rules);
            var was = CellsOf(ids, plain);
            var now = CellsOf(ids, Rules);
            var scratch = new Cell();
            foreach (var old in was.Where(c => !now.ContainsKey(c.Key)))
            {
                var under = underBuildings.TryGetValue(old.Key, out var u) ? u : (Rules.Buildings.Ground, 0);
                scratch.Reset(under.Item1);
                scratch.Height = under.Item2;
                KeepInStart(old.Key, scratch);
            }
            foreach (var cell in now)
            {
                scratch.Reset(cell.Value);
                KeepInStart(cell.Key, scratch);
            }
        }
    }
}
