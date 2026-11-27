// World state and the helpers every system shares. Port of the cell parts of
// sandbox/src/world.ts. The rule interpreter is in World.React.cs, the tick in
// World.Sim.cs and spells in World.Spells.cs.
using System;
using System.Collections.Generic;
using System.Linq;

namespace Chem
{
    public sealed partial class World
    {
        public const int LogCap = 200;

        readonly List<LogEntry> log = new List<LogEntry>();
        readonly HashSet<string> logged = new HashSet<string>();

        const byte FlowMark = 1, SteamMark = 2, WindMark = 4;

        public World(Rules rules, int w, int h, uint seed = 1, string fill = "dirt", bool wrapX = false, bool wrapY = false)
        {
            if (w <= 0 || h <= 0) throw new ArgumentException("World size must be positive");
            if (wrapX && (w & Chunks.Mask) != 0) throw new ArgumentException($"A world that wraps is a whole number of chunks wide ({Chunks.Size} cells a chunk), not {w}");
            if (wrapY && !wrapX) throw new ArgumentException("A world wraps north-south only as a torus: east-west too");
            if (wrapY && (h & Chunks.Mask) != 0) throw new ArgumentException($"A torus is a whole number of chunks high ({Chunks.Size} cells a chunk), not {h}");
            ownerThread = Environment.CurrentManagedThreadId;
            Rules = rules ?? throw new ArgumentNullException(nameof(rules));
            W = w;
            H = h;
            WrapX = wrapX;
            WrapY = wrapY;
            edgeless = wrapX;
            wrapChunks = wrapX ? w >> Chunks.Shift : 0;
            wrapChunksY = wrapY ? h >> Chunks.Shift : 0;
            RngState = seed;
            Seed = seed;
            Weather = Sky.Weather(rules.Sim, seed, 0);
            MakeSystems();
            BuildChunks(fill);
        }

        public Rules Rules { get; private set; }
        public int W { get; }
        public int H { get; }
        public bool WrapX { get; }
        public bool WrapY { get; }
        readonly int wrapChunks, wrapChunksY;
        readonly bool edgeless;
        public int Tick { get; private set; }

        public IReadOnlyList<LogEntry> Log => log;
        public int LogAppended { get; private set; }

        public uint RngState { get; set; }

        public (int dir, int ticks)? Wind { get; set; }

        public sealed class Whirl
        {
            public int X, Y, Radius, Ticks, Age, Every, MaxMass, Dir, Travel;
            public string Spell, Carrying;
        }

        public sealed class Star
        {
            public string Spell;
            public int X, Y, Dir, Charge, Radius, Lands;
        }

        readonly List<Star> stars = new List<Star>();
        public IReadOnlyList<Star> Stars => stars;

        readonly List<Whirl> whirls = new List<Whirl>();
        readonly List<(Cell from, Cell to)> runs = new List<(Cell from, Cell to)>();
        public IReadOnlyList<Whirl> Whirls => whirls;

        public void ApplyRules(Rules rules)
        {
            FinishTick();
            var before = Rules;
            Rules = rules ?? throw new ArgumentNullException(nameof(rules));
            ForgetWaterSnapshots();
            Weather = SkyNow();
            rulesStamp++;
            if (Unfits(before, rules)) Refit(before);
            RaiderSystem.Sanitize();
            LayBuildings(before, rules);
            LayCells(before, rules);
        }

        int rulesStamp = 1;

        static bool Unfits(Rules before, Rules after) =>
            before.Sim.Height.Min != after.Sim.Height.Min || before.Sim.Height.Max != after.Sim.Height.Max
            || !SameLiquids(before, after)
            || before.Terrain.Keys.Any(id => !after.Terrain.ContainsKey(id))
            || before.Covers.Keys.Any(id => !after.Covers.ContainsKey(id))
            || before.Gases.Keys.Any(id => !after.Gases.ContainsKey(id));

