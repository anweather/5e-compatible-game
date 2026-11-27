// The time spells: Hasten Time runs the day clock faster for a while, Control
// Weather holds a weather of the caster's choosing for a while. Both are world
// state set by a cast and moved on by the tick, so every peer (and a late
// joiner replaying the casts from tick 0) has the same sky. Port of the time
// parts of sandbox/src/actions.ts (hastenTime, controlWeather) and sim.ts
// (tickClock, endHeldWeather, skyNow).
using System;

namespace Chem
{
    public sealed partial class World
    {
        public const string HasteEffect = "haste", WeatherEffect = "weather";

        public static readonly WeatherKind[] WeatherCycle = { WeatherKind.Clear, WeatherKind.Rain, WeatherKind.Snow, WeatherKind.Storm };

        public (int rate, int ticks)? Haste { get; set; }

        public long ClockAhead { get; set; }

        public (WeatherKind kind, int from, int until)? WeatherSet { get; set; }

        public long ClockTick => Tick + ClockAhead;

        public long Minutes => Sky.Minutes(Rules.Sim, ClockTick);

        public double ClockSmooth(double fraction) => ClockTick + fraction * (Haste.HasValue ? Haste.Value.rate : 1);

        void Hasten(SpellDef s, int charge)
        {
            if (Haste.HasValue)
            {
                Haste = null;
                AddLog("Time settles back to its own pace", LogKind.Notable);
                return;
            }
            Haste = (Math.Max(1, s.Rate), ChargedLength(Math.Max(1, s.Duration), charge));
            AddLog("Time races: the sun hurries across the sky", LogKind.Notable);
        }

        void HoldWeather(SpellDef s, int charge)
        {
            var now = WeatherSet.HasValue ? WeatherSet.Value.kind : Weather.Kind;
            var next = WeatherCycle[(Array.IndexOf(WeatherCycle, now) + 1) % WeatherCycle.Length];
            WeatherSet = (next, Tick, Tick + ChargedLength(Math.Max(1, s.Duration), charge));
            AddLog(next == WeatherKind.Clear ? "The sky clears at your word" : $"The sky turns to {next.ToString().ToLowerInvariant()} at your word", LogKind.Notable);
        }

        WeatherNow SkyNow()
        {
            var natural = Sky.Weather(Rules.Sim, Seed, Tick);
            if (!WeatherSet.HasValue) return natural;
            var (kind, from, until) = WeatherSet.Value;
            return Sky.Held(Rules.Sim, Seed, kind, from, until, Tick, natural.Period);
        }

        void EndSetWeather()
        {
            if (!WeatherSet.HasValue || Tick < WeatherSet.Value.until) return;
            WeatherSet = null;
            AddLog("The weather goes its own way again");
        }

        void TickClock()
        {
            if (!Haste.HasValue) return;
            var (rate, ticks) = Haste.Value;
            ClockAhead += rate - 1;
            if (ticks > 1)
            {
                Haste = (rate, ticks - 1);
                return;
            }
            Haste = null;
            AddLog("Time slows back to its own pace");
        }

        public string SkyHash() => Fnv1a.Hash(SkyState());

        public string SkyState()
        {
            var inv = System.Globalization.CultureInfo.InvariantCulture;
            string haste = Haste.HasValue ? Haste.Value.rate.ToString(inv) + "x" + Haste.Value.ticks.ToString(inv) : "-";
            string held = WeatherSet.HasValue
                ? ((int)WeatherSet.Value.kind).ToString(inv) + ":" + WeatherSet.Value.from.ToString(inv) + ":" + WeatherSet.Value.until.ToString(inv)
                : "-";
            return ClockAhead.ToString(inv) + "/" + haste + "/" + held;
        }
    }
}
