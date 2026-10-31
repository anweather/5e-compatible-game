// What the weather does to cells, once a tick after gas: rain fills squares
// and dry spells dry shallow ones, snow freezes shallow water, covers open
// ground and drifts into banks against walls and trees, building up slowly in
// the open too (and dry spells thaw the ice and melt the snow), fog lays
// patches of thin fog gas on low ground near water that drift a little and
// thin away once the stretch ends (World.LineOfSight), storms strike lightning, and in a meteor
// shower meteors appear and land a little later as the Meteor spell. Rain's hold on fire
// (dousing, less spread) is in Burn and SpreadChance, the prevailing wind in
// WindDir. Port of tickWeather in sandbox/src/sim.ts; the weather itself
// comes from Sky.cs. Nothing happens during the calm stretches the world
// starts with. At night (World.Night.cs) snow freezes water day.nightFreeze
// times as fast, and dry weather leaves the ice and snow till morning.
using System;
using System.Collections.Generic;

namespace Chem
{
    public sealed partial class World
    {
        readonly List<(int x, int y)> strikes = new List<(int x, int y)>();
        readonly List<(int x, int y, int lands)> falling = new List<(int x, int y, int lands)>();
        readonly List<(int x, int y, int lands)> newMeteors = new List<(int x, int y, int lands)>();

        /// <summary>The covers snow lays and drifts into (materials.json).</summary>
        public const string SnowCover = "snow", BankCover = "snowbank";

        /// <summary>The spell a meteor lands as (its crater, its fire).</summary>
        public const string MeteorSpell = "meteor";

        /// <summary>The world's seed: the weather's, as RngState is the cells'.</summary>
        public uint Seed { get; }

        /// <summary>This tick's weather (Sky.Weather), set at the start of each Step.</summary>
        public WeatherNow Weather { get; private set; }

        /// <summary>Squares lightning struck this tick, for a view's flash and thunder.</summary>
        public IReadOnlyList<(int x, int y)> Strikes => strikes;

        // The strikes and the new meteors of the last HistoryTicks ticks, stamped with their tick, for
        // a view that steps several ticks in one frame (tick-spread, catch-up) and would otherwise see
        // only the last tick's. Not world state: never hashed or saved.
        const int HistoryTicks = 600;
        readonly List<(int x, int y, int tick)> strikeHistory = new List<(int x, int y, int tick)>();
        readonly List<(int x, int y, int lands, int tick)> meteorHistory = new List<(int x, int y, int lands, int tick)>();

        /// <summary>Strikes after <paramref name="tick"/> (within the last 600 ticks), oldest first, each with its tick.</summary>
        public IEnumerable<(int x, int y, int tick)> StrikesSince(int tick)
        {
            foreach (var s in strikeHistory) if (s.tick > tick) yield return s;
        }

        /// <summary>Meteors that appeared after <paramref name="tick"/> (within the last 600 ticks), oldest first: where, the tick they land on, the tick they appeared.</summary>
        public IEnumerable<(int x, int y, int lands, int tick)> MeteorsSince(int tick)
        {
            foreach (var m in meteorHistory) if (m.tick > tick) yield return m;
        }

        // Drops what is older than HistoryTicks (each new tick).
        void TrimWeatherHistory()
        {
            strikeHistory.RemoveAll(s => s.tick <= Tick - HistoryTicks);
            meteorHistory.RemoveAll(m => m.tick <= Tick - HistoryTicks);
            flashHistory.RemoveAll(f => f.tick <= Tick - HistoryTicks);
            shedStrikes.RemoveAll(s => s.tick <= Tick - HistoryTicks);
        }

        /// <summary>Meteors that appeared this tick, for a view to bring down: where, and the tick they land on.</summary>
        public IReadOnlyList<(int x, int y, int lands)> NewMeteors => newMeteors;

        /// <summary>The wind fire and gas follow: Gust of Wind's while it blows, else the weather's; -1 for none.</summary>
        public int WindDir => Wind.HasValue ? Wind.Value.dir : Weather.WindDir;


