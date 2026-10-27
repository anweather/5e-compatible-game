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
        /// <summary>The spell effects (spells.json "effect") of Hasten Time and Control Weather.</summary>
        public const string HasteEffect = "haste", WeatherEffect = "weather";

        /// <summary>The weathers Control Weather steps through, a step a cast; from anything else it clears first.</summary>
        public static readonly WeatherKind[] WeatherCycle = { WeatherKind.Clear, WeatherKind.Rain, WeatherKind.Snow, WeatherKind.Storm };

        /// <summary>Hasten Time: how many times as fast the day clock runs, and the ticks it has left; null at its own pace.</summary>
        public (int rate, int ticks)? Haste { get; set; }

        /// <summary>Ticks of day the clock has run ahead of Tick (Hasten Time). The clock reads ClockTick.</summary>
        public long ClockAhead { get; set; }

        /// <summary>Control Weather: the weather held, the tick it was cast and the tick it ends; null for the natural weather.</summary>
        public (WeatherKind kind, int from, int until)? WeatherSet { get; set; }

        /// <summary>The tick the day clock reads: Tick plus what Hasten Time has run it ahead.</summary>
        public long ClockTick => Tick + ClockAhead;

        /// <summary>Game minutes since midnight of day 1 (Sky.Minutes at ClockTick).</summary>
        public long Minutes => Sky.Minutes(Rules.Sim, ClockTick);

        /// <summary>ClockTick part way to the next tick (0..1), for a view that moves the sun between ticks.</summary>
        public double ClockSmooth(double fraction) => ClockTick + fraction * (Haste.HasValue ? Haste.Value.rate : 1);

        /// <summary>
        /// Hasten Time. While it runs, casting it again ends it (time settles back to its own pace,
        /// keeping the hours gained). Otherwise the clock runs Rate times as fast for Duration ticks,
        /// half again as long per charge level past a tap.
        /// </summary>
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

        /// <summary>
        /// Control Weather: the next weather in WeatherCycle after the one in force (held or natural),
        /// held from now for Duration ticks, half again as long per charge level past a tap.
        /// </summary>
        void HoldWeather(SpellDef s, int charge)
        {
            var now = WeatherSet.HasValue ? WeatherSet.Value.kind : Weather.Kind;
            var next = WeatherCycle[(Array.IndexOf(WeatherCycle, now) + 1) % WeatherCycle.Length];
            WeatherSet = (next, Tick, Tick + ChargedLength(Math.Max(1, s.Duration), charge));
            AddLog(next == WeatherKind.Clear ? "The sky clears at your word" : $"The sky turns to {next.ToString().ToLowerInvariant()} at your word", LogKind.Notable);
        }

        /// <summary>The weather this tick: the held weather while Control Weather lasts, else the seed's.</summary>
        WeatherNow SkyNow()
        {
            var natural = Sky.Weather(Rules.Sim, Seed, Tick);
            if (!WeatherSet.HasValue) return natural;
            var (kind, from, until) = WeatherSet.Value;
            return Sky.Held(Rules.Sim, Seed, kind, from, until, Tick, natural.Period);
        }

        /// <summary>At the start of a tick: held weather whose time is up gives way to the natural weather.</summary>
        void EndSetWeather()
        {
            if (!WeatherSet.HasValue || Tick < WeatherSet.Value.until) return;
            WeatherSet = null;
            AddLog("The weather goes its own way again");
        }

        /// <summary>At the end of a tick: a hastened clock runs Rate ticks of day for this one.</summary>
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

        /// <summary>
        /// A fingerprint (FNV-1a, 8 hex digits at most) of the time spells' state: the clock's lead,
        /// the haste and the held weather. For the netcode checksum beside CellHash, so a peer whose
        /// sky drifted shows up.
        /// </summary>
        public string SkyHash() => Fnv1a.Hash(SkyState());

        /// <summary>The time spells' state as text, e.g. "3420/20x9/1:100:820" ("-" for none).</summary>
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