        void Sanitize(Cell c)
        {
            var height = Rules.Sim.Height;
            c.Height = Math.Max(height.Min, Math.Min(height.Max, c.Height));
            if (!Rules.Terrain.ContainsKey(c.T)) c.T = RetiredInto(c.T) ?? "dirt";
            if (c.Cover != null && !Rules.Covers.ContainsKey(c.Cover)) c.Cover = null;
            if (c.Liquid >= Rules.Liquids.Count)
            {
                c.Water = 0;
                c.Liquid = 0;
            }
            if (c.Gas != null && !Rules.Gases.ContainsKey(c.Gas))
            {
                c.Gas = null;
                c.GasAmt = 0;
            }
        }

        public static int DirX(int d) => d switch { 1 => 1, 3 => -1, _ => 0 };
        public static int DirY(int d) => d switch { 0 => -1, 2 => 1, _ => 0 };

        public bool InBounds(int x, int y) => edgeless ? OnEdgeless(x, y) : x >= 0 && y >= 0 && x < W && y < H;

        bool OnEdgeless(int x, int y) => IsOpen ? FindChunk(Chunks.ChunkOf(x), Chunks.ChunkOf(y)) != null : WrapY || (y >= 0 && y < H);
        public int Idx(int x, int y) => IsOpen ? NoIndexAt() : FoldY(y) * W + FoldX(x);

        public long CellKey(int x, int y) => Chunks.Key(FoldX(x), FoldY(y));

        public int FoldX(int x)
        {
            if (!WrapX || (uint)x < (uint)W) return x;
            int f = x % W;
            return f < 0 ? f + W : f;
        }

        public int FoldY(int y)
        {
            if (!WrapY || (uint)y < (uint)H) return y;
            int f = y % H;
            return f < 0 ? f + H : f;
        }

        public int FoldChunkY(int cy)
        {
            if (wrapChunksY == 0 || (uint)cy < (uint)wrapChunksY) return cy;
            int f = cy % wrapChunksY;
            return f < 0 ? f + wrapChunksY : f;
        }

        public int DeltaY(int y0, int y1)
        {
            int d = y1 - y0;
            if (!WrapY) return d;
            d = FoldY(d);
            return d > H / 2 ? d - H : d;
        }

        public int DeltaChunkY(int cy0, int cy1) => ChunkDelta(cy0, cy1, wrapChunksY);

        internal (int from, int to) YSpan(int y0, int y1) => WrapY ? (y0, Math.Min(y1, y0 + H - 1)) : (Math.Max(0, y0), Math.Min(H - 1, y1));

        public int FoldChunkX(int cx)
        {
            if (wrapChunks == 0 || (uint)cx < (uint)wrapChunks) return cx;
            int f = cx % wrapChunks;
            return f < 0 ? f + wrapChunks : f;
        }

        public int DeltaX(int x0, int x1)
        {
            int d = x1 - x0;
            if (!WrapX) return d;
            d = FoldX(d);
            return d > W / 2 ? d - W : d;
        }

        public int DeltaChunkX(int cx0, int cx1) => ChunkDelta(cx0, cx1, wrapChunks);

        public static int ChunkDelta(int cx0, int cx1, int wrapChunks)
        {
            int d = cx1 - cx0;
            if (wrapChunks <= 0) return d;
            d %= wrapChunks;
            if (d < 0) d += wrapChunks;
            return d > wrapChunks / 2 ? d - wrapChunks : d;
        }

        internal (int from, int to) XSpan(int x0, int x1) => WrapX ? (x0, Math.Min(x1, x0 + W - 1)) : (Math.Max(0, x0), Math.Min(W - 1, x1));

        public int WrapChunks => wrapChunks;

        public int WrapChunksY => wrapChunksY;
        public Cell At(int x, int y) => Locate(x, y, out _, out _);

