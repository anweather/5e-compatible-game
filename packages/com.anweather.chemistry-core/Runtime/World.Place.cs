// What a spell places where it lands (docs/design/events-and-placing.md 2a): spells.json
// "place", one of a thing, an animal, a terrain or a cover, on cells picked by
// Placement.Pick from the world's seed, so every peer picks the same. A terrain or cover
// is laid fresh on the cells (shared cell state, in the cell hash like any cast). A thing
// joins World.Placed, core state: ThingsNow is the file's and the mods' things and these,
// each with an id above any the others reach (ThingId.Placed), gone once it lapses. An
// animal is the view's (J-056): the core lists this tick's (World.Summoned) and each peer
// brings one out on that cell with that Individual, to live as ambient life does.
// PlacedText joins CastHash and the save, and is "" until a spell places, so worlds
// without hash and save as they did.
//
// Not a port: the sandbox has no placing.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Newtonsoft.Json.Linq;

namespace Chem
{
    public readonly struct Summon
    {
        public Summon(int n, string species, int x, int y, uint individual, int tick)
        {
            N = n;
            Species = species;
            X = x;
            Y = y;
            Individual = individual;
            Tick = tick;
        }

        public int N { get; }
        public string Species { get; }
        public int X { get; }
        public int Y { get; }
        public uint Individual { get; }
        public int Tick { get; }
    }

    public sealed partial class World
    {
        const int PlaceSalt = 0x706c6163, TurnSalt = 0x7475726e, SummonSalt = 0x73756d6e;

        public const int DefaultMaxPlaced = 64;

        readonly List<ThingSpawn> placedThings = new List<ThingSpawn>();
        readonly List<Summon> summoned = new List<Summon>();
        readonly List<Summon> summonHistory = new List<Summon>();
        int placedVersion;
        int placeActions, placedCount;
        HashSet<(int, int)> builtCells;
        Rules builtFor;

        public IReadOnlyList<ThingSpawn> Placed => placedThings;

        public IReadOnlyList<Summon> Summoned => summoned;

        public int LastSummonN => summonHistory.Count > 0 ? summonHistory[summonHistory.Count - 1].N : -1;

        public IEnumerable<Summon> SummonedAfter(int n)
        {
            foreach (var s in summonHistory) if (s.N > n) yield return s;
        }

        void BeginPlaceTick()
        {
            summoned.Clear();
            if (summonHistory.Count > 0) summonHistory.RemoveAll(s => s.Tick <= Tick - HistoryTicks);
        }

        void TickPlaced()
        {
            if (placedThings.Count == 0) return;
            if (placedThings.RemoveAll(t => t.Lapses > 0 && t.Lapses <= Tick) > 0) placedVersion++;
        }

        int MaxPlaced => (int)(roomBudgets?.Number("place.maxLive", DefaultMaxPlaced) ?? DefaultMaxPlaced);

        void Place(SpellDef s, List<(int x, int y)> cells, int x, int y)
        {
            var p = s.Place;
            if (p == null) return;
            PlaceOn(p, s.Name, p.Within >= 0 ? Placement.Around(x, y, p.Within) : cells);
        }

        void PlaceOn(PlaceDef p, string name, List<(int x, int y)> area)
        {
            int salt = unchecked((int)WorldHash.Hash4(unchecked((int)Seed), PlaceSalt, Tick, placeActions));
            placeActions++;
            if (WrapX) area = area.ConvertAll(c => (FoldX(c.x), FoldY(c.y)));
            var picks = Placement.Pick(area, p.Count, p.Spacing, PlaceOpen(p.Onto), unchecked((int)Seed), salt);
            foreach (var (cx, cy) in picks)
            {
                if (p.Thing != null) PlaceThing(p, cx, cy, salt);
                else if (p.Animal != null) SummonAnimal(p.Animal, cx, cy);
                else if (p.Terrain != null) LayFresh(At(cx, cy), p.Terrain);
                else if (p.Cover != null) LayFresh(At(cx, cy), null, p.Cover);
            }
            if (picks.Count < p.Count)
                AddLog(picks.Count == 0 ? $"{name} finds no room" : $"{name}: {picks.Count} of {p.Count} had room", LogKind.Info);
        }

        void PlaceThing(PlaceDef p, int x, int y, int salt)
        {
            int n = placedCount++;
            placedThings.Add(PlacedSpawn(n, p.Thing, x, y, (int)(WorldHash.Hash4(salt, TurnSalt, x, y) % 4) * 90, p.Lasts > 0 ? Tick + p.Lasts : 0));
            int max = Math.Max(1, MaxPlaced);
            if (placedThings.Count > max) placedThings.RemoveRange(0, placedThings.Count - max);
            placedVersion++;
        }

