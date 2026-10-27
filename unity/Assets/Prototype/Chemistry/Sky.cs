// The time of day and the weather, worked out from the tick and the world's
// seed alone: no state of their own, so every peer (and a late joiner, who
// replays from tick 0) has the same sky at the same tick. Port of
// sandbox/src/sky.ts. What the weather does to cells is World.Weather.cs.
using System;

namespace Chem
{
    /// <summary>The kinds of weather, in the fixed order sim.json's weather.weights are read in.</summary>
    public enum WeatherKind { Clear, Cloudy, Windy, Fog, Rain, Storm, Snow }

    /// <summary>The weather at one tick: its kind, how strong it is (0..100, ramping in and out) and the wind it brings.</summary>
    public readonly struct WeatherNow
    {
        public WeatherNow(WeatherKind kind, int strength, int windDir, int period, int meteors = 0)
        {
            Kind = kind;
            Strength = strength;
            WindDir = windDir;
            Period = period;
            Meteors = meteors;
        }

        public WeatherKind Kind { get; }
        public int Strength { get; }
        /// <summary>The prevailing wind's direction (0..3, N E S W), or -1 for none.</summary>
        public int WindDir { get; }
        /// <summary>Which stretch of weather.periodMinutes this is, from 0.</summary>
        public int Period { get; }
        /// <summary>How thick a meteor shower is, 0..100: a clear stretch may be one (weather.meteors.showerChance).</summary>
        public int Meteors { get; }

        /// <summary>How hard it rains, 0..100 (rain and storms).</summary>
        public int Rain => Kind == WeatherKind.Rain || Kind == WeatherKind.Storm ? Strength : 0;
        public int Fog => Kind == WeatherKind.Fog ? Strength : 0;
        /// <summary>How hard it snows, 0..100.</summary>
        public int Snow => Kind == WeatherKind.Snow ? Strength : 0;
        /// <summary>How much the weather holds fire back, 0..100: rain fully, snow by half.</summary>
        public int Wet => Rain + Snow / 2;
    }

    public static class Sky
    {
        public const int MinutesPerDay = 24 * 60;
        static readonly int Kinds = Enum.GetValues(typeof(WeatherKind)).Length;

        /// <summary>Ticks in one stretch of sim.json minutes (whole ticks, at least 1).</summary>
        public static int Ticks(SimParams p, double minutes) => Math.Max(1, (int)Math.Floor(minutes * 60 * p.TicksPerSecond));

        /// <summary>Game minutes since midnight of day 1 at this tick (day.startHour at tick 0; a day lasts day.lengthMinutes).</summary>
        public static long Minutes(SimParams p, long tick) =>
            (long)Math.Floor(p.Day.StartHour * 60) + tick * MinutesPerDay / Ticks(p, p.Day.LengthMinutes);

        /// <summary>Minutes() for a tick with a fraction, for a view that moves the sun between ticks.</summary>
        public static double MinutesSmooth(SimParams p, double tick) =>
            Math.Floor(p.Day.StartHour * 60) + tick * MinutesPerDay / Ticks(p, p.Day.LengthMinutes);

        public static int Day(long minutes) => (int)(minutes / MinutesPerDay) + 1;
        public static int MinuteOfDay(long minutes) => (int)(minutes % MinutesPerDay);

        /// <summary>The weather at this tick.</summary>
        public static WeatherNow Weather(SimParams p, uint seed, int tick)
        {
            var wp = p.Weather;
            int length = Ticks(p, wp.PeriodMinutes);
            int period = tick / length, at = tick % length;
            var kind = KindOf(p, seed, period);
            int ramp = Math.Max(1, Ticks(p, wp.RampMinutes));
            if (kind == WeatherKind.Clear)
            {
                // A clear stretch after the calm may be a meteor shower, ramping in and out within it.
                bool shower = period >= wp.CalmPeriods && Hash(seed, period, 3) / 4294967296.0 < wp.ShowerChance;
                return new WeatherNow(kind, 0, -1, period, shower ? Math.Min(100, Math.Min(at, length - at) * 100 / ramp) : 0);
            }
            // Ramp in from the start and out toward the end, unless the stretch next door is the same weather.
            int fromStart = period > 0 && KindOf(p, seed, period - 1) == kind ? ramp : at;
            int toEnd = KindOf(p, seed, period + 1) == kind ? ramp : length - at;
            int strength = Math.Min(100, Math.Min(fromStart, toEnd) * 100 / ramp);
            bool windy = (kind == WeatherKind.Windy || kind == WeatherKind.Storm) && strength >= 50;
            return new WeatherNow(kind, strength, windy ? (int)(Hash(seed, period, 2) >> 30) : -1, period);
        }

        /// <summary>
        /// Weather set by Control Weather at tick <paramref name="from"/> until <paramref name="until"/>,
        /// at this tick: ramping in from the cast and out toward the end over weather.rampMinutes, a
        /// storm's wind from the seed and the cast's tick. <paramref name="period"/> is the natural
        /// weather's stretch, carried through for a view.
        /// </summary>
        public static WeatherNow Held(SimParams p, uint seed, WeatherKind kind, int from, int until, int tick, int period)
        {
            if (kind == WeatherKind.Clear) return new WeatherNow(kind, 0, -1, period);
            int ramp = Math.Max(1, Ticks(p, p.Weather.RampMinutes));
            int strength = Math.Max(0, Math.Min(100, Math.Min(tick - from, until - tick) * 100 / ramp));
            bool windy = (kind == WeatherKind.Windy || kind == WeatherKind.Storm) && strength >= 50;
            return new WeatherNow(kind, strength, windy ? (int)(Hash(seed, from, 4) >> 30) : -1, period);
        }

        /// <summary>
        /// The weather of one stretch: clear for the first weather.calmPeriods, then picked by
        /// weather.weights with the seed's hash of the stretch.
        /// </summary>
        public static WeatherKind KindOf(SimParams p, uint seed, int period)
        {
            var wp = p.Weather;
            if (period < wp.CalmPeriods) return WeatherKind.Clear;
            double total = 0;
            for (int k = 0; k < Kinds; k++) total += Math.Max(0, wp.Weights[k]);
            if (total <= 0) return WeatherKind.Clear;
            double pick = Hash(seed, period, 1) / 4294967296.0 * total;
            for (int k = 0; k < Kinds; k++)
            {
                pick -= Math.Max(0, wp.Weights[k]);
                if (pick < 0) return (WeatherKind)k;
            }
            return WeatherKind.Clear;
        }

        /// <summary>A 32-bit hash of the seed, a stretch and a salt (murmur3's finaliser), the same as sky.ts's hash.</summary>
        public static uint Hash(uint seed, int period, uint salt)
        {
            unchecked
            {
                uint h = seed ^ ((uint)period * 0x9e3779b1u) ^ (salt * 0x85ebca77u);
                h ^= h >> 16;
                h *= 0x85ebca6bu;
                h ^= h >> 13;
                h *= 0xc2b2ae35u;
                h ^= h >> 16;
                return h;
            }
        }
    }
}