        public IEnumerable<(int x, int y, int dir)> Neighbours(int x, int y)
        {
            for (int d = 0; d < 4; d++)
            {
                int nx = x + DirX(d), ny = y + DirY(d);
                if (InBounds(nx, ny)) yield return (FoldX(nx), FoldY(ny), d);
            }
        }

        public TerrainDef TerrainOf(Cell c)
        {
            var def = c.Def;
            if (def != null && c.DefRules == rulesStamp) return def;
            c.DefRules = rulesStamp;
            return c.Def = Rules.Terrain.TryGetValue(c.T, out def) ? def : Rules.Terrain[RetiredInto(c.T) ?? "dirt"];
        }
        public bool HasTag(Cell c, string tag) => TerrainOf(c).Has(tag);
        public bool IsBlocking(Cell c) => TerrainOf(c).Blocking || IsRaisedWall(c);

        public bool LineOfSight(int x0, int y0, int x1, int y1) => SightLine(x0, y0, x1, y1, false);

        public bool FogBetween(int x0, int y0, int x1, int y1) => !SightLine(x0, y0, x1, y1, true);

        bool SightLine(int x0, int y0, int x1, int y1, bool fogOnly)
        {
            int dx = Math.Abs(x1 - x0), dy = -Math.Abs(y1 - y0), sx = x0 < x1 ? 1 : -1, sy = y0 < y1 ? 1 : -1;
            int err = dx + dy, x = x0, y = y0, thin = 0;
            while (true)
            {
                if (x == x1 && y == y1) return true;
                (x, y, err) = LineStep(x, y, err, dx, dy, sx, sy);
                if (x == x1 && y == y1) return true;
                var c = At(x, y);
                if (c == null) return true;
                if (Hides(c, fogOnly, ref thin)) return false;
            }
        }

        bool Hides(Cell c, bool fogOnly, ref int thin) =>
            Fogged(c) || (!fogOnly && BlocksSight(c)) || Veiled(c, ref thin);

        bool Veiled(Cell c, ref int thin)
        {
            if (c.Gas == null || !Rules.Gases.TryGetValue(c.Gas, out var g) || g.HidesAfter <= 0) return false;
            return ++thin >= g.HidesAfter;
        }

        public bool Fogged(Cell c) =>
            c.Gas != null && Rules.Gases.TryGetValue(c.Gas, out var g) && g.BlocksSightAt > 0 && c.GasAmt >= g.BlocksSightAt;

        static (int x, int y, int err) LineStep(int x, int y, int err, int dx, int dy, int sx, int sy)
        {
            int e2 = 2 * err;
            if (e2 >= dy) { err += dy; x += sx; }
            if (e2 <= dx) { err += dx; y += sy; }
            return (x, y, err);
        }

        bool BlocksSight(Cell c) => IsBlocking(c) && !HasTag(c, "seeThrough");
        public bool IsRaisedWall(Cell c) => HeightOf(c) >= Rules.Sim.Height.BlockAt;
        bool hasLand;
        public const int NoLand = int.MinValue;

        public void SetLand(int[] units)
        {
            if (units != null && units.Length != W * H) throw new ArgumentException($"land needs {W * H} cells, got {units.Length}");
            if (units == null) SetLand((Action<int, int[]>)null);
            else SetLand((y, row) => Array.Copy(units, y * W, row, 0, W));
        }

        public void SetLand(Action<int, int[]> row) => SpreadLand(row, (_, c) => IsWaterBody(c));