        static ThingSpawn PlacedSpawn(int n, string kind, int x, int y, int rotation, int lapses)
        {
            int cx = Chunks.ChunkOf(x), cy = Chunks.ChunkOf(y);
            return new ThingSpawn
            {
                Id = ThingId.Placed(cx, cy, n),
                Kind = kind,
                ChunkX = cx,
                ChunkY = cy,
                LocalX = x - cx * Chunks.Size,
                LocalY = y - cy * Chunks.Size,
                Rotation = rotation,
                Placed = true,
                Lapses = lapses,
            };
        }

        void SummonAnimal(string species, int x, int y)
        {
            x = FoldX(x);
            y = FoldY(y);
            int n = placedCount++;
            uint individual = WorldHash.Hash4(unchecked((int)Seed), SummonSalt, n, 0) | 1u;
            var summon = new Summon(n, species, x, y, individual, Tick);
            summoned.Add(summon);
            summonHistory.Add(summon);
        }

        void LayFresh(Cell c, string terrain, string cover = null)
        {
            int height = c.Height, star = c.Star;
            c.Reset(terrain ?? c.T);
            c.Height = height;
            c.Star = star;
            c.ForgetDef();
            if (cover != null) PutCover(c, cover);
        }

        public Func<int, int, bool> PlaceOpen(IReadOnlyList<string> onto, bool activeOnly = true)
        {
            var built = BuiltCells();
            var things = new HashSet<(int, int)>(ThingsNow.All.Select(t => (t.CellX, t.CellY)));
            return (x, y) =>
            {
                x = FoldX(x);
                y = FoldY(y);
                if (!InBounds(x, y) || (activeOnly && !IsActiveAt(x, y)) || built.Contains((x, y)) || things.Contains((x, y))) return false;
                var c = At(x, y);
                if (c.Water > 0 || c.Frozen || IsBlocking(c)) return false;
                if (onto == null || onto.Count == 0) return HasTag(c, "ground");
                foreach (var tag in onto) if (HasTag(c, tag)) return true;
                return false;
            };
        }

        HashSet<(int, int)> BuiltCells()
        {
            if (builtCells != null && ReferenceEquals(builtFor, Rules)) return builtCells;
            builtCells = new HashSet<(int, int)>();
            foreach (var row in RowsUnder(Rules))
                foreach (var c in BuildingRaster.Cells(row, Rules.Buildings)) builtCells.Add((c.X, c.Y));
            builtFor = Rules;
            return builtCells;
        }

        public string PlacedText()
        {
            if (placeActions == 0) return "";
            var sb = new StringBuilder();
            sb.Append("|place:").Append(Whole(placeActions)).Append(':').Append(Whole(placedCount));
            foreach (var t in placedThings)
                sb.Append('|').Append(Whole(t.Id.PlacedN)).Append(':').Append(t.Kind).Append(':')
                  .Append(Whole(t.CellX)).Append(',').Append(Whole(t.CellY)).Append(':').Append(Whole(t.Rotation)).Append(':').Append(Whole(t.Lapses));
            return sb.ToString();
        }

        void SavePlaced(JObject save)
        {
            if (placeActions == 0) return;
            save["placedThings"] = new JObject
            {
                ["actions"] = placeActions,
                ["count"] = placedCount,
                ["things"] = new JArray(placedThings.Select(t => new JArray(t.Id.PlacedN, t.Kind, t.CellX, t.CellY, t.Rotation, t.Lapses))),
            };
            if (summonHistory.Count > 0)
                save["placedThings"]["summons"] = new JArray(summonHistory.Select(a => new JArray(a.N, a.Species, a.X, a.Y, (long)a.Individual, a.Tick)));
        }

        void LoadPlaced(JObject s)
        {
            if (!(s["placedThings"] is JObject p)) return;
            placeActions = (int)p["actions"];
            placedCount = (int)p["count"];
            foreach (var t in (p["things"] as JArray ?? new JArray()).OfType<JArray>())
                placedThings.Add(PlacedSpawn((int)t[0], (string)t[1], (int)t[2], (int)t[3], (int)t[4], (int)t[5]));
            foreach (var a in (p["summons"] as JArray ?? new JArray()).OfType<JArray>())
                summonHistory.Add(new Summon((int)a[0], (string)a[1], (int)a[2], (int)a[3], (uint)(long)a[4], (int)a[5]));
            placedVersion++;
        }
    }
}