        /// <summary>Whether the weather changes the cells this tick: not in the calm first periods, unless a spell set it.</summary>
        bool WeatherActs => Weather.Period >= Rules.Sim.Weather.CalmPeriods || WeatherSet.HasValue;

        /// <summary>Whether rain is falling on the cells this tick, filling them here and there.</summary>
        bool Raining => WeatherActs && Weather.Rain > 0;

        void TickWeather(RowSpan[] rows)
        {
            var now = Weather;
            newMeteors.Clear();
            LandMeteors();
            LandStars();
            if (!WeatherActs) return;
            WeatherOnCells(rows, tickChunks);
            var p = Rules.Sim.Weather;
            if (now.Kind == WeatherKind.Storm && Tick % p.StrikeEvery == 0 && Rand() < p.StrikeChance * now.Strength / 100) Strike();
            if (now.Meteors > 0 && Tick % p.MeteorEvery == 0 && Rand() < p.MeteorChance * now.Meteors / 100) Meteor();
        }

        /// <summary>What the weather does to each cell (rain fills, snow freezes and settles, dry weather dries and thaws), without strikes or meteors.</summary>
        void WeatherOnCells(RowSpan[] rows, Chunk[] chunks)
        {
            if (!WeatherActs) return;
            var now = Weather;
            var p = Rules.Sim.Weather;
            if (now.Rain > 0)
            {
                if (Tick % p.FillEvery == 0) Fill(rows, p.FillChance * now.Rain / 100);
            }
            else if (now.Snow > 0)
            {
                if (Tick % p.FreezeEvery == 0) Snow(rows, now.Snow);
            }
            else if (now.Fog > 0)
            {
                if (Tick % p.FogEvery == 0) Veil(chunks, p.FogGas, p.FogChance * now.Fog / 100);
            }
            else if (Tick % p.DryEvery == 0)
            {
                Dry(rows, p.DryChance);
                if (IsNight) return;
                Freeze(rows, p.ThawChance, false);
                Melt(rows, p.ThawChance);
            }
        }

        /// <summary>Snow at this strength (0..100): shallow water freezes, and snow settles, banks and builds up.</summary>
        void Snow(RowSpan[] rows, int snow)
        {
            var p = Rules.Sim.Weather;
            Freeze(rows, p.FreezeChance * snow / 100 * (IsNight ? Rules.Sim.Day.NightFreeze : 1), true);
            Settle(rows, p.CoverChance * snow / 100, p.BankChance * snow / 100, p.DeepenChance * snow / 100);
        }

        void Meteor()
        {
            int x = (int)(Rand() * W), y = (int)(Rand() * H);
            var m = (x, y, Tick + Rules.Sim.Weather.MeteorFallTicks);
            falling.Add(m);
            newMeteors.Add(m);
            meteorHistory.Add((m.x, m.y, m.Item3, Tick));
        }

        // Meteors whose tick has come land as the Meteor spell, in the order they appeared; one that
        // lands on paused ground (no one near) leaves no mark.
        void LandMeteors()
        {
            if (falling.Count == 0) return;
            var spell = Rules.Spell(MeteorSpell);
            foreach (var (x, y, lands) in falling)
            {
                if (lands > Tick || spell == null || !IsActiveAt(x, y)) continue;
                AddLog("A meteor strikes", LogKind.Notable);
                Land(spell, x, y, 0, 1, 0, 0);
            }
            falling.RemoveAll(m => m.lands <= Tick);
        }

        void Fill(RowSpan[] rows, double chance)
        {
            int max = Rules.Sim.WaterMaxLevel;
            foreach (var s in rows)
            {
                var cells = s.Chunk.Cells;
                for (int i = s.Start; i < s.End; i++)
                {
                    var c = cells[i];
                    if (IsBlocking(c) || c.Water >= max) continue;
                    if (Rand() < chance) c.Water++;
                }
            }
        }