        void SpreadLand(Action<int, int[]> row, Func<int, Cell, bool> marked)
        {
            DropLandPeek();
            landFromSource = false;
            landMarks = null;
            landRowsHash = null;
            NoLandLater();
            if (row == null)
            {
                hasLand = false;
                foreach (var ch in ChunksInOrder) ch.PackedLand = null;
                return;
            }
            hasLand = true;
            var rows = new int[Chunks.Size * W];
            var one = new int[W];
            var land = new int[Chunks.Area];
            var marks = new List<int>();
            uint hash = LandRowsSeed;
            for (int cy = 0; cy <= Chunks.ChunkOf(H - 1); cy++)
            {
                int y0 = cy << Chunks.Shift;
                for (int j = 0; j < Chunks.Size && y0 + j < H; j++)
                {
                    row(y0 + j, one);
                    Array.Copy(one, 0, rows, j * W, W);
                    hash = HashLandRow(hash, one);
                }
                for (int cx = 0; cx <= Chunks.ChunkOf(W - 1); cx++)
                {
                    var ch = FindChunk(cx, cy);
                    var b = ch.Held;
                    if (b != null) b.Land ??= new int[Chunks.Area];
                    var at = b?.Land ?? land;
                    var read = new ChunkReader(this, ch, walkScratch);
                    for (int i = 0; i < Chunks.Area; i++)
                    {
                        int x = CellX(ch, i), y = CellY(ch, i);
                        var c = read.At(i);
                        int units = c == null ? NoLand : rows[(y - y0) * W + x];
                        if (units == NoLand) at[i] = NoLand;
                        else if (marked(Idx(x, y), c))
                        {
                            at[i] = NoLand;
                            if (c != null) marks.Add(Idx(x, y));
                        }
                        else at[i] = units + (landMoved?[x, y] ?? 0);
                    }
                    if (b == null) ch.PackedLand = LandCodec.Encode(land, LandBytes);
                }
            }
            marks.Sort();
            landMarks = marks;
            landRowsHash = hash.ToString("x8", System.Globalization.CultureInfo.InvariantCulture);
        }

        bool IsWaterBody(Cell c) => c == null || c.Water > 0 || TerrainOf(c).Spring > 0;

        List<int> landMarks;
        string landRowsHash;

        const uint LandRowsSeed = 0x811c9dc5;

        static uint HashLandRow(uint hash, int[] row)
        {
            foreach (int v in row)
                for (int k = 0; k < 32; k += 8) hash = (hash ^ (uint)((v >> k) & 0xff)) * 0x01000193;
            return hash;
        }

        bool landFromSource;

        public void LandFromSource()
        {
            if (!(Source is IGeneratedSource)) throw new InvalidOperationException($"The world's source ({Source.Version}) holds no land: SetLand takes it");
            FinishTick();
            DropLandPeek();
            hasLand = true;
            landFromSource = true;
            landMarks = null;
            landRowsHash = null;
            NoLandLater();
            foreach (var ch in ChunksInOrder)
            {
                ch.PackedLand = null;
                if (ch.Held is ChunkBlock b) SourceLand(ch, b.Land ??= new int[Chunks.Area]);
            }
        }

        public void SetLandByChunk(int[] units)
        {
            if (units == null) throw new ArgumentNullException(nameof(units));
            if (units.Length != W * H) throw new ArgumentException($"land needs {W * H} cells, got {units.Length}");
            SetLandByChunk(LandChunks.Of(units, W, H));
        }

        public void SetLandByChunk(LandChunks units)
        {
            if (units == null) throw new ArgumentNullException(nameof(units));
            if (units.W != W || units.H != H) throw new ArgumentException($"land needs {W} x {H}, got {units.W} x {units.H}");
            FinishTick();
            DropLandPeek();
            landFromSource = false;
            hasLand = true;
            landRowsLater = units;
            landRulesLater = Rules;
            landMarks = null;
            landRowsHash = null;
            var marks = new List<int>();
            var land = new int[Chunks.Area];
            var rows = new int[Chunks.Area];
            foreach (var ch in ChunksInOrder)
            {
                ch.PackedLand = null;
                ch.LandLater = ch.Held == null && ch.Packed != null && ch.Packed.Count == 0;
                if (ch.LandLater) continue;
                units.ChunkInto(ch.X, ch.Y, rows);
                var b = ch.Held;
                if (b != null) b.Land ??= new int[Chunks.Area];
                var at = b?.Land ?? land;
                var read = new ChunkReader(this, ch, walkScratch);
                for (int i = 0; i < Chunks.Area; i++)
                {
                    int x = CellX(ch, i), y = CellY(ch, i);
                    var c = read.At(i);
                    int u = c == null ? NoLand : rows[i];
                    if (u == NoLand) at[i] = NoLand;
                    else if (IsWaterBody(c))
                    {
                        at[i] = NoLand;
                        marks.Add(Idx(x, y));
                    }
                    else at[i] = u + (landMoved?[x, y] ?? 0);
                }
                if (b == null) ch.PackedLand = LandCodec.Encode(land, LandBytes);
            }
            marks.Sort();
            landMarksNow = marks;
        }

