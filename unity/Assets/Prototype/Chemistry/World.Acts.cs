// Acts (docs/design/world-acts.md): something that happens once, now, as a room command (RoomChange.Act), not a rule that
// keeps holding. "Add 20 trees near me" is an act: what it lays, places, brings out or casts goes through the same verbs
// a spell's place does, the mod list stays as it was, and a second one doesn't make a second mod. The cells come in the
// command (Workbench.Act picks them from the world it reads), so each peer lays the same ones at the same tick, skipping
// the same cells that aren't open. A room's change mode decides an act as it does a mod, and what each did is kept, so
// RoomChange.Undo can take back the last ones: ground it laid goes back to what stood under, unless something has
// changed it since, and things it placed go. Animals live in each peer's view (J-056) and casts are over, so those stay.
// ActText joins CastHash and the save, "" until an act, so worlds without hash as they did.
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Chem
{
    public sealed partial class World
    {
        /// <summary>What an act can be: laying a terrain or cover, placing a thing, bringing out an animal, or casting a spell.</summary>
        public const string ActTerrain = "terrain", ActCover = "cover", ActThing = "thing", ActAnimal = "animal", ActCast = "cast";

        /// <summary>How many acts are kept to undo: the oldest are forgotten, and stay done.</summary>
        public const int MostActsKept = 32;

        sealed class ActDone
        {
            public int N;
            public string By, Kind, What;
            public readonly List<(int key, string terrain, string cover)> Cells = new List<(int, string, string)>();
            public readonly List<int> Things = new List<int>();
        }

        readonly List<ActDone> acts = new List<ActDone>();
        int actCount;

        RoomVerdict DoAct(RoomCommand c)
        {
            string why = RoomPolicy.Refusal(Room.Mode, Room.Host, c.Peer);
            if (why != null) return Refused(c, why);
            var ask = ReadAct(c.Json);
            if (!ActKnown((string)ask["kind"], (string)ask["what"])) return Refused(c, "that isn't something to do");
            string kind = (string)ask["kind"], what = (string)ask["what"];
            var cells = (ask["cells"] as JArray ?? new JArray()).OfType<JArray>().Select(a => ((int)a[0], (int)a[1])).ToList();
            var act = new ActDone { N = actCount + 1, By = c.Peer, Kind = kind, What = what };
            int done = kind == ActCast ? CastAct(act, cells) : PlaceAct(act, cells, ask);
            if (done == 0) return Refused(c, "none of those cells had room");
            actCount++;
            acts.Add(act);
            if (acts.Count > MostActsKept) acts.RemoveAt(0);
            string left = done < cells.Count ? $"; {cells.Count - done} had no room" : "";
            return new RoomVerdict(true, null, $"{c.Peer} {(kind == ActCast ? "cast" : "brought out")} {Whole(done)} {what} (act {Whole(act.N)}){left}");
        }

        static JObject ReadAct(string json)
        {
            try
            {
                return JObject.Parse(json);
            }
            catch (JsonException)
            {
                return new JObject();
            }
        }

        bool ActKnown(string kind, string what)
        {
            if (string.IsNullOrEmpty(what)) return false;
            switch (kind)
            {
                case ActTerrain: return Rules.Terrain.ContainsKey(what);
                case ActCover: return Rules.Covers.ContainsKey(what);
                case ActThing: return Rules.Entities.ContainsKey(what) || Rules.Items.ContainsKey(what);
                case ActAnimal: return Rules.Fauna.Species(what) != null;
                case ActCast: return Rules.Spell(what) != null;
                default: return false;
            }
        }

        // Each open cell gets what's asked for; the count of those that did.
        int PlaceAct(ActDone act, List<(int x, int y)> cells, JObject ask)
        {
            var onto = (ask["onto"] as JArray)?.Select(t => (string)t).ToList();
            var open = PlaceOpen(onto, false);
            int lasts = (int?)ask["lasts"] ?? 0, salt = unchecked((int)WorldHash.Hash4(unchecked((int)Seed), PlaceSalt, Tick, placeActions));
            placeActions++;
            var touched = new SortedSet<(int y, int x)>();
            int done = 0;
            foreach (var (x, y) in cells)
            {
                if (!open(x, y)) continue;
                done++;
                if (act.Kind == ActAnimal) SummonAnimal(act.What, x, y);
                else if (act.Kind == ActThing) PlaceActThing(act, x, y, lasts, salt);
                else LayAct(act, x, y, touched);
            }
            foreach (var key in touched) FreshenBuilt(key.x, key.y);
            return done;
        }

        void PlaceActThing(ActDone act, int x, int y, int lasts, int salt)
        {
            act.Things.Add(placedCount);
            PlaceThing(new PlaceDef { Thing = act.What, Lasts = lasts }, x, y, salt);
        }

        void LayAct(ActDone act, int x, int y, SortedSet<(int y, int x)> touched)
        {
            int key = Idx(x, y);
            var c = Cells[key];
            act.Cells.Add((key, c.T, c.Cover));
            LayFresh(c, act.Kind == ActTerrain ? act.What : null, act.Kind == ActCover ? act.What : null);
            KeepInStart(key, c);
            touched.Add(ChunkKey(key));
        }

        // A spell cast at each cell as a trigger's is, with its own delay, the view told by the triggered list.
        int CastAct(ActDone act, List<(int x, int y)> cells)
        {
            var s = Rules.Spell(act.What);
            int dir = DirOf(0, 1), done = 0;
            foreach (var (x, y) in cells)
            {
                if (!InBounds(x, y) || !Release(s, (x, y), dir, 1, (0, 1), s.Delay, $"{act.By} calls down {s.Name}")) continue;
                done++;
                var cast = new TriggeredCast { N = ++triggeredCasts, Trigger = "act", Player = act.By, Spell = s.Id, X = x, Y = y, Dir = dir, Tick = Tick, Lands = Tick + s.Delay };
                triggered.Add(cast);
                triggeredHistory.Add(cast);
            }
            return done;
        }

        // The act named, or with none the peer's last that's kept.
        RoomVerdict UndoAct(RoomCommand c)
        {
            string why = RoomPolicy.Refusal(Room.Mode, Room.Host, c.Peer);
            if (why != null) return Refused(c, why);
            var act = int.TryParse(c.Json, NumberStyles.None, CultureInfo.InvariantCulture, out int n)
                ? acts.Find(a => a.N == n)
                : acts.FindLast(a => a.By == c.Peer);
            if (act == null) return Refused(c, "there's no act to take back");
            if (act.Kind == ActAnimal || act.Kind == ActCast) return Refused(c, act.Kind == ActCast ? "a cast can't be taken back" : "animals can't be taken back once they're out");
            acts.Remove(act);
            int gone = act.Things.Count > 0 ? TakeThings(act) : TakeCells(act);
            return new RoomVerdict(true, null, $"{c.Peer} took back {Whole(gone)} {act.What} (act {Whole(act.N)})");
        }

        int TakeThings(ActDone act)
        {
            int gone = placedThings.RemoveAll(t => act.Things.Contains(t.Id.PlacedN));
            if (gone > 0) placedVersion++;
            return gone;
        }

        int TakeCells(ActDone act)
        {
            var touched = new SortedSet<(int y, int x)>();
            int gone = 0;
            foreach (var (key, terrain, cover) in act.Cells)
            {
                var c = Cells[key];
                // Burnt, felled or built on since: what's there now stands.
                if ((act.Kind == ActTerrain ? c.T : c.Cover) != act.What) continue;
                LayFresh(c, Rules.Terrain.ContainsKey(terrain) ? terrain : Rules.Buildings.Ground, cover != null && Rules.Covers.ContainsKey(cover) ? cover : null);
                KeepInStart(key, c);
                touched.Add(ChunkKey(key));
                gone++;
            }
            foreach (var key in touched) FreshenBuilt(key.x, key.y);
            return gone;
        }

        /// <summary>The acts kept to undo, newest first, in words: for the tools.</summary>
        public IEnumerable<string> ActsKept() =>
            Enumerable.Reverse(acts).Select(a => $"act {Whole(a.N)}: {a.What} by {a.By}");

        /// <summary>The acts done and kept, for CastHash: "" until one, so worlds without hash as they did.</summary>
        public string ActText()
        {
            if (actCount == 0) return "";
            var sb = new StringBuilder("|acts:").Append(Whole(actCount));
            foreach (var a in acts)
            {
                sb.Append('|').Append(Whole(a.N)).Append(':').Append(a.By).Append(':').Append(a.Kind).Append(':').Append(a.What);
                foreach (var (key, terrain, cover) in a.Cells) sb.Append(',').Append(Whole(key)).Append('=').Append(terrain).Append('/').Append(cover);
                foreach (int t in a.Things) sb.Append(",t").Append(Whole(t));
            }
            return sb.ToString();
        }

        void SaveActs(JObject save)
        {
            if (actCount == 0) return;
            save["acts"] = new JObject
            {
                ["count"] = actCount,
                ["kept"] = new JArray(acts.Select(a => new JObject
                {
                    ["n"] = a.N,
                    ["by"] = a.By,
                    ["kind"] = a.Kind,
                    ["what"] = a.What,
                    ["cells"] = new JArray(a.Cells.Select(c => new JArray(c.key, c.terrain, c.cover))),
                    ["things"] = new JArray(a.Things),
                })),
            };
        }

        void LoadActs(JObject s)
        {
            if (!(s["acts"] is JObject saved)) return;
            actCount = (int)saved["count"];
            foreach (var o in (saved["kept"] as JArray ?? new JArray()).OfType<JObject>())
            {
                var a = new ActDone { N = (int)o["n"], By = (string)o["by"], Kind = (string)o["kind"], What = (string)o["what"] };
                foreach (var c in (o["cells"] as JArray ?? new JArray()).OfType<JArray>()) a.Cells.Add(((int)c[0], (string)c[1], (string)c[2]));
                foreach (var t in o["things"] as JArray ?? new JArray()) a.Things.Add((int)t);
                acts.Add(a);
            }
        }
    }
}
