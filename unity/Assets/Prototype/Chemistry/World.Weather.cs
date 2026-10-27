// What the weather does to cells, once a tick after gas: rain fills squares
// and dry spells dry shallow ones, snow freezes shallow water (and dry spells
// thaw it), storms strike lightning, and in a meteor
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

        /// <summary>Game minutes since midnight of day 1 (Sky.Minutes).</summary>
        public long Minutes => Sky.Minutes(Rules.Sim, Tick);

        void TickWeather(RowSpan[] rows)
        {
            var now = Weather;
            newMeteors.Clear();
            LandMeteors();
            if (now.Period < Rules.Sim.Weather.CalmPeriods) return;
            var p = Rules.Sim.Weather;
            if (now.Rain > 0)
            {
                if (Tick % p.FillEvery == 0) Fill(rows, p.FillChance * now.Rain / 100);
            }
            else if (now.Snow > 0)
            {
                if (Tick % p.FreezeEvery == 0) Freeze(rows, p.FreezeChance * now.Snow / 100, true);
            }
            else if (Tick % p.DryEvery == 0)
            {
                Dry(rows, p.DryChance);
                Freeze(rows, p.ThawChance, false);
            }
            if (now.Kind == WeatherKind.Storm && Tick % p.StrikeEvery == 0 && Rand() < p.StrikeChance * now.Strength / 100) Strike();
            if (now.Meteors > 0 && Tick % p.MeteorEvery == 0 && Rand() < p.MeteorChance * now.Meteors / 100) Meteor();
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