        public int LandUnmadeChunks => ChunksInOrder.Count(LandUnmade);

        LandChunks landRowsLater;
        int[] laterRows;
        Rules landRulesLater;
        List<int> landMarksNow;

        void NoLandLater()
        {
            landRowsLater = null;
            landRulesLater = null;
            landMarksNow = null;
            foreach (var ch in ChunksInOrder) ch.LandLater = false;
        }

        bool WaterBodyAsSet(Cell c)
        {
            if (c.Water > 0) return true;
            var terrain = landRulesLater.Terrain;
            return (terrain.TryGetValue(c.T, out var def) ? def : terrain[RetiredInto(c.T) ?? "dirt"]).Spring > 0;
        }

        void LaterLand(Chunk ch, int[] at)
        {
            var rows = laterRows ??= new int[Chunks.Area];
            landRowsLater.ChunkInto(ch.X, ch.Y, rows);
            for (int i = 0; i < Chunks.Area; i++)
            {
                int x = CellX(ch, i), y = CellY(ch, i);
                if (!InBounds(x, y))
                {
                    at[i] = NoLand;
                    continue;
                }
                int u = rows[i];
                if (u == NoLand)
                {
                    at[i] = NoLand;
                    continue;
                }
                landCell.Reset();
                Source.Make(x, y, landCell);
                at[i] = WaterBodyAsSet(landCell) ? NoLand : u + (landMoved?[x, y] ?? 0);
            }
        }

        void LandSetLater()
        {
            if (landRowsLater == null || landRowsHash != null) return;
            uint hash = LandRowsSeed;
            var band = new int[Chunks.Size * W];
            var row = new int[W];
            for (int cy = 0; cy * Chunks.Size < H; cy++)
            {
                landRowsLater.Band(cy, band);
                for (int ly = 0; ly < Chunks.Size && cy * Chunks.Size + ly < H; ly++)
                {
                    Array.Copy(band, ly * W, row, 0, W);
                    hash = HashLandRow(hash, row);
                }
            }
            var marks = new List<int>(landMarksNow);
            var rows = new int[Chunks.Area];
            foreach (var ch in ChunksInOrder)
            {
                if (!ch.LandLater) continue;
                landRowsLater.ChunkInto(ch.X, ch.Y, rows);
                for (int i = 0; i < Chunks.Area; i++)
                {
                    int x = CellX(ch, i), y = CellY(ch, i);
                    if (!InBounds(x, y) || rows[i] == NoLand) continue;
                    landCell.Reset();
                    Source.Make(x, y, landCell);
                    if (WaterBodyAsSet(landCell)) marks.Add(Idx(x, y));
                }
            }
            marks.Sort();
            landMarks = marks;
            landRowsHash = hash.ToString("x8", System.Globalization.CultureInfo.InvariantCulture);
        }

        void SourceLand(Chunk ch, int[] at)
        {
            if (!landFromSource)
            {
                LaterLand(ch, at);
                return;
            }
            var source = (IGeneratedSource)Source;
            for (int i = 0; i < Chunks.Area; i++)
            {
                int x = CellX(ch, i), y = CellY(ch, i);
                if (!InBounds(x, y))
                {
                    at[i] = NoLand;
                    continue;
                }
                landCell.Reset();
                source.Make(x, y, landCell);
                at[i] = IsWaterBody(landCell) ? NoLand : source.LandAt(x, y) + (landMoved?[x, y] ?? 0);
            }
        }

