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
    /// <summary>What a rule switch did to the world's buildings. For the view: a handler must not change the world.</summary>
    public sealed class BuildingsChange
    {
        internal HashSet<int> Covered = new HashSet<int>();
        readonly int w;

        internal BuildingsChange(int w, IReadOnlyList<string> ids)
        {
            this.w = w;
            Ids = ids;
        }

        /// <summary>The buildings whose rows changed, whose drawing must follow: World.BuildingRowOf(id) is the row, null once it's gone.</summary>
        public IReadOnlyList<string> Ids { get; }

        /// <summary>Whether this cell was just built on, so whoever stands there is pushed out (World.NearestFree).</summary>
        public bool Covers(int x, int y) => Covered.Contains(y * w + x);
    }

    public sealed partial class World
    {
        // How far a push looks for a free cell, in cells.
        const int PushReach = 48;

        List<JObject> baseBuildings;
        // What stood under a cell a mod's building was laid on (terrain and height), by index.
        readonly SortedDictionary<int, (string terrain, int height)> underBuildings = new SortedDictionary<int, (string, int)>();

        /// <summary>A rule switch changed what's built. Raised after the cells are laid, with the chunks raised on ChunkFreshened.</summary>
        public event Action<BuildingsChange> BuildingsChanged;

        /// <summary>The rows of the buildings the world was made with (WorldBuildings), by the world's starting cells.</summary>
        internal IReadOnlyList<JObject> BaseBuildings => baseBuildings ??= (roomBudgets?.Buildings ?? WorldBuildings.Empty).RowsOn(StartTerrain, Rules.Buildings);

        // Before a mod's building lands, so the rows read the world as it was made.
        internal void NoteBaseBuildings() => _ = BaseBuildings;

        string StartTerrain(int x, int y)
        {
            if (!InBounds(x, y)) return null;
            return startAt != null ? startShapes[startAt[Idx(x, y)]].T : At(x, y).T;
        }

        /// <summary>Every building in force, in order: those the world was made with, then those the mods added.</summary>
        public IReadOnlyList<BuildingRow> BuildingsNow => RowsUnder(Rules);

        /// <summary>Whether the world was made with this building, so a mod's row of that id edits it.</summary>
        public bool IsBaseBuilding(string id) => BaseBuildings.Any(r => (string)r["id"] == id);

        /// <summary>The buildings drawn from their rows: what the mods brought and edited, and the world's rows laid at the start. The rest stand as the valley drew them.</summary>
        public List<BuildingRow> RowsDrawn()
        {
            var laid = new HashSet<string>((roomBudgets?.Buildings ?? WorldBuildings.Empty).LaidRows.Select(r => (string)r["id"]), StringComparer.Ordinal);
            return BuildingsNow.Where(r => (laid.Contains(r.Id) && IsBaseBuilding(r.Id)) || Rules.ModBuildings.Any(m => m.Id == r.Id)).ToList();
        }

        /// <summary>A building in force by id, or null.</summary>
        public BuildingRow BuildingRowOf(string id) => RowOf(id, Rules);

        /// <summary>What a building's cells are under the rules in force, in world cells.</summary>
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

        // Whether a switch can change what's built: a mod's rows, or tables the world's own rows read through (#85).
        static bool MayChangeBuildings(Rules was, Rules now) =>
            was.ModBuildings.Count > 0 || now.ModBuildings.Count > 0 || !JToken.DeepEquals(was.Buildings.Json, now.Buildings.Json);

        // The rows that differ between two rule sets, in id order: only what decides cells counts. A table
        // change can redraw the world's own rows (a drawn-door that becomes a gap), so they're compared too.
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

        // The cells a list of rows covers, by index, in order (a later row wins); cells off the world are left out.
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

        // ---------- laying what changed ----------

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
                var start = startAt != null ? startShapes[startAt[index]] : c;
                underBuildings[index] = (start.T, start.Height);
            }
            c.Reset(terrain);
            c.ForgetDef();
            KeepInStart(index, c);
        }

        // A wall taken away: what stood under it, or for a cell the world was made with, the plain ground.
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
            if (startAt == null) return;
            int shape = startShapes.FindIndex(s => SameCell(s, c));
            if (shape < 0)
            {
                var copy = new Cell();
                copy.CopyFrom(c);
                startShapes.Add(copy);
                shape = startShapes.Count - 1;
            }
            startAt[index] = shape;
        }

        // A chunk's cells were laid over: its water is checked afresh, the audit counts again, and the view redraws it.
        void FreshenBuilt(int cx, int cy)
        {
            var ch = FindChunk(cx, cy);
            if (ch == null) return;
            ch.SnapValid = false;
            if (startAt != null) Recount(ch);
            ChunkFreshened?.Invoke(new ChunkFresh(cx, cy));
        }

        // Raiders on a cell just built on, in id order, each to the nearest free cell.
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

        /// <summary>
        /// The nearest cell someone standing here can be pushed to: not blocking, not under water, and in no
        /// building; nearest by distance, then north to south and west to east; null if none is within reach.
        /// The view asks this for its players, who aren't core state, and logs its own line (BuildingsChanged).
        /// </summary>
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

        // The cells of every building standing now, by index (NearestFree's own, uncached, unlike BuiltCells).
        HashSet<int> BuiltIndexes()
        {
            var built = new HashSet<int>();
            foreach (var row in RowsUnder(Rules))
                foreach (var c in BuildingRaster.Cells(row, Rules.Buildings))
                    if (InBounds(c.X, c.Y)) built.Add(Idx(c.X, c.Y));
            return built;
        }

        // The nearest free cell on the square ring this far out, north to south and west to east on ties; null if none.
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

        // ---------- the site check, on every peer at the tick ----------

        /// <summary>
        /// Whether these rules' changed rows may stand: null if so, else one sentence. Only cells a building
        /// newly takes count, each of which must be in the world, clear of other buildings and things, dry and open ground.
        /// </summary>
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

        /// <summary>Why a building can't stand on this cell (null if it can), other than on the given buildings' cells or under the things leaving.</summary>
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

        // Two buildings on one cell, among those a mod has a hand in.
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

        /// <summary>
        /// The site problem a patch would meet here if it switched on now: null if none. It doesn't switch it on.
        /// With <paramref name="replacing"/>, a mod on that is an earlier state of this same patch, it goes in that mod's
        /// place rather than over it, so what that mod put in the world doesn't count against the patch.
        /// A patch that doesn't compose with the mods on is a sentence too, never a ModException: this runs on the
        /// game's main thread for the tools (GameRoomDesk), where a throw would reach no one.
        /// </summary>
        public string SiteProblemOf(string patchJson, string title = null, string replacing = null)
        {
            if (RoomRules == null) return null;
            const string standIn = "\0site";
            var mods = RoomRules.IsOn(replacing) ? RoomRules.Replaced(new ActiveMod(replacing, patchJson)) : RoomRules.Mods.Append(new ActiveMod(standIn, patchJson));
            string misfit = Compose(mods, $"{title ?? "that mod"} doesn't fit the rules", out var next);
            return misfit != null ? misfit.Replace(standIn + ": ", "") : SiteProblem(next.Effective, title);
        }

        // ---------- the save ----------

        void SaveBuildings(JObject room)
        {
            if (underBuildings.Count > 0) room["under"] = new JArray(underBuildings.Select(u => new JArray(u.Key, u.Value.terrain, u.Value.height)));
        }

        void LoadBuildings(JObject room)
        {
            foreach (var u in room["under"] ?? new JArray()) underBuildings[(int)u[0]] = ((string)u[1], (int)u[2]);
        }

        // A load makes the starting state from the source, without the mods' buildings: they go in again.
        internal void BuildingsIntoStart()
        {
            if (startAt == null || Rules.ModBuildings.Count == 0) return;
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
