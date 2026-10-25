// World state and the helpers every system shares. Port of the cell parts of
// sandbox/src/world.ts. The rule interpreter is in World.React.cs, the tick in
// World.Sim.cs and spells in World.Spells.cs.
using System;
using System.Collections.Generic;
using System.Globalization;

namespace Chem
{
    public sealed partial class World
    {
        public const int LogCap = 200;

        readonly List<LogEntry> log = new List<LogEntry>();
        readonly HashSet<string> logged = new HashSet<string>();
        // Per-tick marks machines read in the TS; cleared at the end of each tick.
        readonly byte[] flowMarks, steamMarks, windMarks;

        public World(Rules rules, int w, int h, uint seed = 1, string fill = "dirt")
        {
            if (w <= 0 || h <= 0) throw new ArgumentException("World size must be positive");
            Rules = rules ?? throw new ArgumentNullException(nameof(rules));
            W = w;
            H = h;
            RngState = seed;
            Cells = new Cell[w * h];
            for (int i = 0; i < Cells.Length; i++) Cells[i] = new Cell(fill);
            flowMarks = new byte[w * h];
            steamMarks = new byte[w * h];
            windMarks = new byte[w * h];
        }

        public Rules Rules { get; }
        public int W { get; }
        public int H { get; }
        public int Tick { get; private set; }
        public Cell[] Cells { get; }

        /// <summary>The event log, oldest first, capped at LogCap entries. The last entry's Count/Tick may grow.</summary>
        public IReadOnlyList<LogEntry> Log => log;
        /// <summary>Entries ever appended (not merges). Lets a view spot new lines after old ones drop off the front.</summary>
        public int LogAppended { get; private set; }

        /// <summary>The mulberry32 state; identical to the TS world's `rng`.</summary>
        public uint RngState { get; set; }

        /// <summary>Active wind from Gust of Wind: direction (0..3) and ticks left, or null.</summary>
        public (int dir, int ticks)? Wind { get; set; }

        // ---------- directions ----------

        /// <summary>dir: 0=N(y-1) 1=E 2=S(y+1) 3=W, same as the TS DX/DY.</summary>
        public static int DirX(int d) => d switch { 1 => 1, 3 => -1, _ => 0 };
        public static int DirY(int d) => d switch { 0 => -1, 2 => 1, _ => 0 };

        // ---------- cells ----------

        public bool InBounds(int x, int y) => x >= 0 && y >= 0 && x < W && y < H;
        public int Idx(int x, int y) => y * W + x;
        public Cell At(int x, int y) => InBounds(x, y) ? Cells[y * W + x] : null;

        /// <summary>Neighbours in fixed N, E, S, W order, so the simulation stays deterministic.</summary>
        public IEnumerable<(int x, int y, int dir)> Neighbours(int x, int y)
        {
            for (int d = 0; d < 4; d++)
            {
                int nx = x + DirX(d), ny = y + DirY(d);
                if (InBounds(nx, ny)) yield return (nx, ny, d);
            }
        }

        public TerrainDef TerrainOf(Cell c) => Rules.Terrain[c.T];
        public bool HasTag(Cell c, string tag) => TerrainOf(c).Has(tag);
        public bool IsBlocking(Cell c) => HasTag(c, "blocking");
        /// <summary>In water units, so it compares directly with Cell.Water.</summary>
        public int ElevationUnits(Cell c) => TerrainOf(c).Elevation * Rules.Sim.WaterUnitsPerStep;
        public bool CoverHasTag(Cell c, string tag) =>
            c.Cover != null && Rules.Covers.TryGetValue(c.Cover, out var cv) && cv.Has(tag);
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

        /// <summary>Stable fingerprint of the cells (FNV-1a), matching the TS golden script in the tests.</summary>
        public string CellHash()
        {
            var sb = new System.Text.StringBuilder();
            foreach (var c in Cells)
                sb.Append(c.T).Append(',').Append(c.Water).Append(',').Append(c.Frozen ? 1 : 0).Append(',')
                  .Append(c.Cover ?? "").Append(',').Append(c.Fire).Append(',').Append(c.Fuel).Append(',')
                  .Append(c.Gas ?? "").Append(',').Append(c.GasAmt).Append(',').Append(c.Charge).Append(';');
            unchecked
            {
                uint h = 0x811c9dc5;
                var s = sb.ToString();
                for (int i = 0; i < s.Length; i++) h = (h ^ s[i]) * 0x01000193u;
                return h.ToString("x", CultureInfo.InvariantCulture);
            }
        }
    }
}
