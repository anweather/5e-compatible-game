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
            if (casts.Count > 0) save["casts"] = new JArray(casts.Values.Select(SaveCast));
            if (crackedThings.Count > 0) save["crackedThings"] = new JArray(crackedThings.Select(t => t.ToString(System.Globalization.CultureInfo.InvariantCulture)));
            if (stars.Count > 0) save["stars"] = new JArray(stars.Select(st => new JArray(st.Spell, st.X, st.Y, st.Dir, st.Charge, st.Radius, st.Lands)));
            if (whirls.Count > 0) save["whirls"] = new JArray(whirls.Select(ww => new JArray(ww.X, ww.Y, ww.Radius, ww.Ticks, ww.Age, ww.Every, ww.MaxMass, ww.Spell, ww.Dir, ww.Travel, ww.Carrying)));
            if (waves.Count > 0) save["waves"] = new JArray(waves.Select(wv => new JArray(wv.Spell, wv.X, wv.Y, wv.Dir, wv.Width, wv.Left, wv.Every, wv.Age, wv.Power, wv.MaxMass, wv.OX, wv.OY, wv.AX, wv.AY, wv.Step)));
            if (GatesOpen) save["gatesOpen"] = true;
            if (machines.Count > 0) save["machines"] = new JArray(machines.OrderBy(m => m.Key).Select(m => new JArray(m.Key, m.Value.Level, m.Value.Hold, m.Value.Powered)));
            SaveGolf(save);
            if (boulders.Count > 0) save["boulders"] = new JArray(boulders.Select(b => new JArray(b.Spell, b.X, b.Y, b.Dir, b.Left, b.Every, b.Age)));
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

        // A player's cast in progress and grimoire. Only when there are any, so saves without read as they did.
        static JArray SaveCast(PlayerCast c) => new JArray(c.Player, c.First, c.Second, c.Meter, c.Total, c.Window, c.Slot, c.DpadCasting, new JArray(c.Known), c.Charge);

        // A rolling wave; one saved before waves rolled at an angle rolls on along its grid way from where it is.
        static Wave LoadWave(JArray wv)
        {
            var w = new Wave { Spell = (string)wv[0], X = (int)wv[1], Y = (int)wv[2], Dir = (int)wv[3], Width = (int)wv[4], Left = (int)wv[5], Every = (int)wv[6], Age = (int)wv[7], Power = (int)wv[8], MaxMass = (int)wv[9] };
            bool angled = wv.Count > 14;
            w.OX = angled ? (int)wv[10] : w.X;
            w.OY = angled ? (int)wv[11] : w.Y;
            w.AX = angled ? (int)wv[12] : DirX(w.Dir);
            w.AY = angled ? (int)wv[13] : DirY(w.Dir);
            w.Step = angled ? (int)wv[14] : 0;
            return w;
        }

        void LoadCast(JToken a)
        {
            var c = new PlayerCast
            {
                Player = (string)a[0],
                First = (string)a[1],
                Second = (string)a[2],
                Meter = (int)a[3],
                Total = (int)a[4],
                Window = (int)a[5],
                Slot = (int)a[6],
                DpadCasting = (int)a[7],
                // Not Holding: no button is down on a loaded game, so a held cast goes off as a tap.
                Charge = a.Count() > 9 ? (int)a[9] : 1,
            };
            foreach (var id in a[8]) c.Known.Add((string)id);
            casts[c.Player] = c;
        }

        // The machines, whether they have the gates open, and the boulders still rolling.
        void LoadMachinesAndBoulders(JObject s)
        {
            GatesOpen = s["gatesOpen"] != null && (bool)s["gatesOpen"];
            foreach (var m in s["machines"] ?? new JArray())
                machines[(int)m[0]] = new MachineState { Level = (int)m[1], Hold = (int)m[2], Powered = (bool)m[3] };
            foreach (var b in s["boulders"] ?? new JArray())
                boulders.Add(new Boulder { Spell = (string)b[0], X = (int)b[1], Y = (int)b[2], Dir = (int)b[3], Left = (int)b[4], Every = (int)b[5], Age = (int)b[6] });
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
            foreach (var t in s["crackedThings"] ?? new JArray()) crackedThings.Add(ulong.Parse((string)t, System.Globalization.CultureInfo.InvariantCulture));
            foreach (var st in s["stars"] ?? new JArray())
                stars.Add(new Star { Spell = (string)st[0], X = (int)st[1], Y = (int)st[2], Dir = (int)st[3], Charge = (int)st[4], Radius = (int)st[5], Lands = (int)st[6] });
            foreach (var ww in s["whirls"] ?? new JArray())
                whirls.Add(new Whirl { X = (int)ww[0], Y = (int)ww[1], Radius = (int)ww[2], Ticks = (int)ww[3], Age = (int)ww[4], Every = (int)ww[5], MaxMass = (int)ww[6], Spell = (string)ww[7], Dir = (int)ww[8], Travel = (int)ww[9], Carrying = (string)ww[10] });
            foreach (var wv in s["waves"] ?? new JArray())
                waves.Add(LoadWave((JArray)wv));
            LoadMachinesAndBoulders(s);
            LoadGolf(s);
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
            foreach (var a in s["casts"] ?? new JArray()) LoadCast(a);
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
            && a.Gas == b.Gas && a.GasAmt == b.GasAmt && a.Charge == b.Charge && a.Height == b.Height && a.Life == b.Life && a.DampUntil == b.DampUntil && a.Rust == b.Rust && a.Heat == b.Heat && a.Star == b.Star && a.Cracked == b.Cracked && a.Lit == b.Lit && a.Slick == b.Slick;

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
            if (c.Rust != 0 || c.Heat != 0 || c.Lit != 0 || c.Slick != 0 || c.Star != 0 || c.Cracked != 0) a.Add(c.Rust);
            if (c.Heat != 0 || c.Lit != 0 || c.Slick != 0 || c.Star != 0 || c.Cracked != 0) a.Add(c.Heat);
            if (c.Lit != 0 || c.Slick != 0 || c.Star != 0 || c.Cracked != 0) a.Add(c.Lit);
            if (c.Slick != 0 || c.Star != 0 || c.Cracked != 0) a.Add(c.Slick);
            if (c.Star != 0 || c.Cracked != 0) a.Add(c.Star);
            if (c.Cracked != 0) a.Add(c.Cracked);
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
            foreach (var a in o["cells"] ?? new JArray()) LoadCell(ch.Cells[(int)a[0]], (JArray)a);
        }

        // One saved cell (SaveCell): the fields every save has, then the later ones, 0 when a save leaves them out.
        static void LoadCell(Cell c, JArray a)
        {
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
            c.DampUntil = Later(a, 12);
            c.Rust = Later(a, 13);
            c.Heat = Later(a, 14);
            c.Lit = Later(a, 15);
            c.Slick = Later(a, 16);
            c.Star = Later(a, 17);
            c.Cracked = Later(a, 18);
            c.ForgetDef();
        }

        // A field added after the first save format: 0 when the cell's array stops short of it.
        static int Later(JArray a, int i) => a.Count > i ? (int)a[i] : 0;
    }
}
