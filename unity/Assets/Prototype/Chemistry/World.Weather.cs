// What the weather does to cells, once a tick after gas: rain fills squares
// and dry spells dry shallow ones, storms strike lightning. Rain's hold on fire
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

        /// <summary>The world's seed: the weather's, as RngState is the cells'.</summary>
        public uint Seed { get; }

        /// <summary>This tick's weather (Sky.Weather), set at the start of each Step.</summary>
        public WeatherNow Weather { get; private set; }

        /// <summary>Squares lightning struck this tick, for a view's flash and thunder.</summary>
        public IReadOnlyList<(int x, int y)> Strikes => strikes;

        /// <summary>The wind fire and gas follow: Gust of Wind's while it blows, else the weather's; -1 for none.</summary>
        public int WindDir => Wind.HasValue ? Wind.Value.dir : Weather.WindDir;

        /// <summary>Game minutes since midnight of day 1 (Sky.Minutes).</summary>
        public long Minutes => Sky.Minutes(Rules.Sim, Tick);

        void TickWeather(RowSpan[] rows)
        {
            var now = Weather;
            if (now.Period < Rules.Sim.Weather.CalmPeriods) return;
            var p = Rules.Sim.Weather;
            if (now.Rain > 0 && Tick % p.FillEvery == 0) Fill(rows, p.FillChance * now.Rain / 100);
            else if (now.Rain == 0 && Tick % p.DryEvery == 0) Dry(rows, p.DryChance);
            if (now.Kind == WeatherKind.Storm && Tick % p.StrikeEvery == 0 && Rand() < p.StrikeChance * now.Strength / 100) Strike();
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

        void Strike()
        {
            int x = (int)(Rand() * W), y = (int)(Rand() * H);
            strikes.Add((x, y));
            AddLog("Lightning strikes", LogKind.Notable);
            Apply(x, y, "lightning", 2, -1, false);
        }

        /// <summary>Rain may burn a fire down a step; true if it went out.</summary>
        bool Doused(Cell c)
        {
            int rain = Weather.Rain;
            if (rain == 0) return false;
            if (Rand() >= Rules.Sim.Weather.DouseChance * rain / 100) return false;
            if (--c.Fire > 0) return false;
            AddGas(c, "steam", 1);
            LogOnce("rain-douse", "The rain puts out a fire");
            return true;
        }

        /// <summary>How much of fire's spread the rain leaves, 1 when dry.</summary>
        double RainDamp() => 1 - Rules.Sim.Weather.Dampen * Weather.Rain / 100;
    }
}