        readonly Cell landCell = new Cell();

        bool LandUnmade(Chunk ch) => (landFromSource || ch.LandLater) && ch.Held == null && ch.PackedLand == null;

        int LandOf(Chunk ch, int i)
        {
            if (!hasLand) return NoLand;
            return ch.Held is ChunkBlock b ? b.Land[i] : PackedLand(ch)[i];
        }

        int LandAtCell(int x, int y) => LandOf(FindChunk(Chunks.ChunkOf(x), Chunks.ChunkOf(y)), LocalIndex(x, y));

        public int LandUnitsAt(int x, int y) => InBounds(x, y) ? LandAtCell(x, y) : NoLand;

        void MoveLand(int x, int y, int by)
        {
            if (!hasLand) return;
            var ch = FindChunk(Chunks.ChunkOf(x), Chunks.ChunkOf(y));
            int[] at;
            if (ch.Held is ChunkBlock b) at = b.Land;
            else if (LandUnmade(ch))
            {
                if (landPeekChunk == ch) landPeekChunk = null;
                return;
            }
            else
            {
                at = PackedLand(ch);
                landPeekEdited = true;
            }
            int i = LocalIndex(x, y);
            if (at[i] != NoLand) at[i] += by;
        }

        readonly int[] landPeek = new int[Chunks.Area];
        Chunk landPeekChunk;
        bool landPeekEdited;
        byte[] landBytes;

        byte[] LandBytes => landBytes ??= new byte[LandCodec.MostBytes];

        int[] PackedLand(Chunk ch)
        {
            if (landPeekChunk == ch) return landPeek;
            DropLandPeek();
            if (LandUnmade(ch)) SourceLand(ch, landPeek);
            else LandCodec.Decode(ch.PackedLand, landPeek);
            landPeekChunk = ch;
            return landPeek;
        }

        void DropLandPeek()
        {
            if (landPeekEdited) landPeekChunk.PackedLand = LandCodec.Encode(landPeek, LandBytes);
            landPeekEdited = false;
            landPeekChunk = null;
        }

        void UnpackLand(Chunk ch, ChunkBlock b)
        {
            if (!hasLand) return;
            b.Land ??= new int[Chunks.Area];
            if (landPeekChunk == ch)
            {
                Array.Copy(landPeek, b.Land, Chunks.Area);
                landPeekEdited = false;
                landPeekChunk = null;
            }
            else if ((landFromSource || ch.LandLater) && ch.PackedLand == null) SourceLand(ch, b.Land);
            else LandCodec.Decode(ch.PackedLand, b.Land);
            ch.PackedLand = null;
        }

        void PackLand(Chunk ch, ChunkBlock b)
        {
            if (hasLand) ch.PackedLand = LandCodec.Encode(b.Land, LandBytes);
        }

        public int ElevationUnits(Cell c) => (TerrainOf(c).Elevation + HeightOf(c)) * Rules.Sim.WaterUnitsPerStep;

        internal int GroundUnits(int x, int y)
        {
            int units = ElevationUnits(At(x, y));
            int land = LandAtCell(x, y);
            if (land != NoLand) units += land;
            return units;
        }
        public bool CoverBurns(Cell c) => CoverOf(c)?.Burns == true;

        public bool CoverHasTag(Cell c, string tag) =>
            c.Cover != null && Rules.Covers.TryGetValue(c.Cover, out var cv) && cv.Has(tag);
        public CoverDef CoverOf(Cell c) =>
            c?.Cover != null && Rules.Covers.TryGetValue(c.Cover, out var cv) ? cv : null;
        public bool IsSpiky(Cell c) => CoverHasTag(c, "spiky");
        public int LiftAt(int x, int y) => InBounds(x, y) ? TerrainOf(At(x, y)).Lift : 0;

