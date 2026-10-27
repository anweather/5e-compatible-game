// World state and the helpers every system shares. Port of the cell parts of
// sandbox/src/world.ts. The rule interpreter is in World.React.cs, the tick in
// World.Sim.cs and spells in World.Spells.cs.
using System;
using System.Collections.Generic;

namespace Chem
{
    public sealed partial class World
    {
        public const int LogCap = 200;

        readonly List<LogEntry> log = new List<LogEntry>();
        readonly HashSet<string> logged = new HashSet<string>();

        // Per-tick marks machines read in the TS, kept in Chunk.Marks.
        const byte FlowMark = 1, SteamMark = 2, WindMark = 4;

        public World(Rules rules, int w, int h, uint seed = 1, string fill = "dirt")
        {
            if (w <= 0 || h <= 0) throw new ArgumentException("World size must be positive");
            Rules = rules ?? throw new ArgumentNullException(nameof(rules));
            W = w;
            H = h;
            RngState = seed;
            Seed = seed;
            Weather = Sky.Weather(rules.Sim, seed, 0);
            Cells = new Cell[w * h];
            BuildChunks(fill);
        }

        public Rules Rules { get; private set; }
        public int W { get; }
        public int H { get; }
        public int Tick { get; private set; }
        /// <summary>
        /// Every cell of the world's extent, row-major (index Idx(x, y)): a view for callers that
        /// want them all. The cells themselves are stored in chunks (World.Chunks.cs); this array
        /// holds the same objects.
        /// </summary>
        public Cell[] Cells { get; }

        /// <summary>The event log, oldest first, capped at LogCap entries. The last entry's Count/Tick may grow.</summary>
        public IReadOnlyList<LogEntry> Log => log;
        /// <summary>Entries ever appended (not merges). Lets a view spot new lines after old ones drop off the front.</summary>
        public int LogAppended { get; private set; }

        /// <summary>The mulberry32 state; identical to the TS world's `rng`.</summary>
        public uint RngState { get; set; }

        /// <summary>Active wind from Gust of Wind: direction (0..3) and ticks left, or null.</summary>
        public (int dir, int ticks)? Wind { get; set; }

        /// <summary>
        /// Switches the rules on a running world (a live mod on or off; see Rules.WithMods),
        /// keeping its cells. Call it between ticks, at the same tick on every peer. Like the
        /// sandbox's applyMods and sanitizeWorld: anything the new rules no longer define is
        /// replaced the same way everywhere (terrain by dirt, covers and gases cleared, heights
        /// clamped into the new range).
        /// </summary>
        public void ApplyRules(Rules rules)
        {
            Rules = rules ?? throw new ArgumentNullException(nameof(rules));
            var height = rules.Sim.Height;
            Weather = SkyNow();
            foreach (var c in Cells)
            {
                c.ForgetDef();
                c.Height = Math.Max(height.Min, Math.Min(height.Max, c.Height));
                if (!rules.Terrain.ContainsKey(c.T)) c.T = "dirt";
                if (c.Cover != null && !rules.Covers.ContainsKey(c.Cover)) c.Cover = null;
                if (c.Gas != null && !rules.Gases.ContainsKey(c.Gas))
                {
                    c.Gas = null;
                    c.GasAmt = 0;
                }
            }
        }

        // ---------- directions ----------

        /// <summary>dir: 0=N(y-1) 1=E 2=S(y+1) 3=W, same as the TS DX/DY.</summary>
        public static int DirX(int d) => d switch { 1 => 1, 3 => -1, _ => 0 };
        public static int DirY(int d) => d switch { 0 => -1, 2 => 1, _ => 0 };

        // ---------- cells ----------

        public bool InBounds(int x, int y) => x >= 0 && y >= 0 && x < W && y < H;
        public int Idx(int x, int y) => y * W + x;
        public Cell At(int x, int y) => Locate(x, y, out _, out _);

        /// <summary>Neighbours in fixed N, E, S, W order, so the simulation stays deterministic.</summary>
        public IEnumerable<(int x, int y, int dir)> Neighbours(int x, int y)
        {
            for (int d = 0; d < 4; d++)
            {
                int nx = x + DirX(d), ny = y + DirY(d);
                if (InBounds(nx, ny)) yield return (nx, ny, d);
            }
        }

        public TerrainDef TerrainOf(Cell c)
        {
            // Every tick asks each wet or burning cell about its terrain several times; looking the
            // id up only when it changes keeps that cheap. (Reference check: T is replaced, not edited.)
            if (!ReferenceEquals(c.DefFor, c.T))
            {
                c.Def = Rules.Terrain[c.T];
                c.DefFor = c.T;
            }
            return c.Def;
        }
        public bool HasTag(Cell c, string tag) => TerrainOf(c).Has(tag);
        public bool IsBlocking(Cell c) => HasTag(c, "blocking") || IsRaisedWall(c);

        /// <summary>
        /// Whether (x1, y1) can be seen from (x0, y0): no blocking square between them on a
        /// Bresenham line, except see-through ones (gate bars), and no thick fog (a gas's
        /// blocksSightAt). The ends don't count. Mirrors lineOfSight in sandbox/src/world.ts
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
            int err = dx + dy, x = x0, y = y0;
            while (true)
            {
                if (x == x1 && y == y1) return true;
                (x, y, err) = LineStep(x, y, err, dx, dy, sx, sy);
                if (x == x1 && y == y1) return true;
                var c = At(x, y);
                if (c == null) return true;
                if (Fogged(c) || (!fogOnly && BlocksSight(c))) return false;
            }
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
        public bool IsRaisedWall(Cell c) => c.Height >= Rules.Sim.Height.BlockAt;
        /// <summary>Terrain elevation plus raised or dug height, in water units, so it compares directly with Cell.Water.</summary>
        public int ElevationUnits(Cell c) => (TerrainOf(c).Elevation + c.Height) * Rules.Sim.WaterUnitsPerStep;
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
        public bool IsSlippery(Cell c) => CoverHasTag(c, "slippery") || (c.Frozen && c.Water > 0);
        public bool IsConductive(Cell c) => (c.Water > 0 && !c.Frozen) || HasTag(c, "conductive");

        public double Flammability(Cell c)
        {
            if (CoverHasTag(c, "flammable")) return Rules.Covers[c.Cover].Flammability;
            var t = TerrainOf(c);
            return t.Has("flammable") ? t.Flammability : 0;
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
            if (!def.Has("flammable") && c.Cover == null) c.Fire = 0;
        }

        /// <summary>Pours water units onto a square (not onto blocking terrain), capped at maxLevel.</summary>
        public void AddWater(int x, int y, int units)
        {
            var c = At(x, y);
            if (c == null || IsBlocking(c)) return;
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
