// The time of day and the weather, worked out from the tick and the world's
// seed alone: no state of their own, so every peer (and a late joiner, who
// replays from tick 0) has the same sky at the same tick. Port of
// sandbox/src/sky.ts. What the weather does to cells is World.Weather.cs.
using System;
using System.Globalization;

namespace Chem
{
    public enum WeatherKind { Clear, Cloudy, Windy, Fog, Rain, Storm, Snow }

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
        public int WindDir { get; }
        public int Period { get; }
        public int Meteors { get; }

        public int Rain => Kind == WeatherKind.Rain || Kind == WeatherKind.Storm ? Strength : 0;
        public int Fog => Kind == WeatherKind.Fog ? Strength : 0;
        public int Snow => Kind == WeatherKind.Snow ? Strength : 0;
        public int Wet => Rain + Snow / 2;
    }

    public enum LightLevel { Dark, Dim, Bright }

    public static class Sky
    {
        public const int MinutesPerDay = 24 * 60;
        static readonly int Kinds = Enum.GetValues(typeof(WeatherKind)).Length;

        public static int Ticks(SimParams p, double minutes) => Math.Max(1, (int)Math.Floor(minutes * 60 * p.TicksPerSecond));

        public static long Minutes(SimParams p, long tick) =>
            (long)Math.Floor(p.Day.StartHour * 60) + tick * MinutesPerDay / Ticks(p, p.Day.LengthMinutes);

        public static double MinutesSmooth(SimParams p, double tick) =>
            Math.Floor(p.Day.StartHour * 60) + tick * MinutesPerDay / Ticks(p, p.Day.LengthMinutes);

        public static string ClockWords(int minuteOfDay) =>
            (minuteOfDay / 60).ToString("00", CultureInfo.InvariantCulture) + ":" + (minuteOfDay % 60).ToString("00", CultureInfo.InvariantCulture);

        public static string WeatherName(WeatherKind kind) => kind.ToString().ToLowerInvariant();

        public static WeatherKind? ParseWeather(string name)
        {
            for (int k = 0; k < Kinds; k++)
                if (string.Equals(WeatherName((WeatherKind)k), (name ?? "").Trim(), StringComparison.OrdinalIgnoreCase)) return (WeatherKind)k;
            return null;
        }

        public static int Day(long minutes) => (int)(minutes / MinutesPerDay) + 1;
        public static int MinuteOfDay(long minutes) => (int)(minutes % MinutesPerDay);

        public static LightLevel Daylight(DayParams d, long minutes)
        {
            int m = MinuteOfDay(minutes);
            if (InHours(d.DawnTo, d.DuskFrom, m)) return LightLevel.Bright;
            if (InHours(d.DuskTo, d.DawnFrom, m)) return LightLevel.Dark;
            return LightLevel.Dim;
        }

        public static bool InHours(double from, double to, int minuteOfDay)
        {
            int a = (int)Math.Floor(from * 60), b = (int)Math.Floor(to * 60);
            return a <= b ? minuteOfDay >= a && minuteOfDay < b : minuteOfDay >= a || minuteOfDay < b;
        }

        public static WeatherNow Weather(SimParams p, uint seed, int tick)
        {
            var wp = p.Weather;
            int length = Ticks(p, wp.PeriodMinutes);
            int period = tick / length, at = tick % length;
            var kind = KindOf(p, seed, period);
            int ramp = Math.Max(1, Ticks(p, wp.RampMinutes));
            if (kind == WeatherKind.Clear)
            {
                bool shower = period >= wp.CalmPeriods && Hash(seed, period, 3) / 4294967296.0 < wp.ShowerChance;
                return new WeatherNow(kind, 0, -1, period, shower ? Math.Min(100, Math.Min(at, length - at) * 100 / ramp) : 0);
            }
            int fromStart = period > 0 && KindOf(p, seed, period - 1) == kind ? ramp : at;
            int toEnd = KindOf(p, seed, period + 1) == kind ? ramp : length - at;
            int strength = Math.Min(100, Math.Min(fromStart, toEnd) * 100 / ramp);
            bool windy = (kind == WeatherKind.Windy || kind == WeatherKind.Storm) && strength >= 50;
            return new WeatherNow(kind, strength, windy ? (int)(Hash(seed, period, 2) >> 30) : -1, period);
        }

        public static WeatherNow Held(SimParams p, uint seed, WeatherKind kind, int from, int until, int tick, int period)
        {
            if (kind == WeatherKind.Clear) return new WeatherNow(kind, 0, -1, period);
            int ramp = Math.Max(1, Ticks(p, p.Weather.RampMinutes));
            int strength = Math.Max(0, Math.Min(100, Math.Min(tick - from, until - tick) * 100 / ramp));
            bool windy = (kind == WeatherKind.Windy || kind == WeatherKind.Storm) && strength >= 50;
            return new WeatherNow(kind, strength, windy ? (int)(Hash(seed, from, 4) >> 30) : -1, period);
        }

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
