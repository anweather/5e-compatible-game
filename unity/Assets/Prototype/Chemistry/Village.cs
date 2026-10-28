// A village laid out on cells from a recipe and a seed (D12, docs/WORLD_LIFE.md):
// a square with a well in the middle, the inn and the shop facing it, lanes
// leading out, and houses on plots along the square and the lanes, each facing
// its street. Only the layout lives here: footprints are wall cells (timber
// burns like the cabins' log walls, stone doesn't), so every machine must lay
// the same village. How a building looks is the view's (BuildingMesher), from
// the building's own seed.
//
// Every choice is a WorldHash of the village's seed and a counter, never a
// stream, and all maths is integer, so a plan is the same everywhere and
// VillageTests can pin its digest. Not a port: the sandbox has no villages.
using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Chem
{
    public enum BuildingKind { House, Inn, Shop }
    public enum WallKind { Timber, Stone, Log }
    public enum RoofKind { Gable, Hip }
    public enum RoofCover { Thatch, Slate, Shingle }

    /// <summary>A direction on the cell grid: north is -y (y grows south), east is +x.</summary>
    public enum Side { North, East, South, West }

    public static class Sides
    {
        public static (int dx, int dy) Step(Side s) => s switch
        {
            Side.North => (0, -1),
            Side.East => (1, 0),
            Side.South => (0, 1),
            _ => (-1, 0),
        };

        public static Side Opposite(Side s) => (Side)(((int)s + 2) % 4);
    }

    /// <summary>A rectangle of cells: x, y its top-left (north-west) cell.</summary>
    public readonly struct CellRect : IEquatable<CellRect>
    {
        public readonly int X, Y, W, H;

        public CellRect(int x, int y, int w, int h)
        {
            X = x;
            Y = y;
            W = w;
            H = h;
        }

        public int Right => X + W;
        public int Bottom => Y + H;
        public bool Contains(int x, int y) => x >= X && x < Right && y >= Y && y < Bottom;
        public CellRect Grown(int by) => new CellRect(X - by, Y - by, W + 2 * by, H + 2 * by);

        public IEnumerable<(int x, int y)> Cells()
        {
            for (int y = Y; y < Bottom; y++)
                for (int x = X; x < Right; x++) yield return (x, y);
        }

        public bool Equals(CellRect o) => X == o.X && Y == o.Y && W == o.W && H == o.H;
        public override bool Equals(object obj) => obj is CellRect o && Equals(o);
        public override int GetHashCode() => ((X * 397 ^ Y) * 397 ^ W) * 397 ^ H;
        public override string ToString() => $"({X},{Y} {W}x{H})";
        public static bool operator ==(CellRect a, CellRect b) => a.Equals(b);
        public static bool operator !=(CellRect a, CellRect b) => !a.Equals(b);
    }

    /// <summary>One building of a village plan: what it is, where it stands and how it's built.</summary>
    public sealed class VillageBuilding
    {
        public BuildingKind Kind { get; internal set; }
        /// <summary>The cells it stands on, all of them wall.</summary>
        public CellRect Footprint { get; internal set; }
        /// <summary>The way its front (and door) faces: toward its street.</summary>
        public Side Door { get; internal set; }
        /// <summary>Its size in cells along its front and from front to back.</summary>
        public int Width { get; internal set; }
        public int Depth { get; internal set; }
        public int Storeys { get; internal set; }
        public WallKind Walls { get; internal set; }
        public RoofKind Roof { get; internal set; }
        public RoofCover Cover { get; internal set; }
        public bool Chimney { get; internal set; }
        /// <summary>Its own seed, for the view's details and colours.</summary>
        public int Seed { get; internal set; }

        /// <summary>What its walls and roof are made of: terrain ids (world/villages.json "materials").</summary>
        public string WallMaterial { get; internal set; } = "logwall";
        public string RoofMaterial { get; internal set; } = "thatch";

        /// <summary>The cell character its walls are in a map (ChemGrid's legend), from their material.</summary>
        public char WallCell => BuildingMaterials.CellOf(WallMaterial);
    }

    /// <summary>
    /// What a building's walls and roof are made of, by kind (world/villages.json "materials"): terrain
    /// ids in materials.json, so a timber house's walls burn like log walls and a stone one's don't, and a
    /// thatch roof burns where slate doesn't. The walls stand on the footprint's cells; the roof isn't a
    /// cell (yet), but carries its material.
    /// </summary>
    public sealed class BuildingMaterials
    {
        public Dictionary<WallKind, string> Walls { get; } = new Dictionary<WallKind, string>
        {
            [WallKind.Timber] = "logwall", [WallKind.Stone] = "stone", [WallKind.Log] = "logwall",
        };
        public Dictionary<RoofCover, string> Roofs { get; } = new Dictionary<RoofCover, string>
        {
            [RoofCover.Thatch] = "thatch", [RoofCover.Slate] = "slate", [RoofCover.Shingle] = "shingle",
        };

        public string WallOf(WallKind k) => Walls[k];
        public string RoofOf(RoofCover c) => Roofs[c];

        // The map characters wall materials stand as (ChemGrid's legend).
        static readonly Dictionary<string, char> Cells = new Dictionary<string, char>
        {
            ["logwall"] = 'L', ["stone"] = '#', ["wood"] = '=', ["metal"] = 'm', ["bedrock"] = '%',
        };

        /// <summary>A wall material's map character; throws for one the map has no character for.</summary>
        public static char CellOf(string material) =>
            Cells.TryGetValue(material, out char c) ? c : throw new FormatException($"villages.json: no map cell for wall material {material}");

        /// <summary>Whether a wall material can stand on a map (has a character).</summary>
        public static bool CanBeWall(string material) => Cells.ContainsKey(material);
    }

    /// <summary>What a village should have (world/villages.json).</summary>
    public sealed class VillageSpec
    {
        public string Name { get; set; } = "Village";
        /// <summary>The world cell the square is centred on.</summary>
        public int X { get; set; }
        public int Y { get; set; }
        public int Seed { get; set; }
        /// <summary>The square's side in cells, smallest and largest.</summary>
        public int SquareMin { get; set; } = 10;
        public int SquareMax { get; set; } = 13;
        /// <summary>The most houses (fewer if the land runs out).</summary>
        public int Houses { get; set; } = 10;
        /// <summary>Lanes out of the square: 1 (the road) or 2.</summary>
        public int Lanes { get; set; } = 2;
        public int LaneMin { get; set; } = 14;
        public int LaneMax { get; set; } = 22;
        public bool Inn { get; set; } = true;
        public bool Shop { get; set; } = true;
        /// <summary>How many buildings in ten are stone (the rest timber, a few log).</summary>
        public int StoneInTen { get; set; } = 3;
        /// <summary>What the buildings' walls and roofs are made of (the file's "materials", shared by all).</summary>
        public BuildingMaterials Materials { get; set; } = new BuildingMaterials();
    }

    /// <summary>A laid-out village: its square, well, lanes and buildings, in world cells.</summary>
    public sealed class VillagePlan
    {
        public VillageSpec Spec { get; internal set; }
        public CellRect Square { get; internal set; }
        public (int x, int y) Well { get; internal set; }
        public Side Road { get; internal set; }
        public List<CellRect> Lanes { get; } = new List<CellRect>();
        public List<VillageBuilding> Buildings { get; } = new List<VillageBuilding>();

        /// <summary>Everything the village covers, a cell padding included: keep trees and rocks off it.</summary>
        public CellRect Bounds { get; internal set; }

        /// <summary>
        /// The village's cells as map characters (ChemGrid's legend): building walls, the dirt of
        /// the square and lanes ('.'), and the well's water ('w'). Later entries win.
        /// </summary>
        public IEnumerable<(int x, int y, char c)> Cells()
        {
            foreach (var (x, y) in Square.Cells()) yield return (x, y, '.');
            foreach (var lane in Lanes)
                foreach (var (x, y) in lane.Cells()) yield return (x, y, '.');
            foreach (var b in Buildings)
                foreach (var (x, y) in b.Footprint.Cells()) yield return (x, y, b.WallCell);
            yield return (Well.x, Well.y, 'w');
        }

        /// <summary>FNV-1a over the whole plan, for golden tests.</summary>
        public uint Digest()
        {
            uint h = 2166136261;
            void Mix(int v)
            {
                unchecked
                {
                    for (int i = 0; i < 4; i++) h = (h ^ (byte)(v >> (8 * i))) * 16777619;
                }
            }
            void Rect(CellRect r)
            {
                Mix(r.X);
                Mix(r.Y);
                Mix(r.W);
                Mix(r.H);
            }
            Rect(Square);
            Mix(Well.x);
            Mix(Well.y);
            Mix((int)Road);
            foreach (var l in Lanes) Rect(l);
            foreach (var b in Buildings)
            {
                Mix((int)b.Kind);
                Rect(b.Footprint);
                Mix((int)b.Door);
                Mix(b.Storeys);
                Mix((int)b.Walls);
                Mix((int)b.Roof);
                Mix((int)b.Cover);
                Mix(b.Chimney ? 1 : 0);
                Mix(b.Seed);
            }
            return h;
        }
    }

    /// <summary>A row of plots: the cell its front line starts at, the way it runs, the way its doors face, its length.</summary>
    readonly struct Front
    {
        public readonly int X, Y, Length;
        public readonly Side Along, Door;

        public Front(int x, int y, Side along, Side door, int length)
        {
            X = x;
            Y = y;
            Along = along;
            Door = door;
            Length = length;
        }
    }

    /// <summary>Lays villages out (VillagePlan) and reads their specs (world/villages.json).</summary>
    public static class VillageLayout
    {
        public const string FileName = "villages.json";

        /// <summary>
        /// Lays out a village. <paramref name="buildable"/> says whether a world cell can take a
        /// building or a street (dry, not too steep, not already something); a plot that needs a
        /// cell it refuses is skipped.
        /// </summary>
        public static VillagePlan Lay(VillageSpec spec, Func<int, int, bool> buildable) => new Layer(spec, buildable).Run();

        // One village being laid: what's taken so far, and the count of choices made (each choice
        // is a hash of the seed, its kind and this count).
        sealed class Layer
        {
            const int SaltSquare = 1, SaltRoad = 2, SaltShopSide = 3, SaltLane = 4, SaltSize = 5, SaltLook = 6, SaltGap = 7;

            readonly VillageSpec spec;
            readonly Func<int, int, bool> buildable;
            readonly HashSet<(int, int)> taken = new HashSet<(int, int)>();
            readonly VillagePlan plan;
            int counter, houses;

            public Layer(VillageSpec spec, Func<int, int, bool> buildable)
            {
                this.spec = spec;
                this.buildable = buildable;
                plan = new VillagePlan { Spec = spec };
            }

            int Pick(int salt, int lo, int hi) => lo + (int)(WorldHash.Hash4(spec.Seed, salt, counter++, 0) % (uint)(Math.Max(lo, hi) - lo + 1));

            public VillagePlan Run()
            {
                int side = Pick(SaltSquare, Math.Min(spec.SquareMin, spec.SquareMax), Math.Max(spec.SquareMin, spec.SquareMax));
                var square = new CellRect(spec.X - side / 2, spec.Y - side / 2, side, side);
                plan.Square = square;
                plan.Well = (spec.X, spec.Y);
                Take(square);

                // The inn faces the road across the square, the shop one side of it, a lane out of the side facing the shop.
                var road = (Side)Pick(SaltRoad, 0, 3);
                plan.Road = road;
                var innSide = Sides.Opposite(road);
                var shopSide = (Side)(((int)road + (Pick(SaltShopSide, 0, 1) == 0 ? 1 : 3)) % 4);
                var freeSide = Sides.Opposite(shopSide);

                // The lanes first, so no building stands across one.
                var fronts = new List<Front>();
                foreach (var s in new[] { innSide, shopSide, freeSide, road }) fronts.Add(SquareFront(square, s));
                LayLane(square, road, fronts);
                if (spec.Lanes > 1) LayLane(square, freeSide, fronts);

                if (spec.Inn) LayLandmark(SquareFront(square, innSide), BuildingKind.Inn, Pick(SaltSize, 7, 9), Pick(SaltSize, 5, 6));
                if (spec.Shop) LayLandmark(SquareFront(square, shopSide), BuildingKind.Shop, Pick(SaltSize, 5, 6), Pick(SaltSize, 4, 5));
                foreach (var f in fronts) LayHouses(f);

                var bounds = square;
                foreach (var l in plan.Lanes) bounds = Union(bounds, l);
                foreach (var b in plan.Buildings) bounds = Union(bounds, b.Footprint);
                plan.Bounds = bounds.Grown(1);
                return plan;
            }

            void LayLane(CellRect square, Side s, List<Front> fronts)
            {
                var lane = Lane(square, s, Pick(SaltLane, spec.LaneMin, spec.LaneMax));
                plan.Lanes.Add(lane);
                Take(lane);
                AddLaneFronts(lane, s, fronts);
            }

            void LayLandmark(Front f, BuildingKind kind, int width, int depth)
            {
                width = Math.Min(width, f.Length);
                var b = Plot(f, (f.Length - width) / 2, width, depth, kind);
                if (Fits(b.Footprint)) Add(b);
            }

            // Houses along a frontage, out from its start, with gaps; a plot that won't fit moves on a cell.
            void LayHouses(Front f)
            {
                int at = 0;
                while (at < f.Length && houses < spec.Houses)
                {
                    int width = Pick(SaltSize, 4, 6), depth = Pick(SaltSize, 3, 5);
                    if (at + width > f.Length) return;
                    var house = Plot(f, at, width, depth, BuildingKind.House);
                    if (!Fits(house.Footprint))
                    {
                        at++;
                        continue;
                    }
                    Add(house);
                    houses++;
                    at += width + Pick(SaltGap, 1, 3);
                }
            }

            void Add(VillageBuilding b)
            {
                b.Seed = (int)WorldHash.Hash4(spec.Seed, SaltLook, counter++, 1);
                Look(b, spec.StoneInTen);
                b.WallMaterial = spec.Materials.WallOf(b.Walls);
                b.RoofMaterial = spec.Materials.RoofOf(b.Cover);
                plan.Buildings.Add(b);
                Take(b.Footprint.Grown(1));
            }

            bool Fits(CellRect r)
            {
                foreach (var (x, y) in r.Cells())
                    if (taken.Contains((x, y)) || !buildable(x, y)) return false;
                return true;
            }

            void Take(CellRect r)
            {
                foreach (var c in r.Cells()) taken.Add(c);
            }

            // Storeys, walls and roof from a building's seed: the inn tall and timbered, stone for some
            // in ten, a few old log houses; slate or thatch on stone.
            static void Look(VillageBuilding b, int stoneInTen)
            {
                int seed = b.Seed;
                uint Roll(int salt) => WorldHash.Hash4(seed, salt, 0, 0);
                int stone = (int)(Roll(1) % 10);
                if (b.Kind == BuildingKind.Inn || (stone >= stoneInTen && stone < 9)) b.Walls = WallKind.Timber;
                else if (stone < stoneInTen) b.Walls = WallKind.Stone;
                else b.Walls = WallKind.Log;
                bool tall = b.Kind == BuildingKind.Inn || (b.Width >= 5 && Roll(2) % 3 == 0);
                b.Storeys = tall ? 2 : 1;
                b.Roof = Roll(3) % 3 == 0 ? RoofKind.Hip : RoofKind.Gable;
                int cover = (int)(Roll(4) % 3);
                if (b.Walls != WallKind.Stone) b.Cover = (RoofCover)cover;
                else b.Cover = cover == 0 ? RoofCover.Thatch : RoofCover.Slate;
                b.Chimney = b.Kind != BuildingKind.Shop || Roll(5) % 2 == 0;
            }

            // A lane two cells wide out of the middle of the square's side, length cells long.
            static CellRect Lane(CellRect sq, Side s, int length)
            {
                int midX = sq.X + sq.W / 2 - 1, midY = sq.Y + sq.H / 2 - 1;
                switch (s)
                {
                    case Side.North: return new CellRect(midX, sq.Y - length, 2, length);
                    case Side.South: return new CellRect(midX, sq.Bottom, 2, length);
                    case Side.East: return new CellRect(sq.Right, midY, length, 2);
                    default: return new CellRect(sq.X - length, midY, length, 2);
                }
            }

            // The frontage facing one side of the square, a cell of verge back from it.
            static Front SquareFront(CellRect sq, Side s)
            {
                switch (s)
                {
                    case Side.North: return new Front(sq.X, sq.Y - 2, Side.East, Side.South, sq.W);
                    case Side.South: return new Front(sq.X, sq.Bottom + 1, Side.East, Side.North, sq.W);
                    case Side.East: return new Front(sq.Right + 1, sq.Y, Side.South, Side.West, sq.H);
                    default: return new Front(sq.X - 2, sq.Y, Side.South, Side.East, sq.H);
                }
            }

            // The two frontages along a lane, a cell of verge back from it, running out from the square.
            static void AddLaneFronts(CellRect lane, Side outward, List<Front> fronts)
            {
                if (outward == Side.North || outward == Side.South)
                {
                    int y = outward == Side.North ? lane.Bottom - 1 : lane.Y;
                    fronts.Add(new Front(lane.X - 2, y, outward, Side.East, lane.H));
                    fronts.Add(new Front(lane.Right + 1, y, outward, Side.West, lane.H));
                    return;
                }
                int x = outward == Side.East ? lane.X : lane.Right - 1;
                fronts.Add(new Front(x, lane.Y - 2, outward, Side.South, lane.W));
                fronts.Add(new Front(x, lane.Bottom + 1, outward, Side.North, lane.W));
            }

            // A building on a frontage, at cells along it from its start: width along the front line,
            // depth back from it (away from the door).
            static VillageBuilding Plot(Front f, int at, int width, int depth, BuildingKind kind)
            {
                var (ax, ay) = Sides.Step(f.Along);
                var (bx, by) = Sides.Step(Sides.Opposite(f.Door));
                // Two corners: the front one at the start, the back one at the end.
                int x1 = f.X + ax * at, y1 = f.Y + ay * at;
                int x2 = x1 + ax * (width - 1) + bx * (depth - 1), y2 = y1 + ay * (width - 1) + by * (depth - 1);
                var rect = new CellRect(Math.Min(x1, x2), Math.Min(y1, y2), Math.Abs(x2 - x1) + 1, Math.Abs(y2 - y1) + 1);
                return new VillageBuilding { Kind = kind, Footprint = rect, Door = f.Door, Width = width, Depth = depth };
            }

            static CellRect Union(CellRect a, CellRect b)
            {
                int x = Math.Min(a.X, b.X), y = Math.Min(a.Y, b.Y);
                return new CellRect(x, y, Math.Max(a.Right, b.Right) - x, Math.Max(a.Bottom, b.Bottom) - y);
            }

        }

        /// <summary>Reads world/villages.json from <paramref name="worldDir"/>; no file means no villages.</summary>
        public static List<VillageSpec> Load(string worldDir)
        {
            var path = Path.Combine(worldDir, FileName);
            return File.Exists(path) ? FromJson(File.ReadAllText(path)) : new List<VillageSpec>();
        }

        /// <summary>The villages in a villages.json text. Throws <see cref="FormatException"/> for one that can't be read.</summary>
        public static List<VillageSpec> FromJson(string json)
        {
            JObject doc;
            try { doc = JObject.Parse(json); }
            catch (JsonException e) { throw new FormatException($"{FileName}: {e.Message}", e); }
            var list = new List<VillageSpec>();
            var materials = ReadMaterials(doc["materials"]);
            if (doc["villages"] is JArray villages)
                foreach (var v in villages)
                {
                    var spec = ReadSpec(v);
                    spec.Materials = materials;
                    list.Add(spec);
                }
            return list;
        }

        // "materials": { "walls": { "timber": "logwall", ... }, "roofs": { "thatch": "thatch", ... } },
        // keyed by the kinds' names in lower case; a kind left out keeps its default.
        static BuildingMaterials ReadMaterials(JToken t)
        {
            var m = new BuildingMaterials();
            if (!(t is JObject o)) return m;
            if (o["walls"] is JObject walls) ReadWalls(walls, m);
            if (o["roofs"] is JObject roofs) ReadRoofs(roofs, m);
            return m;
        }

        static void ReadWalls(JObject walls, BuildingMaterials m)
        {
            foreach (var p in walls.Properties())
            {
                if (!Enum.TryParse(p.Name, true, out WallKind k)) throw new FormatException($"{FileName}: unknown wall kind {p.Name}");
                string material = (string)p.Value;
                if (!BuildingMaterials.CanBeWall(material)) throw new FormatException($"{FileName}: wall material {material} can't stand on a map");
                m.Walls[k] = material;
            }
        }

        static void ReadRoofs(JObject roofs, BuildingMaterials m)
        {
            foreach (var p in roofs.Properties())
            {
                if (!Enum.TryParse(p.Name, true, out RoofCover c)) throw new FormatException($"{FileName}: unknown roof cover {p.Name}");
                m.Roofs[c] = (string)p.Value;
            }
        }

        static VillageSpec ReadSpec(JToken v)
        {
            if (!(v is JObject o)) throw new FormatException($"{FileName}: a village must be an object");
            if (!(o["at"] is JArray at) || at.Count != 2) throw new FormatException($"{FileName}: a village needs \"at\": [x, y]");
            var spec = new VillageSpec { Name = (string)o["name"] ?? "Village", X = (int)at[0], Y = (int)at[1], Seed = (int?)o["seed"] ?? 0 };
            if (o["square"] is JArray sq && sq.Count == 2)
            {
                spec.SquareMin = (int)sq[0];
                spec.SquareMax = (int)sq[1];
            }
            if (o["laneLength"] is JArray ll && ll.Count == 2)
            {
                spec.LaneMin = (int)ll[0];
                spec.LaneMax = (int)ll[1];
            }
            spec.Houses = (int?)o["houses"] ?? spec.Houses;
            spec.Lanes = Math.Max(1, Math.Min(2, (int?)o["lanes"] ?? spec.Lanes));
            spec.Inn = (bool?)o["inn"] ?? spec.Inn;
            spec.Shop = (bool?)o["shop"] ?? spec.Shop;
            spec.StoneInTen = (int?)o["stoneInTen"] ?? spec.StoneInTen;
            if (spec.SquareMin < 5 || spec.SquareMax < spec.SquareMin)
                throw new FormatException($"{FileName}: {spec.Name}'s square must be at least 5 cells, smallest first");
            if (spec.LaneMin < 4 || spec.LaneMax < spec.LaneMin)
                throw new FormatException($"{FileName}: {spec.Name}'s lanes must be at least 4 cells, shortest first");
            return spec;
        }
    }
}
