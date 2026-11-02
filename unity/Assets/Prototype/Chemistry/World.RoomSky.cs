// The clock and the weather as room commands (RoomChange.SetTime, SetWeather): the
// MCP's set_time and set_weather. They ride in the tick batch like a mod's switch, so
// the same change mode decides them on every peer at the same tick, and what they set
// is what the time spells set (World.Time.cs): the clock's lead and the held weather,
// which are in the save and the checksum (SkyHash).
using System;
using System.Globalization;

namespace Chem
{
    public sealed partial class World
    {
        // The clock only runs forward: to the next time the day reaches that minute.
        RoomVerdict SetTime(RoomCommand c)
        {
            int minute = -1;
            string why = RoomPolicy.Refusal(Room.Mode, Room.Host, c.Peer)
                ?? (int.TryParse(c.Json, NumberStyles.None, CultureInfo.InvariantCulture, out minute) && minute < Sky.MinutesPerDay ? null : "that isn't a time of day");
            if (why != null) return Refused(c, why);
            ClockAhead += TicksUntilMinute(minute);
            return new RoomVerdict(true, null, $"{c.Peer} set the time to {Sky.ClockWords(minute)} ({RoomPolicy.Words(Room.Mode)} room)");
        }

        // The ticks of day from now to the first tick that reads that minute; 0 if it reads it now.
        long TicksUntilMinute(int minute)
        {
            long now = Minutes;
            long ahead = (minute - Sky.MinuteOfDay(now) + Sky.MinutesPerDay) % Sky.MinutesPerDay;
            if (ahead == 0) return 0;
            var p = Rules.Sim;
            long fromStart = now + ahead - (long)Math.Floor(p.Day.StartHour * 60);
            long day = Sky.Ticks(p, p.Day.LengthMinutes);
            return (fromStart * day + Sky.MinutesPerDay - 1) / Sky.MinutesPerDay - ClockTick;
        }

        RoomVerdict SetWeather(RoomCommand c)
        {
            string room = RoomPolicy.Words(Room.Mode);
            string why = RoomPolicy.Refusal(Room.Mode, Room.Host, c.Peer);
            if (why == null && c.Json == RoomCommand.NaturalWeather)
            {
                WeatherSet = null;
                return new RoomVerdict(true, null, $"{c.Peer} let the weather go its own way ({room} room)");
            }
            var parts = c.Json.Split(':');
            var kind = parts.Length == 2 ? Sky.ParseWeather(parts[0]) : null;
            int minutes = 0;
            why = why ?? (kind.HasValue && int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out minutes) && minutes >= 1 && minutes <= RoomPolicy.MaxHoldMinutes
                ? null
                : "that isn't a weather and a time to hold it");
            if (why != null) return Refused(c, why);
            WeatherSet = (kind.Value, Tick, Tick + Sky.Ticks(Rules.Sim, minutes));
            return new RoomVerdict(true, null, $"{c.Peer} set the weather to {Sky.WeatherName(kind.Value)} for {minutes} minute{(minutes == 1 ? "" : "s")} ({room} room)");
        }
    }
}
