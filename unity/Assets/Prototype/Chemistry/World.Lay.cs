// The cells mods lay, in a running world (docs/design/events-and-placing.md 2b; Rules.Lay.cs).
// At a rule switch every peer lays the cells the new rules lay and the old didn't, fresh (no
// fire, gas or water; height kept), skipping any that aren't open now (a cell that burnt, got
// wet or got a thing since: the same skip on every peer, logged), and keeps what stood under.
// A cell the old rules laid and the new don't goes back to that. The starting state follows,
// as for a building, so the dragon keeps a laid tree. No rule laid, nothing runs, and LaidText
// is "", so worlds without such mods hash and save as they did.
//
// Not a port: the sandbox has no world edits.
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using Newtonsoft.Json.Linq;

namespace Chem
{
    public sealed partial class World
    {
        // What stood under each cell a mod laid (terrain and cover), by index.
        readonly SortedDictionary<int, (string terrain, string cover)> underLaid = new SortedDictionary<int, (string, string)>();

        /// <summary>How many cells the mods on have laid (those that had no room aren't counted).</summary>
        public int LaidCount => underLaid.Count;

        // The cells a rule set lays, by index (a later row wins), as terrain or "cover:" + cover; off the world left out.
        SortedDictionary<int, string> LaidBy(Rules rules)
        {
            var cells = new SortedDictionary<int, string>();
            foreach (var row in rules.ModLays)
                foreach (var (x, y) in row.Cells)
                    if (InBounds(x, y)) cells[Idx(x, y)] = row.Terrain ?? CoverMark + row.Cover;
            return cells;
        }

        const string CoverMark = "cover:";

        void LayCells(Rules was, Rules now)
        {
            if (was.ModLays.Count == 0 && now.ModLays.Count == 0) return;
            NoteBaseBuildings();
            var oldCells = LaidBy(was);
            var newCells = LaidBy(now);
            var touched = new SortedSet<(int y, int x)>();
            foreach (var old in oldCells)
            {
                if (newCells.TryGetValue(old.Key, out var same) && same == old.Value) continue;
                if (!underLaid.TryGetValue(old.Key, out var under)) continue;
                underLaid.Remove(old.Key);
                var c = Cells[old.Key];
                LayFresh(c, now.Terrain.ContainsKey(under.terrain) ? under.terrain : now.Buildings.Ground, under.cover != null && now.Covers.ContainsKey(under.cover) ? under.cover : null);
                KeepInStart(old.Key, c);
                touched.Add(ChunkKey(old.Key));
            }
            var open = PlaceOpen(null, false);
            var skipped = new SortedDictionary<string, (int skipped, int of)>(System.StringComparer.Ordinal);
            foreach (var cell in newCells)
            {
                if (oldCells.TryGetValue(cell.Key, out var had) && had == cell.Value) continue;
                skipped.TryGetValue(cell.Value, out var tally);
                tally.of++;
                var c = Cells[cell.Key];
                if (open(cell.Key % W, cell.Key / W))
                {
                    underLaid[cell.Key] = (c.T, c.Cover);
                    bool cover = cell.Value.StartsWith(CoverMark, System.StringComparison.Ordinal);
                    LayFresh(c, cover ? null : cell.Value, cover ? cell.Value.Substring(CoverMark.Length) : null);
                    KeepInStart(cell.Key, c);
                    touched.Add(ChunkKey(cell.Key));
                }
                else tally.skipped++;
                skipped[cell.Value] = tally;
            }
            foreach (var s in skipped.Where(s => s.Value.skipped > 0))
                AddLog(string.Format(CultureInfo.InvariantCulture, "{0} of {1} {2} had no room", s.Value.skipped, s.Value.of, LaidName(s.Key, now)), LogKind.Info);
            foreach (var key in touched) FreshenBuilt(key.x, key.y);
        }

        static string LaidName(string laid, Rules rules)
        {
            if (laid.StartsWith(CoverMark, System.StringComparison.Ordinal))
            {
                string id = laid.Substring(CoverMark.Length);
                return rules.Covers.TryGetValue(id, out var cv) ? cv.Name.ToLowerInvariant() : id;
            }
            return rules.Terrain.TryGetValue(laid, out var t) ? t.Name.ToLowerInvariant() : laid;
        }

        /// <summary>The cells the mods laid and what stood under each, for CastHash: "" with none, so worlds without hash as they did.</summary>
        public string LaidText()
        {
            if (underLaid.Count == 0) return "";
            var sb = new StringBuilder("|laid");
            foreach (var u in underLaid)
                sb.Append(':').Append(u.Key.ToString(CultureInfo.InvariantCulture)).Append('=').Append(u.Value.terrain).Append('/').Append(u.Value.cover ?? "-");
            return sb.ToString();
        }

        void SaveLaid(JObject save)
        {
            if (underLaid.Count > 0) save["laid"] = new JArray(underLaid.Select(u => new JArray(u.Key, u.Value.terrain, u.Value.cover)));
        }

        void LoadLaid(JObject s)
        {
            foreach (var u in (s["laid"] as JArray ?? new JArray()).OfType<JArray>())
                underLaid[(int)u[0]] = ((string)u[1], u[2].Type == JTokenType.Null ? null : (string)u[2]);
        }
    }
}
