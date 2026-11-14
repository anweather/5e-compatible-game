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

        // Per-tick marks machines read in the TS, kept in Chunk.Marks.
        const byte FlowMark = 1, SteamMark = 2, WindMark = 4;

        /// <summary>
        /// A world of w x h cells. With <paramref name="wrapX"/> its east edge joins its west (map.json's "wrap": "ew", the
        /// seam): a cell or chunk x outside [0, W) folds into it (FoldX), so everything reaches across; north and south stay
        /// walls. A world that wraps is a whole number of chunks wide.
        /// </summary>
        public World(Rules rules, int w, int h, uint seed = 1, string fill = "dirt", bool wrapX = false)
        {
            if (w <= 0 || h <= 0) throw new ArgumentException("World size must be positive");
            if (wrapX && (w & Chunks.Mask) != 0) throw new ArgumentException($"A world that wraps is a whole number of chunks wide ({Chunks.Size} cells a chunk), not {w}");
            ownerThread = Environment.CurrentManagedThreadId;
            Rules = rules ?? throw new ArgumentNullException(nameof(rules));
            W = w;
            H = h;
            WrapX = wrapX;
            edgeless = wrapX;
            wrapChunks = wrapX ? w >> Chunks.Shift : 0;
            RngState = seed;
            Seed = seed;
            Weather = Sky.Weather(rules.Sim, seed, 0);
            BuildChunks(fill);
        }

        public Rules Rules { get; private set; }
        public int W { get; }
        public int H { get; }
        /// <summary>Whether east joins west (the seam): x is taken modulo W everywhere (FoldX).</summary>
        public bool WrapX { get; }
        // Chunks across a world that wraps, 0 for one that doesn't.
        readonly int wrapChunks;
        // A world with no east and west edges: one that wraps, or an open one (no edges at all). InBounds tests it first,
        // so a bounded world's test is the one it always was.
        readonly bool edgeless;
        public int Tick { get; private set; }

        /// <summary>The event log, oldest first, capped at LogCap entries. The last entry's Count/Tick may grow.</summary>
        public IReadOnlyList<LogEntry> Log => log;
        /// <summary>Entries ever appended (not merges). Lets a view spot new lines after old ones drop off the front.</summary>
        public int LogAppended { get; private set; }

        /// <summary>The mulberry32 state; identical to the TS world's `rng`.</summary>
        public uint RngState { get; set; }

        /// <summary>Active wind from Gust of Wind: direction (0..3) and ticks left, or null.</summary>
        public (int dir, int ticks)? Wind { get; set; }

        /// <summary>
        /// A whirlwind still whirling (spells.json pullEvery: Whirlwind): its middle, its radius in cells, the
        /// ticks it lasts and has run, how often it pulls and the heaviest creature it drags; the spell it came
        /// from, the way it was cast and how often it moves; and what it carries (a CarryDef's id, or null).
        /// </summary>
        public sealed class Whirl
        {
            public int X, Y, Radius, Ticks, Age, Every, MaxMass, Dir, Travel;
            public string Spell, Carrying;
        }

        /// <summary>A delayed cast on its way down (spells.json delay: Falling Star): what lands, where, how charged, the tick it lands.</summary>
        public sealed class Star
        {
            public string Spell;
            public int X, Y, Dir, Charge, Radius, Lands;
        }

        readonly List<Star> stars = new List<Star>();
        /// <summary>The stars on their way down, in the order they were cast.</summary>
        public IReadOnlyList<Star> Stars => stars;

        readonly List<Whirl> whirls = new List<Whirl>();
        // Lava running this tick (World.Run): from, to, moved at the end of the tick.
        readonly List<(Cell from, Cell to)> runs = new List<(Cell from, Cell to)>();
        /// <summary>The whirlwinds still whirling, in the order they were cast.</summary>
        public IReadOnlyList<Whirl> Whirls => whirls;

        /// <summary>
        /// Switches the rules on a running world (a live mod on or off; see Rules.WithMods),
        /// keeping its cells. Call it between ticks, at the same tick on every peer. Like the
        /// sandbox's applyMods and sanitizeWorld: anything the new rules no longer define is
        /// replaced the same way everywhere (terrain by dirt, covers and gases cleared, heights
        /// clamped into the new range).
        /// </summary>
        public void ApplyRules(Rules rules)
        {
            FinishTick(); // a rule switch lands between whole ticks (StepSome)
            var before = Rules;
            Rules = rules ?? throw new ArgumentNullException(nameof(rules));
            ForgetWaterSnapshots(); // terrain may mean something else now
            Weather = SkyNow();
            // Every cell's cached terrain is now stale (TerrainOf reads it again), without a pass over the cells (#181).
            // A switch that can leave cells something the new rules don't define makes the active chunks' cells fit
            // now and every other chunk's when it activates (World.Fit.cs, #190).
            rulesStamp++;
            if (Unfits(before, rules)) Refit(before);
            SanitizeRaiders();
            LayBuildings(before, rules);
            LayCells(before, rules);
        }

        // Which rules a cell's cached terrain was looked up in (Cell.DefRules).
        int rulesStamp = 1;

        // Whether a cell that fits the rules before can fail to fit the rules after (Sanitize): a terrain, cover or gas
        // they no longer define, liquids moved (a cell's kind is an index), or another height range. Most switches (a sim value, a spell, a rate)
        // are none of those.
        static bool Unfits(Rules before, Rules after) =>
            before.Sim.Height.Min != after.Sim.Height.Min || before.Sim.Height.Max != after.Sim.Height.Max
            || !SameLiquids(before, after)
            || before.Terrain.Keys.Any(id => !after.Terrain.ContainsKey(id))
            || before.Covers.Keys.Any(id => !after.Covers.ContainsKey(id))
            || before.Gases.Keys.Any(id => !after.Gases.ContainsKey(id));

        // A cell made to fit the rules in force (sanitizeCell in the TS).
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

        // ---------- directions ----------

        /// <summary>dir: 0=N(y-1) 1=E 2=S(y+1) 3=W, same as the TS DX/DY.</summary>
        public static int DirX(int d) => d switch { 1 => 1, 3 => -1, _ => 0 };
        public static int DirY(int d) => d switch { 0 => -1, 2 => 1, _ => 0 };

        // ---------- cells ----------

        /// <summary>
        /// Whether (x, y) is on the world: any x on one that wraps (it folds), north and south walls either way.
        /// On an open world (World.OpenWorld), whether it's in a filled chunk.
        /// </summary>
        public bool InBounds(int x, int y) => edgeless ? OnEdgeless(x, y) : x >= 0 && y >= 0 && x < W && y < H;

        bool OnEdgeless(int x, int y) => IsOpen ? FindChunk(Chunks.ChunkOf(x), Chunks.ChunkOf(y)) != null : y >= 0 && y < H;
        /// <summary>The world index of (x, y), its x folded across the seam first. An open world has none (it has no width): CellKey names a cell there.</summary>
        public int Idx(int x, int y) => IsOpen ? NoIndexAt() : y * W + FoldX(x);

        /// <summary>A cell's own number on any world, open or bounded, for a set of cells: its x folded across the seam first.</summary>
        public long CellKey(int x, int y) => Chunks.Key(FoldX(x), y);

        // ---------- the seam ----------
        // Every reach across the seam goes through these: a cell or chunk x outside [0, W) folds into it on a world that
        // wraps, and is left as it is on one that doesn't (so a world without wrap reads and hashes as it always has).

        /// <summary>A cell x folded into [0, W) on a world that wraps; unchanged on one that doesn't.</summary>
        public int FoldX(int x)
        {
            if (!WrapX || (uint)x < (uint)W) return x;
            int f = x % W;
            return f < 0 ? f + W : f;
        }

        /// <summary>A chunk x folded into [0, W / Chunks.Size) on a world that wraps; unchanged on one that doesn't.</summary>
        public int FoldChunkX(int cx)
        {
            if (wrapChunks == 0 || (uint)cx < (uint)wrapChunks) return cx;
            int f = cx % wrapChunks;
            return f < 0 ? f + wrapChunks : f;
        }

        /// <summary>
        /// The cells from x0 east to x1: x1 - x0, or on a world that wraps the short way round the seam (in -W/2 .. W/2,
        /// east positive; exactly half way round counts east).
        /// </summary>
        public int DeltaX(int x0, int x1)
        {
            int d = x1 - x0;
            if (!WrapX) return d;
            d = FoldX(d);
            return d > W / 2 ? d - W : d;
        }

        /// <summary>The chunks from cx0 east to cx1, the short way round on a world that wraps (DeltaX in chunks).</summary>
        public int DeltaChunkX(int cx0, int cx1) => ChunkDelta(cx0, cx1, wrapChunks);

        /// <summary>Chunks from cx0 east to cx1 on a world <paramref name="wrapChunks"/> chunks round (0: no wrap), the short way.</summary>
        public static int ChunkDelta(int cx0, int cx1, int wrapChunks)
        {
            int d = cx1 - cx0;
            if (wrapChunks <= 0) return d;
            d %= wrapChunks;
            if (d < 0) d += wrapChunks;
            return d > wrapChunks / 2 ? d - wrapChunks : d;
        }

        /// <summary>
        /// The columns [x0, x1] of a box on the world, to walk: clipped to [0, W) on a world that doesn't wrap; on one that
        /// does left as they are (whoever reads a cell folds it) but at most W of them, so no cell is walked twice.
        /// </summary>
        internal (int from, int to) XSpan(int x0, int x1) => WrapX ? (x0, Math.Min(x1, x0 + W - 1)) : (Math.Max(0, x0), Math.Min(W - 1, x1));

        /// <summary>The chunks round a world that wraps (W / Chunks.Size), 0 for one that doesn't: for ChunkStreaming's distances.</summary>
        public int WrapChunks => wrapChunks;
        public Cell At(int x, int y) => Locate(x, y, out _, out _);

        /// <summary>Neighbours in fixed N, E, S, W order, so the simulation stays deterministic.</summary>
        public IEnumerable<(int x, int y, int dir)> Neighbours(int x, int y)
        {
            for (int d = 0; d < 4; d++)
            {
                int nx = x + DirX(d), ny = y + DirY(d);
                if (InBounds(nx, ny)) yield return (FoldX(nx), ny, d);
            }
        }

        public TerrainDef TerrainOf(Cell c)
        {
            // Every tick asks each wet or burning cell about its terrain several times; looking the
            // id up only when it changes keeps that cheap (Cell.T drops the cache when it's replaced).
            // A cell of a chunk not yet made to fit the rules (World.Fit.cs) reads as fitting would make it.
            var def = c.Def;
            if (def != null && c.DefRules == rulesStamp) return def;
            c.DefRules = rulesStamp;
            return c.Def = Rules.Terrain.TryGetValue(c.T, out def) ? def : Rules.Terrain[RetiredInto(c.T) ?? "dirt"];
        }
        public bool HasTag(Cell c, string tag) => TerrainOf(c).Has(tag);
        public bool IsBlocking(Cell c) => TerrainOf(c).Blocking || IsRaisedWall(c);

        /// <summary>
        /// Whether (x1, y1) can be seen from (x0, y0): no blocking square between them on a
        /// Bresenham line, except see-through ones (gate bars), and no thick fog (a gas's
        /// blocksSightAt) nor a long reach of thin fog (its hidesAfter squares: fog weather).
        /// The ends don't count. Mirrors lineOfSight in sandbox/src/world.ts
        /// (Misty Step's "a square you can see").
        /// </summary>
        public bool LineOfSight(int x0, int y0, int x1, int y1) => SightLine(x0, y0, x1, y1, false);

        /// <summary>
        /// Whether thick fog (Fog Cloud) hides (x1, y1) from (x0, y0): LineOfSight's line, counting
        /// only the fog. For a view that checks walls in 3D itself (Unity's Misty Step).
        /// </summary>
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

        // Whether a square on a sight line stops it: thick fog, a long reach of thin fog, or (unless fogOnly) a wall.
        bool Hides(Cell c, bool fogOnly, ref int thin) =>
            Fogged(c) || (!fogOnly && BlocksSight(c)) || Veiled(c, ref thin);

        // Counts a square of thin fog on the line; true once the line has crossed its gas's hidesAfter of them.
        bool Veiled(Cell c, ref int thin)
        {
            if (c.Gas == null || !Rules.Gases.TryGetValue(c.Gas, out var g) || g.HidesAfter <= 0) return false;
            return ++thin >= g.HidesAfter;
        }

        /// <summary>Whether a square's gas is thick enough to hide what's behind it (gases.json blocksSightAt).</summary>
        public bool Fogged(Cell c) =>
            c.Gas != null && Rules.Gases.TryGetValue(c.Gas, out var g) && g.BlocksSightAt > 0 && c.GasAmt >= g.BlocksSightAt;

        // One step of Bresenham's line (lineOfSight's loop body).
        static (int x, int y, int err) LineStep(int x, int y, int err, int dx, int dy, int sx, int sy)
        {
            int e2 = 2 * err;
            if (e2 >= dy) { err += dy; x += sx; }
            if (e2 <= dx) { err += dx; y += sy; }
            return (x, y, err);
        }

        bool BlocksSight(Cell c) => IsBlocking(c) && !HasTag(c, "seeThrough");
        /// <summary>Raised height.blockAt steps or more: a wall of earth, whatever the terrain.</summary>
        public bool IsRaisedWall(Cell c) => HeightOf(c) >= Rules.Sim.Height.BlockAt;
        // The land under each cell in water units (the valley's baked ground: hills the cells don't hold), or NoLand for a
        // cell of a water body; false for flat land. Only the water reads it. Held a chunk at a time (the sparse World, step
        // 3): an unpacked chunk's in its block (ChunkBlock.Land), a packed chunk's as LandCodec's bytes (Chunk.PackedLand).
        bool hasLand;
        public const int NoLand = int.MinValue;

        /// <summary>
        /// The land's height under each cell (Idx order), in water units, so water on a hillside runs
        /// down it (playtest #31): world/land.json (LandFile), the same integers on every peer. A cell
        /// with water or a spring now (a lake, the river, the sea) is marked NoLand, as is any NoLand
        /// given. Copied; null makes the land flat again.
        /// </summary>
        public void SetLand(int[] units)
        {
            if (units != null && units.Length != W * H) throw new ArgumentException($"land needs {W * H} cells, got {units.Length}");
            if (units == null) SetLand((Action<int, int[]>)null);
            else SetLand((y, row) => Array.Copy(units, y * W, row, 0, W));
        }

        /// <summary>
        /// SetLand a row at a time, north to south, so a big map's land is never held whole: <paramref name="row"/> fills
        /// row y's W cells, once a row. Null makes the land flat again.
        /// </summary>
        public void SetLand(Action<int, int[]> row) => SpreadLand(row, (_, c) => IsWaterBody(c));

        // SetLand's work: the rows, less the cells marked NoLand (a water body's when it's set; the saved marks on a load),
        // plus what's been stamped. What was marked and the rows' hash are kept for a save (World.Save.cs, SaveLandSet).
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
                        // A water body's cell is NoLand; on the rest goes what's been stamped.
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

        // A cell SetLand marks NoLand: none, or one with water or a spring now.
        bool IsWaterBody(Cell c) => c == null || c.Water > 0 || TerrainOf(c).Spring > 0;

        // The cells SetLand marked NoLand for their water (Idx order), and the hash of the rows it was given (FNV-1a over
        // the integers): what a save keeps of land it was given, so a load given the same rows makes the same land. Null
        // when the land is flat or the source's (LandFromSource).
        List<int> landMarks;
        string landRowsHash;

        const uint LandRowsSeed = 0x811c9dc5;

        static uint HashLandRow(uint hash, int[] row)
        {
            foreach (int v in row)
                for (int k = 0; k < 32; k += 8) hash = (hash ^ (uint)((v >> k) & 0xff)) * 0x01000193;
            return hash;
        }

        // Whether the land is the source's (LandFromSource): then an untouched chunk's (no PackedLand while packed) is
        // made from the source as it's read, with what the stamps and the map moved since.
        bool landFromSource;

        /// <summary>
        /// SetLand from the world's own source (a generated map's, IGeneratedSource.LandAt), without reading the whole map:
        /// a chunk's land is made as it's first read, NoLand on a water body as the map makes it, with what the stamps and
        /// the map have moved. So it's the same land however late it's read, and on a world loaded from a save. Between
        /// ticks, once the world is made from the source (or loaded).
        /// </summary>
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

        /// <summary>
        /// SetLand, a chunk at a time (island-startup): the same land, NoLand marks and save as SetLand(units), but a chunk
        /// that is still its source's (packed, with no changes) gets its land when it's first read, not now: its cells are
        /// the source's then as now, so what SetLand would have marked is what the source makes. The rest get theirs now.
        /// The rows are kept, not copied: the caller keeps them as they are. Between ticks.
        /// </summary>
        public void SetLandByChunk(int[] units)
        {
            if (units == null) throw new ArgumentNullException(nameof(units));
            if (units.Length != W * H) throw new ArgumentException($"land needs {W * H} cells, got {units.Length}");
            SetLandByChunk(LandChunks.Of(units, W, H));
        }

        /// <summary>SetLandByChunk from land held a chunk at a time (LandChunks.Read): no whole map's ints, here or in the caller.</summary>
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

        /// <summary>The chunks whose land isn't made yet (SetLandByChunk's, or the source's): for the start's numbers and tests.</summary>
        public int LandUnmadeChunks => ChunksInOrder.Count(LandUnmade);

        // SetLandByChunk's rows and the rules the land was set under (a spring is a water body by those), and the marks it
        // made for the chunks that got their land at once; the save's landSet is worked out from them when it's wanted.
        LandChunks landRowsLater;
        // LaterLand's buffer for a chunk's rows (it runs inside other walks, a chunk unpacking for a read).
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

        // A water body as SetLandByChunk would have marked it: by the rules it was set under.
        bool WaterBodyAsSet(Cell c)
        {
            if (c.Water > 0) return true;
            var terrain = landRulesLater.Terrain;
            return (terrain.TryGetValue(c.T, out var def) ? def : terrain[RetiredInto(c.T) ?? "dirt"]).Spring > 0;
        }

        // A chunk SetLandByChunk left for later: its land from the rows and its cells as the source makes them.
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

        // For a save of land set a chunk at a time: the hash of the rows and every mark SetLand would have made, the left
        // chunks' from their source, as SetLand(units) keeps them. Once; the rows don't change.
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

        // A chunk's land as the source makes it, with what's moved since.
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

        // The cell SourceLand makes the source's cells into: its own, as it runs inside other walks (a chunk unpacking for a read).
        readonly Cell landCell = new Cell();

        // Whether a chunk's land is still the source's, made as it's read: it's packed and has never been unpacked since LandFromSource.
        bool LandUnmade(Chunk ch) => (landFromSource || ch.LandLater) && ch.Held == null && ch.PackedLand == null;

        int LandOf(Chunk ch, int i)
        {
            if (!hasLand) return NoLand;
            return ch.Held is ChunkBlock b ? b.Land[i] : PackedLand(ch)[i];
        }

        // The land at a cell on the world, NoLand for none.
        int LandAtCell(int x, int y) => LandOf(FindChunk(Chunks.ChunkOf(x), Chunks.ChunkOf(y)), LocalIndex(x, y));

        /// <summary>The land under a cell as SetLand and the stamps left it, in water units; NoLand on water, for flat land or off the world. Reads a packed chunk where it lies.</summary>
        public int LandUnitsAt(int x, int y) => InBounds(x, y) ? LandAtCell(x, y) : NoLand;

        // Moves the land at a cell on the world (a stamp, a road's cut), but not on water.
        void MoveLand(int x, int y, int by)
        {
            if (!hasLand) return;
            var ch = FindChunk(Chunks.ChunkOf(x), Chunks.ChunkOf(y));
            int[] at;
            if (ch.Held is ChunkBlock b) at = b.Land;
            else if (LandUnmade(ch))
            {
                // Made from the source with what landMoved holds, this move among it: a peek of it made before is stale.
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

        // One packed chunk's land read out of its bytes, for reading and moving a packed chunk's land without unpacking it.
        // Moved, it goes back into the chunk's bytes when another chunk's is read out, or as the chunk unpacks.
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

        // The peek's chunk given its land back, if it was moved, and the peek let go.
        void DropLandPeek()
        {
            if (landPeekEdited) landPeekChunk.PackedLand = LandCodec.Encode(landPeek, LandBytes);
            landPeekEdited = false;
            landPeekChunk = null;
        }

        // As a chunk unpacks: its land into the block.
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

        // As a chunk packs: its land out of the block.
        void PackLand(Chunk ch, ChunkBlock b)
        {
            if (hasLand) ch.PackedLand = LandCodec.Encode(b.Land, LandBytes);
        }

        /// <summary>Terrain elevation plus raised or dug height, in water units, so it compares directly with Cell.Water.</summary>
        public int ElevationUnits(Cell c) => (TerrainOf(c).Elevation + HeightOf(c)) * Rules.Sim.WaterUnitsPerStep;
        /// <summary>Whether the cell's cover burns (oil, grease, thorns).</summary>
        public bool CoverBurns(Cell c) => CoverOf(c)?.Burns == true;

        public bool CoverHasTag(Cell c, string tag) =>
            c.Cover != null && Rules.Covers.TryGetValue(c.Cover, out var cv) && cv.Has(tag);
        /// <summary>The cell's cover's definition, or null without one (or one the rules no longer know).</summary>
        public CoverDef CoverOf(Cell c) =>
            c?.Cover != null && Rules.Covers.TryGetValue(c.Cover, out var cv) ? cv : null;
        /// <summary>
        /// Whether a cell's cover hinders walking (thorns: spiky, slowing or hurting with each
        /// square walked; CoverDef.Speed and Damage say how much). A view's walkers read it.
        /// </summary>
        public bool IsSpiky(Cell c) => CoverHasTag(c, "spiky");
        /// <summary>
        /// Steps the updraft at (x, y) lifts a prop or a wizard standing over it (materials.json lift.by; liftAt in the
        /// TS), 0 for none or off the grid. The view does the lifting; the core only says where the air rises.
        /// </summary>
        public int LiftAt(int x, int y) => InBounds(x, y) ? TerrainOf(At(x, y)).Lift : 0;

        public bool IsSlippery(Cell c) => CoverHasTag(c, "slippery") || (c.Frozen && c.Water > 0);
        /// <summary>
        /// The share of its speed a walker keeps on a cell: the slower of its cover's and its ground's (snow 0.85, a
        /// snow bank 0.7, thorns and mud 0.5; 1 for most). Raiders are snagged by it (Thorns) and the wizard slowed.
        /// </summary>
        public double WalkSpeed(Cell c) => Math.Min(CoverOf(c)?.Speed ?? 1, TerrainOf(c).Speed);

        /// <summary>Whether walkers can climb or cross a cell on its cover (vines: up a wall, over a pit or a stream), for the view.</summary>
        public bool IsClimbable(Cell c) => CoverHasTag(c, "climbable");
        public bool IsConductive(Cell c) => IsOpenWater(c) || TerrainOf(c).Conducts;

        public double Flammability(Cell c)
        {
            if (CoverBurns(c)) return Rules.Covers[c.Cover].Flammability;
            var t = TerrainOf(c);
            return t.Burns ? t.Flammability : 0;
        }

        // ---------- randomness ----------

        /// <summary>mulberry32, bit-for-bit the same as `rand` in world.ts.</summary>
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

        // ---------- log ----------

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

        /// <summary>Log once per tick per key, so spreading fire doesn't flood the log.</summary>
        public void LogOnce(string key, string text, LogKind kind = LogKind.Info)
        {
            if (!logged.Add(key)) return;
            AddLog(text, kind);
        }

        // ---------- gas ----------

        public void AddGas(Cell c, string id, int n)
        {
            if (id == null) return; // a product the rules leave out (sim.json water.boils, fire.smoke)
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

        // ---------- map building (the sandbox's paint brushes, minus entities) ----------

        /// <summary>Sets terrain like the sandbox's paint brush. Unknown ids are ignored.</summary>
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

        /// <summary>Pours water units onto a square (not onto blocking terrain, nor another liquid), capped at maxLevel.</summary>
        public void AddWater(int x, int y, int units)
        {
            var c = At(x, y);
            if (c == null || IsBlocking(c) || (units > 0 && !TakesWater(c))) return;
            if (units > 0) c.Liquid = 0;
            c.Water = Math.Max(0, Math.Min(Rules.Sim.WaterMaxLevel, c.Water + units));
            if (c.Water == 0) c.Frozen = false;
        }

        /// <summary>Sets (or with null, clears) a square's cover. Blocking squares take no cover.</summary>
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