        /// <summary>A unit of water off squares two units deep or less (not ice): puddles dry, lakes stay.</summary>
        void Dry(RowSpan[] rows, double chance)
        {
            foreach (var s in rows)
            {
                var cells = s.Chunk.Cells;
                for (int i = s.Start; i < s.End; i++)
                {
                    var c = cells[i];
                    if (c.Water == 0 || c.Water > 2 || c.Frozen) continue;
                    if (Rand() < chance) c.Water--;
                }
            }
        }

        /// <summary>Snow freezes the water on squares two units deep or less; without it, that ice thaws. Lakes are left alone.</summary>
        void Freeze(RowSpan[] rows, double chance, bool freeze)
        {
            foreach (var s in rows)
            {
                var cells = s.Chunk.Cells;
                for (int i = s.Start; i < s.End; i++)
                {
                    var c = cells[i];
                    if (c.Water == 0 || c.Water > 2 || c.Frozen == freeze) continue;
                    if (Rand() < chance) c.Frozen = freeze;
                }
            }
        }

        /// <summary>Snow settles on open, dry, unburnt ground, and drifts into banks where it's sheltered.</summary>
        void Settle(RowSpan[] rows, double cover, double bank, double deepen)
        {
            int upwind = WindDir >= 0 ? (WindDir + 2) % 4 : -1;
            foreach (var s in rows)
                for (int i = s.Start; i < s.End; i++) SettleOn(s.Chunk, i, upwind, cover, bank, deepen);
        }

        // Snow on one square: a cover on open ground, a bank where it's sheltered, and slowly in the open.
        void SettleOn(Chunk ch, int i, int upwind, double cover, double bank, double deepen)
        {
            var c = ch.Cells[i];
            if (c.Water > 0 || c.Fire > 0 || IsBlocking(c)) return;
            if (c.Cover == null)
            {
                if (Rand() < cover) c.Cover = SnowCover;
            }
            else if (c.Cover == SnowCover && Rand() < (Sheltered(ch, i, upwind) ? bank : deepen)) c.Cover = BankCover;
        }

        /// <summary>
        /// Fog weather: patches of its gas, here and there. Where they stand is worked out afresh from the
        /// seed, the stretch and the cells, with no state of its own, so every peer and a loaded save agree,
        /// and only the patches' cells are visited. Each block of chunks with an active chunk in it has its
        /// patches, in row-major order; cells of inactive chunks are left alone.
        /// </summary>
        void Veil(Chunk[] chunks, string gas, double chance)
        {
            var p = Rules.Sim.Weather;
            var blocks = new SortedSet<(int y, int x)>();
            foreach (var ch in chunks) blocks.Add((ch.Y / p.FogRegionChunks, ch.X / p.FogRegionChunks));
            int stretch = WeatherSet?.from ?? Weather.Period;
            int start = WeatherSet?.from ?? Weather.Period * Sky.Ticks(Rules.Sim, p.PeriodMinutes);
            int breeze = (int)(Sky.Hash(Seed, stretch, 6) >> 30), drift = (Tick - start) / p.FogDriftTicks;
            foreach (var (ry, rx) in blocks)
            {
                uint block = Sky.Hash(Sky.Hash(Seed, stretch, 5), rx, (uint)ry);
                for (int n = 0; n < p.FogPatches; n++)
                {
                    if (!PatchCentre(block, rx, ry, n, out int x, out int y)) continue;
                    int radius = p.FogRadiusMin + (int)(Sky.Hash(block, n, 9) % (uint)(Math.Max(0, p.FogRadiusMax - p.FogRadiusMin) + 1));
                    LayPatch(x + DirX(breeze) * drift, y + DirY(breeze) * drift, radius, gas, chance);
                }
            }
        }

        /// <summary>The best of a block's FogTries cells for its patch n, by the hash of the block; false if none is dry and open.</summary>
        bool PatchCentre(uint block, int rx, int ry, int n, out int x, out int y)
        {
            var p = Rules.Sim.Weather;
            int size = p.FogRegionChunks * Chunks.Size, x0 = rx * size, y0 = ry * size;
            int w = Math.Min(W, x0 + size) - x0, h = Math.Min(H, y0 + size) - y0;
            x = y = 0;
            double best = double.NegativeInfinity;
            for (int t = 0; t < p.FogTries; t++)
            {
                uint at = Sky.Hash(block, n * p.FogTries + t, 7);
                int cx = x0 + (int)((at & 0xffff) % (uint)w), cy = y0 + (int)((at >> 16) % (uint)h);
                double score = PatchScore(cx, cy);
                if (score <= best) continue;
                best = score;
                x = cx;
                y = cy;
            }
            return !double.IsNegativeInfinity(best);
        }

