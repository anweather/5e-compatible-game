// The clock and the weather: time and weather read them, set_time and set_weather go
// through the room as commands (World.RoomSky.cs), so every peer does them at the same tick.
using System.Globalization;
using System.Linq;

namespace Chem.Mcp
{
    /// <summary>
    /// A running game that can tell the tools what's on and take every room command. It answers
    /// Now from the main thread (WorldNow.Of), null between scenes, and sends a command on as
    /// command.As(its own player), of whatever kind: RoomIntent has to carry RoomCommand.Change.
    /// </summary>
    public interface ILiveDesk : IRoomDesk
    {
        WorldNow Now();
    }

    public sealed partial class Workbench
    {
        /// <summary>The clock in words.</summary>
        public string TimeNow() => TimeWords(SkyNow());

        /// <summary>The weather in words.</summary>
        public string WeatherNow() => WeatherWords(SkyNow());

        WorldNow SkyNow()
        {
            if (Blind) throw new RulesToolException("This game can't tell the tools its clock and weather yet; look at the sky, or ask the player.");
            return Now() ?? throw new RulesToolException(NoWorld);
        }

        string TimeWords(WorldNow now)
        {
            var day = InForce.Sim.Day;
            string light = Sky.Daylight(day, now.Minutes).ToString().ToLowerInvariant();
            string haste = now.Haste.HasValue ? $" Hasten Time is running it {now.Haste.Value.rate} times as fast." : "";
            return $"Time: {Sky.ClockWords(Sky.MinuteOfDay(now.Minutes))} on day {Sky.Day(now.Minutes)}, {(light == "dark" ? "dark" : light + " light")}. " +
                $"A day lasts {Num(day.LengthMinutes)} minutes of play; dawn is {Num(day.DawnFrom)} to {Num(day.DawnTo)}, dusk {Num(day.DuskFrom)} to {Num(day.DuskTo)}.{haste}";
        }

        string WeatherWords(WorldNow now)
        {
            var p = InForce.Sim;
            var w = now.Weather;
            string kind = Sky.WeatherName(w.Kind);
            string wind = w.WindDir >= 0 ? $", wind blowing {new[] { "north", "east", "south", "west" }[w.WindDir]}" : "";
            string strength = w.Kind == WeatherKind.Clear ? "" : $" at {w.Strength}%";
            string shower = w.Meteors > 0 ? $", a meteor shower at {w.Meteors}%" : "";
            if (now.Held.HasValue)
            {
                int left = now.Held.Value.until - now.Tick;
                return $"Weather: {Sky.WeatherName(now.Held.Value.kind)} held, now {kind}{strength}{wind}{shower}; {Num(left / (double)Sky.Ticks(p, 1))} minutes of play left before it goes its own way.";
            }
            int length = Sky.Ticks(p, p.Weather.PeriodMinutes);
            int inTicks = length - now.Tick % length;
            return $"Weather: {kind}{strength}{wind}{shower}, as the world has it; in {Num(inTicks / (double)Sky.Ticks(p, 1))} minutes of play it turns to {Sky.WeatherName(Sky.KindOf(p, now.Seed, w.Period + 1))}.";
        }

        /// <summary>Moves the clock on to a time of day, through the room; it says what happened.</summary>
        public string SetTime(string time, string peer = null)
        {
            SkyNow();
            string said = Decide(RoomCommand.SetTime(PeerOf(peer), MinuteOf(Required(time, "time"))));
            return Refused(said) ? said : said + "\n" + TimeNow();
        }

        /// <summary>Holds a weather for so many minutes of play, or "natural" lets it go its own way, through the room; it says what happened.</summary>
        public string SetWeather(string weather, int? minutes = null, string peer = null)
        {
            SkyNow();
            string name = Required(weather, "weather").ToLowerInvariant();
            RoomCommand command;
            if (name == RoomCommand.NaturalWeather) command = RoomCommand.SetWeather(PeerOf(peer), null);
            else
            {
                var kind = Sky.ParseWeather(name) ?? throw new RulesToolException($"{weather} isn't a weather; the weathers are {string.Join(", ", System.Enum.GetValues(typeof(WeatherKind)).Cast<WeatherKind>().Select(Sky.WeatherName))}, or natural to let it go its own way.");
                int hold = minutes ?? DefaultHoldMinutes;
                if (hold < 1 || hold > RoomPolicy.MaxHoldMinutes) throw new RulesToolException($"A weather holds for 1 to {RoomPolicy.MaxHoldMinutes} minutes of play, not {hold}.");
                command = RoomCommand.SetWeather(PeerOf(peer), kind, hold);
            }
            string said = Decide(command);
            return Refused(said) ? said : said + "\n" + WeatherNow();
        }

        const int DefaultHoldMinutes = 8;

        static bool Refused(string said) => said.Contains(" refused:", System.StringComparison.Ordinal);

        // "09:30", "21", "noon"... as a minute of the day, or the sentence saying what it takes.
        int MinuteOf(string time)
        {
            var day = InForce.Sim.Day;
            string t = time.Trim().ToLowerInvariant();
            switch (t)
            {
                case "noon": case "day": case "daytime": return 12 * 60;
                case "midnight": case "night": case "nighttime": return 0;
                case "dawn": case "sunrise": return (int)(day.DawnFrom * 60);
                case "dusk": case "sunset": return (int)(day.DuskFrom * 60);
            }
            var parts = t.Split(':');
            int minute = 0;
            if (parts.Length <= 2 && int.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out int hour) && hour < 24
                && (parts.Length == 1 || (int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out minute) && minute < 60)))
                return hour * 60 + minute;
            throw new RulesToolException($"{time} isn't a time of day; say 09:30, 21, noon, midnight, dawn or dusk.");
        }
    }
}
