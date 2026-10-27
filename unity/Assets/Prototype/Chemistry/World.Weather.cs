// What the weather does to cells, once a tick after gas: rain fills squares
// and dry spells dry shallow ones, snow freezes shallow water, covers open
// ground and drifts into banks against walls and trees (and dry spells thaw
// the ice and melt the snow), storms strike lightning, and in a meteor
// shower meteors appear and land a little later as the Meteor spell. Rain's hold on fire
// (dousing, less spread) is in Burn and SpreadChance, the prevailing wind in
// WindDir. Port of tickWeather in sandbox/src/sim.ts; the weather itself
// comes from Sky.cs. Nothing happens during the calm stretches the world
// starts with.
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
            if (!WeatherActs) return;
            WeatherOnCells(rows);
            var p = Rules.Sim.Weather;
            if (now.Kind == WeatherKind.Storm && Tick % p.StrikeEvery == 0 && Rand() < p.StrikeChance * now.Strength / 100) Strike();
            if (now.Meteors > 0 && Tick % p.MeteorEvery == 0 && Rand() < p.MeteorChance * now.Meteors / 100) Meteor();
        }

        /// <summary>What the weather does to each cell (rain fills, snow freezes and settles, dry weather dries and thaws), without strikes or meteors.</summary>
        void WeatherOnCells(RowSpan[] rows)
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
                if (Tick % p.FreezeEvery == 0)
                {
                    Freeze(rows, p.FreezeChance * now.Snow / 100, true);
                    Settle(rows, p.CoverChance * now.Snow / 100, p.BankChance * now.Snow / 100);
                }
            }
            else if (Tick % p.DryEvery == 0)
            {
                Dry(rows, p.DryChance);
                Freeze(rows, p.ThawChance, false);
                Melt(rows, p.ThawChance);
            }
        }

        void Meteor()
        {
            int x = (int)(Rand() * W), y = (int)(Rand() * H);
            var m = (x, y, Tick + Rules.Sim.Weather.MeteorFallTicks);
            falling.Add(m);
            newMeteors.Add(m);
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
        void Settle(RowSpan[] rows, double cover, double bank)
        {
            int upwind = WindDir >= 0 ? (WindDir + 2) % 4 : -1;
            foreach (var s in rows)
                for (int i = s.Start; i < s.End; i++) SettleOn(s.Chunk, i, upwind, cover, bank);
        }

        // Snow on one square: a cover on open ground, a bank where it's sheltered.
        void SettleOn(Chunk ch, int i, int upwind, double cover, double bank)
        {
            var c = ch.Cells[i];
            if (c.Water > 0 || c.Fire > 0 || IsBlocking(c)) return;
            if (c.Cover == null)
            {
                if (Rand() < cover) c.Cover = SnowCover;
            }
            else if (c.Cover == SnowCover && Sheltered(ch, i, upwind) && Rand() < bank) c.Cover = BankCover;
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