        /// <summary>How much fog likes a cell: low ground with water near; never on water or anything blocking.</summary>
        double PatchScore(int x, int y)
        {
            var p = Rules.Sim.Weather;
            var c = At(x, y);
            if (c == null || c.Water > 0 || IsBlocking(c)) return double.NegativeInfinity;
            int wet = 0;
            for (int d = 0; d < 4; d++)
            {
                var n = At(x + DirX(d) * p.FogWetReach, y + DirY(d) * p.FogWetReach);
                if (n != null && n.Water > 0) wet++;
            }
            return p.FogWet * wet - p.FogLow * GroundUnits(x, y);
        }

        /// <summary>A unit of gas on the open, gasless cells of a disc: full chance over its middle, thinning at the rim.</summary>
        void LayPatch(int x, int y, int radius, string gas, double chance)
        {
            int r2 = radius * radius;
            for (int dy = -radius; dy <= radius; dy++)
                for (int dx = -radius; dx <= radius; dx++)
                {
                    int d2 = dx * dx + dy * dy;
                    if (d2 >= r2) continue;
                    var c = LocateActive(x + dx, y + dy, out _, out _);
                    if (c == null || c.Gas != null || IsBlocking(c)) continue;
                    if (Rand() < chance * Math.Min(1.0, 2.0 * (r2 - d2) / r2)) AddGas(c, gas, 1);
                }
        }

        /// <summary>Whether something blocking stands next to a square: on its upwind side in a wind, any side without.</summary>
        bool Sheltered(Chunk ch, int i, int upwind)
        {
            for (int d = 0; d < 4; d++)
            {
                if (upwind >= 0 && d != upwind) continue;
                var n = Chunk.Next(ch, i, d, out var nch, out _);
                if (n != null && nch.Active && IsBlocking(n)) return true;
            }
            return false;
        }

        /// <summary>Banks sink back to snow, and snow melts into a puddle.</summary>
        void Melt(RowSpan[] rows, double chance)
        {
            foreach (var s in rows)
            {
                var cells = s.Chunk.Cells;
                for (int i = s.Start; i < s.End; i++) MeltOn(cells[i], chance);
            }
        }

        // One square's snow: a bank sinks to snow, snow melts into a puddle.
        void MeltOn(Cell c, double chance)
        {
            if (c.Cover != SnowCover && c.Cover != BankCover) return;
            if (Rand() >= chance) return;
            if (c.Cover == BankCover)
            {
                c.Cover = SnowCover;
                return;
            }
            c.Cover = null;
            if (c.Water == 0) c.Water = 1;
        }

        void Strike()
        {
            int x = (int)(Rand() * W), y = (int)(Rand() * H);
            strikes.Add((x, y));
            strikeHistory.Add((x, y, Tick));
            AddLog("Lightning strikes", LogKind.Notable);
            Apply(x, y, "lightning", 2, -1, false);
        }

        /// <summary>Rain (or snow, by half) may burn a fire down a step; true if it went out.</summary>
        bool Doused(Cell c)
        {
            int rain = Weather.Wet;
            if (rain == 0) return false;
            if (Rand() >= Rules.Sim.Weather.DouseChance * rain / 100) return false;
            if (--c.Fire > 0) return false;
            AddGas(c, "steam", 1);
            LogOnce("rain-douse", "The rain puts out a fire");
            return true;
        }

        /// <summary>How much of fire's spread the rain (or snow, by half) leaves, 1 when dry.</summary>
        double RainDamp() => 1 - Rules.Sim.Weather.Dampen * Weather.Wet / 100;
    }
}
