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
    /// <summary>An animal a spell brought out: its species (fauna.json), its cell, and the Individual every peer gives it.</summary>
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

        /// <summary>Its place in the world's running count of what spells placed: a view brings out those past the last it saw.</summary>
        public int N { get; }
        public string Species { get; }
        public int X { get; }
        public int Y { get; }
        /// <summary>Odd, never 0: what ValleyFauna.Spawn takes for its coat, size and gait.</summary>
        public uint Individual { get; }
        /// <summary>The tick it came out on (the last whole tick when it was cast).</summary>
        public int Tick { get; }
    }

    public sealed partial class World
    {
        const int PlaceSalt = 0x706c6163, TurnSalt = 0x7475726e, SummonSalt = 0x73756d6e;

        /// <summary>The most placed things live in a room at once when no budgets say (budgets.json place.maxLive): the oldest lapses first.</summary>
        public const int DefaultMaxPlaced = 64;

        readonly List<ThingSpawn> placedThings = new List<ThingSpawn>();
        readonly List<Summon> summoned = new List<Summon>();
        readonly List<Summon> summonHistory = new List<Summon>();
        // Bumped whenever placedThings changes, so ThingsNow is worked out again.
        int placedVersion;
        // Place actions run, and things and animals placed, since the world began: core state.
        int placeActions, placedCount;
        // The cells of the buildings in force, for the rules they were worked out under.
        HashSet<(int, int)> builtCells;
        Rules builtFor;

        /// <summary>The things spells placed that are still there, oldest first (also in ThingsNow).</summary>
        public IReadOnlyList<ThingSpawn> Placed => placedThings;

        /// <summary>The animals spells brought out since this tick began, for the view to bring out (as NewMeteors).</summary>
        public IReadOnlyList<Summon> Summoned => summoned;

        /// <summary>The N of the last animal brought out within the last 600 ticks, or -1: a view compares it with the last it saw, and asks SummonedAfter only when it's past.</summary>
        public int LastSummonN => summonHistory.Count > 0 ? summonHistory[summonHistory.Count - 1].N : -1;

        /// <summary>The animals brought out after the <paramref name="n"/>th (within the last 600 ticks), oldest first: for a view that steps several ticks a frame.</summary>
        public IEnumerable<Summon> SummonedAfter(int n)
        {
            foreach (var s in summonHistory) if (s.N > n) yield return s;
        }

        // Each new tick: this tick's list starts empty, and the history keeps its last HistoryTicks.
        void BeginPlaceTick()
        {
            summoned.Clear();
            if (summonHistory.Count > 0) summonHistory.RemoveAll(s => s.Tick <= Tick - HistoryTicks);
        }

        // Once a tick: placed things whose time is up go, in the order placed.
        void TickPlaced()
        {
            if (placedThings.Count == 0) return;
            if (placedThings.RemoveAll(t => t.Lapses > 0 && t.Lapses <= Tick) > 0) placedVersion++;
        }

        int MaxPlaced => (int)(roomBudgets?.Number("place.maxLive", DefaultMaxPlaced) ?? DefaultMaxPlaced);

        /// <summary>
        /// A spell's place (Land, after its element): Count cells picked among the spell's cells, or within Within of
        /// the aim when it gives one, by the world's seed, the tick and the actions run so far; each gets what's placed.
        /// </summary>
        void Place(SpellDef s, List<(int x, int y)> cells, int x, int y)
        {
            var p = s.Place;
            if (p == null) return;
            PlaceOn(p, s.Name, p.Within >= 0 ? Placement.Around(x, y, p.Within) : cells);
        }

        // A place's Count cells picked over an area, by the world's seed, the tick and the actions run so far; each gets what's placed.
        void PlaceOn(PlaceDef p, string name, List<(int x, int y)> area)
        {
            int salt = unchecked((int)WorldHash.Hash4(unchecked((int)Seed), PlaceSalt, Tick, placeActions));
            placeActions++;
            if (WrapX) area = area.ConvertAll(c => (FoldX(c.x), c.y)); // cells past the seam are where they fold to
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
            int n = placedCount++;
            uint individual = WorldHash.Hash4(unchecked((int)Seed), SummonSalt, n, 0) | 1u;
            var summon = new Summon(n, species, x, y, individual, Tick);
            summoned.Add(summon);
            summonHistory.Add(summon);
        }

        // A cell laid fresh, as a building lays it: no fire, gas or water, its height and target mark kept.
        void LayFresh(Cell c, string terrain, string cover = null)
        {
            int height = c.Height, star = c.Star;
            c.Reset(terrain ?? c.T);
            c.Height = height;
            c.Star = star;
            c.ForgetDef();
            if (cover != null) PutCover(c, cover);
        }

        /// <summary>
        /// Whether something may be placed on a cell: on the world (and active, with <paramref name="activeOnly"/>), open
        /// ground (a terrain with one of <paramref name="onto"/>'s tags, or "ground" with none), not blocking, dry, and with
        /// no building or thing on it. A cast places only where it's active; a world edit lays anywhere.
        /// </summary>
        public Func<int, int, bool> PlaceOpen(IReadOnlyList<string> onto, bool activeOnly = true)
        {
            var built = BuiltCells();
            var things = new HashSet<(int, int)>(ThingsNow.All.Select(t => (t.CellX, t.CellY)));
            return (x, y) =>
            {
                x = FoldX(x);
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

        /// <summary>
        /// What spells placed, for CastHash: the actions run, the count, then each thing still there (n, kind, cell,
        /// turn, the tick it lapses). "" until a spell places, so worlds without hash as they did.
        /// </summary>
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
        }

        void LoadPlaced(JObject s)
        {
            if (!(s["placedThings"] is JObject p)) return;
            placeActions = (int)p["actions"];
            placedCount = (int)p["count"];
            foreach (var t in (p["things"] as JArray ?? new JArray()).OfType<JArray>())
                placedThings.Add(PlacedSpawn((int)t[0], (string)t[1], (int)t[2], (int)t[3], (int)t[4], (int)t[5]));
            placedVersion++;
        }
    }
}
