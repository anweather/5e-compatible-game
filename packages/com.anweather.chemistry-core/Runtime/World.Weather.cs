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

        public const string SnowCover = "snow", BankCover = "snowbank";

        public uint Seed { get; }

        public WeatherNow Weather { get; private set; }

        public IReadOnlyList<(int x, int y)> Strikes => strikes;

        const int HistoryTicks = 600;
        readonly List<(int x, int y, int tick)> strikeHistory = new List<(int x, int y, int tick)>();
        readonly List<(int x, int y, int lands, int tick)> meteorHistory = new List<(int x, int y, int lands, int tick)>();

        public IEnumerable<(int x, int y, int tick)> StrikesSince(int tick)
        {
            foreach (var s in strikeHistory) if (s.tick > tick) yield return s;
        }

        public IEnumerable<(int x, int y, int lands, int tick)> MeteorsSince(int tick)
        {
            foreach (var m in meteorHistory) if (m.tick > tick) yield return m;
        }

        void TrimWeatherHistory()
        {
            strikeHistory.RemoveAll(s => s.tick <= Tick - HistoryTicks);
            meteorHistory.RemoveAll(m => m.tick <= Tick - HistoryTicks);
            flashHistory.RemoveAll(f => f.tick <= Tick - HistoryTicks);
            shedStrikes.RemoveAll(s => s.tick <= Tick - HistoryTicks);
            if (firedHistory.Count > 0) firedHistory.RemoveAll(f => f.tick <= Tick - HistoryTicks);
        }

        public IReadOnlyList<(int x, int y, int lands)> NewMeteors => newMeteors;

        public int WindDir => Wind.HasValue ? Wind.Value.dir : Weather.WindDir;

        bool WeatherActs => Weather.Period >= Rules.Sim.Weather.CalmPeriods || WeatherSet.HasValue;

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

        void Snow(RowSpan[] rows, int snow)
        {
            var p = Rules.Sim.Weather;
            Freeze(rows, p.FreezeChance * snow / 100 * (IsNight ? Rules.Sim.Day.NightFreeze : 1), true);
            Settle(rows, p.CoverChance * snow / 100, p.BankChance * snow / 100, p.DeepenChance * snow / 100);
        }

        void Meteor()
        {
            var (x, y) = AnyCell();
            var m = (x, y, Tick + Rules.Sim.Weather.MeteorFallTicks);
            falling.Add(m);
            newMeteors.Add(m);
            meteorHistory.Add((m.x, m.y, m.Item3, Tick));
        }

        void LandMeteors()
        {
            if (falling.Count == 0) return;
            var spell = Rules.Sim.Weather.MeteorSpell == null ? null : Rules.Spell(Rules.Sim.Weather.MeteorSpell);
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
            int max = Math.Min(Rules.Sim.WaterMaxLevel, Rules.Sim.ThinWater + 1);
            foreach (var s in rows)
            {
                var cells = s.Chunk.ActiveCells;
                for (int i = s.Start; i < s.End; i++)
                {
                    var c = cells[i];
                    if (IsBlocking(c) || c.Water >= max || !TakesWater(c)) continue;
                    if (Rand() < chance)
                    {
                        c.Water++;
                        c.Liquid = 0;
                        c.DampUntil = Tick + Rules.Sim.DampTicks;
                    }
                }
            }
        }

        void Dry(RowSpan[] rows, double chance)
        {
            var p = Rules.Sim;
            int turns = chance > 0 ? Math.Max(1, (int)Math.Round(p.Seep / chance)) : 0, beat = Tick / Math.Max(1, p.Weather.DryEvery);
            foreach (var s in rows)
            {
                var cells = s.Chunk.ActiveCells;
                for (int i = s.Start; i < s.End; i++)
                {
                    var c = cells[i];
                    if (c.Water == 0 || c.Frozen || c.Liquid != 0) continue;
                    if (c.Water <= 2)
                    {
                        if (Rand() < chance) c.Water--;
                    }
                    else if (turns > 0 && p.SoakPass > 0 && OnTurn(s.Chunk, i, beat, turns) && Seeps(s.Chunk, i, c)) c.Water--;
                }
            }
        }

        static bool OnTurn(Chunk ch, int i, int beat, int turns)
        {
            int x = (ch.X << Chunks.Shift) + (i & Chunks.Mask), y = (ch.Y << Chunks.Shift) + (i >> Chunks.Shift);
            return ((beat + x * 7 + y * 13) % turns + turns) % turns == 0;
        }

        bool Seeps(Chunk ch, int i, Cell c)
        {
            if (!SoaksIn(c)) return false;
            for (int d = 0; d < 4; d++)
            {
                var n = Chunk.Next(ch, i, d, out _, out _);
                if (n != null && n.Water > 0 && !SoaksIn(n)) return false;
            }
            return true;
        }

        bool SoaksIn(Cell c)
        {
            var t = TerrainOf(c);
            return (t.SoakEvery > 0 || t.SoakEveryWet > 0) && !c.Frozen && LiquidOf(c).Soaks;
        }

        void Freeze(RowSpan[] rows, double chance, bool freeze)
        {
            foreach (var s in rows)
            {
                var cells = s.Chunk.ActiveCells;
                for (int i = s.Start; i < s.End; i++)
                {
                    var c = cells[i];
                    if (c.Water == 0 || c.Water > 2 || c.Frozen == freeze) continue;
                    if (c.Liquid == 0 ? Rand() < chance : freeze && LiquidFreezes(c) != null && Rand() < chance) FreezeThin(s.Chunk, c, freeze);
                }
            }
        }

        void FreezeThin(Chunk ch, Cell c, bool freeze)
        {
            if (c.Liquid == 0) c.Frozen = freeze;
            else FreezeLiquid(ch, c);
        }

        void Settle(RowSpan[] rows, double cover, double bank, double deepen)
        {
            int upwind = WindDir >= 0 ? (WindDir + 2) % 4 : -1;
            foreach (var s in rows)
                for (int i = s.Start; i < s.End; i++) SettleOn(s.Chunk, i, upwind, cover, bank, deepen);
        }

        void SettleOn(Chunk ch, int i, int upwind, double cover, double bank, double deepen)
        {
            var c = ch.ActiveCells[i];
            var w = Rules.Sim.Weather;
            if (c.Water > 0 || c.Fire > 0 || IsBlocking(c)) return;
            if (c.Cover == null)
            {
                if (Rand() < cover) c.Cover = w.SnowCover;
            }
            else if (c.Cover == w.SnowCover && w.BankCover != null && Rand() < (Sheltered(ch, i, upwind) ? bank : deepen)) c.Cover = w.BankCover;
        }

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

        bool PatchCentre(uint block, int rx, int ry, int n, out int x, out int y)
        {
            var p = Rules.Sim.Weather;
            int size = p.FogRegionChunks * Chunks.Size, x0 = rx * size, y0 = ry * size;
            int w = IsOpen ? size : Math.Min(W, x0 + size) - x0, h = IsOpen ? size : Math.Min(H, y0 + size) - y0;
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

        void Melt(RowSpan[] rows, double chance)
        {
            foreach (var s in rows)
            {
                var cells = s.Chunk.ActiveCells;
                for (int i = s.Start; i < s.End; i++) MeltOn(cells[i], chance);
            }
        }

        void MeltOn(Cell c, double chance)
        {
            var w = Rules.Sim.Weather;
            if (c.Cover == null || (c.Cover != w.SnowCover && c.Cover != w.BankCover)) return;
            if (Rand() >= chance) return;
            if (c.Cover == w.BankCover)
            {
                c.Cover = w.SnowCover;
                return;
            }
            c.Cover = null;
            if (c.Water > 0) return;
            c.Water = 1;
            c.Liquid = 0;
        }

        void Strike()
        {
            var (x, y) = AnyCell();
            strikes.Add((x, y));
            strikeHistory.Add((x, y, Tick));
            AddLog("Lightning strikes", LogKind.Notable);
            Apply(x, y, "lightning", 2, -1, false);
        }

        bool Doused(Cell c)
        {
            int rain = Weather.Wet;
            if (rain == 0) return false;
            if (Rand() >= Rules.Sim.Weather.DouseChance * rain / 100) return false;
            if (--c.Fire > 0) return false;
            AddGas(c, Rules.Sim.WaterBoils, 1);
            LogOnce("rain-douse", "The rain puts out a fire");
            return true;
        }

        double RainDamp() => 1 - Rules.Sim.Weather.Dampen * Weather.Wet / 100;
    }
}