        public bool IsSlippery(Cell c) => CoverHasTag(c, "slippery") || (c.Frozen && c.Water > 0);
        public double WalkSpeed(Cell c)
        {
            var t = TerrainOf(c);
            double ground = t.WetSpeed < 1 && IsDamp(c) ? Math.Min(t.Speed, t.WetSpeed) : t.Speed;
            return Math.Min(CoverOf(c)?.Speed ?? 1, ground);
        }

        public bool IsDamp(Cell c) => (c.Water > 0 && !c.Frozen) || Raining || Tick < c.DampUntil;

        public bool IsClimbable(Cell c) => CoverHasTag(c, "climbable");
        public bool IsConductive(Cell c) => IsOpenWater(c) || TerrainOf(c).Conducts;

        public double Flammability(Cell c)
        {
            if (CoverBurns(c)) return Rules.Covers[c.Cover].Flammability;
            var t = TerrainOf(c);
            return t.Burns ? t.Flammability : 0;
        }

        internal int Roll(int[] dice)
        {
            int total = 0;
            for (int i = 0; i < dice[0]; i++) total += 1 + (int)Math.Floor(Rand() * dice[1]);
            return total;
        }

        public double Rand()
        {
            unchecked
            {
                RngState += 0x6d2b79f5u;
                uint t = RngState;
                t = (t ^ (t >> 15)) * (t | 1u);
                t ^= t + (t ^ (t >> 7)) * (t | 61u);
                return (t ^ (t >> 14)) / 4294967296.0;
            }
        }

        public void AddLog(string text, LogKind kind = LogKind.Info)
        {
            if (catchingUp) return;
            var last = log.Count > 0 ? log[log.Count - 1] : null;
            if (last != null && last.Text == text && Tick - last.Tick < 40)
            {
                last.Count++;
                last.Tick = Tick;
                return;
            }
            log.Add(new LogEntry { Tick = Tick, Text = text, Kind = kind, Count = 1 });
            LogAppended++;
            if (log.Count > LogCap) log.RemoveAt(0);
        }

        public void LogOnce(string key, string text, LogKind kind = LogKind.Info)
        {
            if (!logged.Add(key)) return;
            AddLog(text, kind);
        }

        public void AddGas(Cell c, string id, int n)
        {
            if (id == null) return;
            int max = Rules.Sim.Gas.MaxAmount;
            if (c.Gas == null || c.Gas == id)
            {
                c.Gas = id;
                c.GasAmt = Math.Min(max, c.GasAmt + n);
            }
            else if (n > c.GasAmt)
            {
                c.Gas = id;
                c.GasAmt = Math.Min(max, n);
            }
        }

        public void Paint(int x, int y, string terrainId)
        {
            var c = At(x, y);
            if (c == null || terrainId == null || !Rules.Terrain.TryGetValue(terrainId, out var def)) return;
            if (def.Has("blocking"))
            {
                if (!def.Has("vessel")) DisplaceWater(x, y);
                c.Cover = null;
            }
            c.T = terrainId;
            if (!def.Burns && c.Cover == null) c.Fire = 0;
        }

        public void AddWater(int x, int y, int units)
        {
            var c = At(x, y);
            if (c == null || IsBlocking(c) || (units > 0 && !TakesWater(c))) return;
            if (units > 0) c.Liquid = 0;
            c.Water = Math.Max(0, Math.Min(Rules.Sim.WaterMaxLevel, c.Water + units));
            if (c.Water == 0) c.Frozen = false;
        }

        public void SetCover(int x, int y, string coverId)
        {
            var c = At(x, y);
            if (c == null) return;
            if (coverId == null) { c.Cover = null; return; }
            if (IsBlocking(c) || !Rules.Covers.ContainsKey(coverId)) return;
            c.Cover = coverId;
        }
    }
}
