// Saving a world to disk (docs/WORLD_STREAMING.md, "Part 2, third slice"):
// only what play changed. Every chunk starts as its source made it (the baked
// valley today, ChunkGen from the seed later); a save keeps the world's state
// between ticks and, per chunk, the cells that differ from the source and any
// streaming state that isn't the default. An untouched chunk isn't saved: it
// is made again from the source on load. Integers and ids only, in a fixed
// order, so one world always saves to the same text. C# only (solo); the
// sandbox keeps its own snapshots (serializeWorld).
using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Chem
{
    /// <summary>
    /// Where a world's cells come from before anyone plays: a pure function from a cell's world
    /// position to the cell as made. A save stores only what differs from it.
    /// </summary>
    public interface IChunkSource
    {
        /// <summary>Names this way of making the world; a save is only loaded over the same.</summary>
        string Version { get; }

        /// <summary>Sets <paramref name="cell"/> (freshly reset to dirt) to the cell at (x, y) as made.</summary>
        void Make(int x, int y, Cell cell);
    }

    /// <summary>A save that doesn't fit: another format, source, rules or size.</summary>
    public sealed class SaveException : Exception
    {
        public SaveException(string message) : base(message) { }
    }

    public sealed partial class World
    {
        /// <summary>The save format; a save of another is refused.</summary>
        public const int SaveFormat = 1;

        /// <summary>Makes every cell as the source says (as a fresh world starts, or a load before its changes).</summary>
        public void MakeFrom(IChunkSource source)
        {
            if (source == null) throw new ArgumentNullException(nameof(source));
            FinishTick();
            for (int y = 0; y < H; y++)
                for (int x = 0; x < W; x++)
                {
                    var c = At(x, y);
                    c.Reset();
                    source.Make(x, y, c);
                    c.ForgetDef();
                }
            ForgetWaterSnapshots();
        }

        /// <summary>The chunks with cells that differ from the source, by row then column.</summary>
        public IReadOnlyList<(int cx, int cy)> ChangedChunks(IChunkSource source)
        {
            if (source == null) throw new ArgumentNullException(nameof(source));
            var made = new Cell();
            return SortedChunks(false).Where(ch => ChangedCells(ch, source, made).Any()).Select(ch => (ch.X, ch.Y)).ToList();
        }

        /// <summary>The world as play left it, against the source it was made from (finishing the tick in progress first).</summary>
        public string Save(IChunkSource source)
        {
            if (source == null) throw new ArgumentNullException(nameof(source));
            FinishTick();
            var save = new JObject
            {
                ["format"] = SaveFormat,
                ["source"] = source.Version,
                ["rules"] = Rules.Fingerprint,
                ["w"] = W,
                ["h"] = H,
                ["seed"] = Seed,
                ["tick"] = Tick,
                ["rng"] = RngState,
                ["clockAhead"] = ClockAhead,
                ["haste"] = Haste.HasValue ? new JArray(Haste.Value.rate, Haste.Value.ticks) : null,
                ["weather"] = WeatherSet.HasValue ? new JArray((int)WeatherSet.Value.kind, WeatherSet.Value.from, WeatherSet.Value.until) : null,
                ["wind"] = Wind.HasValue ? new JArray(Wind.Value.dir, Wind.Value.ticks) : null,
                ["meteors"] = new JArray(falling.Select(m => new JArray(m.x, m.y, m.lands))),
                ["nextEntityId"] = NextEntityId,
                ["defense"] = SaveDefense(),
                ["raiders"] = new JArray(raiders.Select(SaveRaider)),
            };
            var made = new Cell();
            var saved = new JArray();
            foreach (var ch in SortedChunks(false))
            {
                var c = SaveChunk(ch, source, made);
                if (c.Count > 2) saved.Add(c); // more than its x and y
            }
            save["chunks"] = saved;
            return save.ToString(Formatting.None);
        }

        /// <summary>
        /// A world from a save: made from the source, then the save's changes and state laid over.
        /// Refused (SaveException) unless the save's format, source and rules are these.
        /// </summary>
        public static World Load(string save, Rules rules, IChunkSource source)
        {
            if (rules == null) throw new ArgumentNullException(nameof(rules));
            if (source == null) throw new ArgumentNullException(nameof(source));
            JObject s;
            try
            {
                s = JObject.Parse(save ?? "");
            }
            catch (JsonReaderException e)
            {
                throw new SaveException($"That isn't a save: {e.Message}");
            }
            Expect((int?)s["format"] == SaveFormat, $"a save of format {s["format"]}, not {SaveFormat}");
            Expect((string)s["source"] == source.Version, $"made by {s["source"]}, not {source.Version}");
            Expect((string)s["rules"] == rules.Fingerprint, "saved under other rules (or mods)");
            var w = new World(rules, (int)s["w"], (int)s["h"], (uint)s["seed"]);
            w.MakeFrom(source);
            w.LoadState(s);
            foreach (var c in s["chunks"] ?? new JArray()) w.LoadChunk((JObject)c);
            w.spans = null;
            w.activeCoords = null;
            w.Weather = w.SkyNow();
            return w;
        }

        static void Expect(bool ok, string what)
        {
            if (!ok) throw new SaveException("This save doesn't fit: " + what);
        }

        // ---------- the world's state between ticks ----------

        JToken SaveDefense() => Defense == null ? JValue.CreateNull() : new JObject
        {
            ["phase"] = (int)Defense.Phase,
            ["at"] = new JArray(Defense.Wave, Defense.Lives, Defense.ToSpawn, Defense.NextSpawn, Defense.Spawned),
            ["gate"] = new JArray(Defense.GateX, Defense.GateY, Defense.FlagX, Defense.FlagY),
            ["field"] = new JArray(Defense.X0, Defense.Y0, Defense.X1, Defense.Y1),
        };

        static JObject SaveRaider(Raider r)
        {
            var status = new JObject();
            foreach (var k in r.Status.Keys.OrderBy(k => k, StringComparer.Ordinal)) status[k] = r.Status[k];
            return new JObject
            {
                ["id"] = r.Id,
                ["kind"] = r.Kind,
                ["at"] = new JArray(r.X, r.Y, r.Hp, r.MaxHp, r.Wait),
                ["alive"] = r.Alive,
                ["status"] = status,
            };
        }

        void LoadState(JObject s)
        {
            Tick = (int)s["tick"];
            RngState = (uint)s["rng"];
            ClockAhead = (long)s["clockAhead"];
            Haste = s["haste"] is JArray h ? ((int)h[0], (int)h[1]) : ((int, int)?)null;
            WeatherSet = s["weather"] is JArray ws ? ((WeatherKind)(int)ws[0], (int)ws[1], (int)ws[2]) : ((WeatherKind, int, int)?)null;
            Wind = s["wind"] is JArray wd ? ((int)wd[0], (int)wd[1]) : ((int, int)?)null;
            foreach (var m in s["meteors"] ?? new JArray()) falling.Add(((int)m[0], (int)m[1], (int)m[2]));
            NextEntityId = (int)s["nextEntityId"];
            if (s["defense"] is JObject d)
            {
                var at = d["at"];
                var gate = d["gate"];
                var field = d["field"];
                Defense = new DefenseState
                {
                    Phase = (DefensePhase)(int)d["phase"],
                    Wave = (int)at[0],
                    Lives = (int)at[1],
                    ToSpawn = (int)at[2],
                    NextSpawn = (int)at[3],
                    Spawned = (int)at[4],
                    GateX = (int)gate[0],
                    GateY = (int)gate[1],
                    FlagX = (int)gate[2],
                    FlagY = (int)gate[3],
                    X0 = (int)field[0],
                    Y0 = (int)field[1],
                    X1 = (int)field[2],
                    Y1 = (int)field[3],
                };
            }
            foreach (var o in s["raiders"] ?? new JArray())
            {
                var at = o["at"];
                var r = new Raider { Id = (int)o["id"], Kind = (string)o["kind"], X = (int)at[0], Y = (int)at[1], Hp = (int)at[2], MaxHp = (int)at[3], Wait = (int)at[4], Alive = (bool)o["alive"] };
                foreach (var p in ((JObject)o["status"]).Properties()) r.Status[p.Name] = (int)p.Value;
                raiders.Add(r);
            }
        }

        // ---------- chunks ----------

        // The local indices of a chunk's cells that differ from the source's.
        static IEnumerable<int> ChangedCells(Chunk ch, IChunkSource source, Cell made)
        {
            for (int i = 0; i < Chunks.Area; i++)
            {
                var c = ch.Cells[i];
                if (c == null) continue;
                made.Reset();
                source.Make(ch.X * Chunks.Size + (i & Chunks.Mask), ch.Y * Chunks.Size + (i >> Chunks.Shift), made);
                if (!SameCell(c, made)) yield return i;
            }
        }

        static bool SameCell(Cell a, Cell b) =>
            a.T == b.T && a.Water == b.Water && a.Frozen == b.Frozen && a.Cover == b.Cover && a.Fire == b.Fire && a.Fuel == b.Fuel
            && a.Gas == b.Gas && a.GasAmt == b.GasAmt && a.Charge == b.Charge && a.Height == b.Height && a.Life == b.Life && a.DampUntil == b.DampUntil && a.Rust == b.Rust && a.Heat == b.Heat;

        // A chunk's coordinates, changed cells and streaming state (just the coordinates when it has neither).
        static JObject SaveChunk(Chunk ch, IChunkSource source, Cell made)
        {
            var o = new JObject { ["x"] = ch.X, ["y"] = ch.Y };
            if (!ch.Active) o["inactive"] = true;
            if (ch.LastLive != NeverLive) o["live"] = ch.LastLive;
            if (ch.EdgeLive.Any(t => t != NeverLive)) o["edges"] = new JArray(ch.EdgeLive);
            if (ch.StoppedAt != NeverLive) o["stopped"] = ch.StoppedAt;
            if (ch.CaughtUp != 0) o["caughtUp"] = ch.CaughtUp;
            var fire = Enumerable.Range(0, Chunks.Area).Where(i => ch.FireGeneration[i] != 0).Select(i => new JArray(i, ch.FireGeneration[i])).ToList();
            if (fire.Count > 0) o["fireGen"] = new JArray(fire);
            var cells = ChangedCells(ch, source, made).Select(i => SaveCell(i, ch.Cells[i])).ToList();
            if (cells.Count > 0) o["cells"] = new JArray(cells);
            return o;
        }

        // Rust only when it has some, so saves without it read as they did.
        static JArray SaveCell(int i, Cell c)
        {
            var a = new JArray(i, c.T, c.Water, c.Frozen ? 1 : 0, c.Cover, c.Fire, c.Fuel, c.Gas, c.GasAmt, c.Charge, c.Height, c.Life, c.DampUntil);
            if (c.Rust != 0 || c.Heat != 0) a.Add(c.Rust);
            if (c.Heat != 0) a.Add(c.Heat);
            return a;
        }

        void LoadChunk(JObject o)
        {
            var ch = FindChunk((int)o["x"], (int)o["y"]) ?? throw new SaveException($"This save doesn't fit: no chunk ({o["x"]}, {o["y"]}) in a world this size");
            ch.Active = o["inactive"] == null;
            ch.LastLive = (int?)o["live"] ?? NeverLive;
            for (int d = 0; d < 4; d++) ch.EdgeLive[d] = o["edges"] is JArray e ? (int)e[d] : NeverLive;
            ch.StoppedAt = (int?)o["stopped"] ?? NeverLive;
            ch.CaughtUp = (int?)o["caughtUp"] ?? 0;
            foreach (var f in o["fireGen"] ?? new JArray()) ch.FireGeneration[(int)f[0]] = (int)f[1];
            foreach (var a in o["cells"] ?? new JArray())
            {
                var c = ch.Cells[(int)a[0]];
                c.T = (string)a[1];
                c.Water = (int)a[2];
                c.Frozen = (int)a[3] != 0;
                c.Cover = (string)a[4];
                c.Fire = (int)a[5];
                c.Fuel = (int)a[6];
                c.Gas = (string)a[7];
                c.GasAmt = (int)a[8];
                c.Charge = (int)a[9];
                c.Height = (int)a[10];
                c.Life = (int)a[11];
                c.DampUntil = a is JArray all && all.Count > 12 ? (int)all[12] : 0;
                c.Rust = a is JArray rusted && rusted.Count > 13 ? (int)rusted[13] : 0;
                c.Heat = a is JArray hot && hot.Count > 14 ? (int)hot[14] : 0;
                c.ForgetDef();
            }
        }
    }
}
