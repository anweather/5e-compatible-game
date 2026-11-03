// A room's data (docs/RULES_MCP.md "Change modes", phase 3; ADR 0013): the
// change mode chosen when the world is made, the host, and the ordered list of
// mods on, with who added each and when. The command that asks to switch a mod
// on or revert one, and the verdict every peer reaches on it. World.Room.cs is
// the system. Strings and whole numbers in a fixed order, in the save and the
// checksum, so every peer in lockstep agrees.
using System;
using System.Collections.Generic;
using System.Globalization;

namespace Chem
{
    /// <summary>How a room takes live rule changes: anyone's, the host's only, or none.</summary>
    public enum ChangeMode { Open, HostOnly, Locked }

    /// <summary>What a room command asks: switch a mod on, revert (take off) one that is on, move the clock or the weather (World.RoomSky.cs), or note that someone picked up a thing a mod added (World.Things.cs).</summary>
    public enum RoomChange { Apply, Revert, SetTime, SetWeather, Found }

    /// <summary>
    /// Asks the room to switch a mod on or revert one, naming the peer who asked. Stamped like a
    /// cast (TickBatch.Room); every peer decides it the same at that tick (World.ApplyRoom).
    /// </summary>
    public readonly struct RoomCommand
    {
        public RoomCommand(RoomChange change, string peer, string mod, string json = null, string title = null)
        {
            Change = change;
            Peer = peer ?? "";
            Mod = mod ?? "";
            Json = json ?? "";
            Title = title;
        }

        public static RoomCommand Apply(string peer, string mod, string json, string title = null) => new RoomCommand(RoomChange.Apply, peer, mod, json, title);
        public static RoomCommand Revert(string peer, string mod) => new RoomCommand(RoomChange.Revert, peer, mod);

        /// <summary>Moves the clock on to this minute of the day (0 to 1439).</summary>
        public static RoomCommand SetTime(string peer, int minuteOfDay) =>
            new RoomCommand(RoomChange.SetTime, peer, TimeMod, minuteOfDay.ToString(CultureInfo.InvariantCulture));

        /// <summary>Holds a weather for so many minutes of play; null gives the weather back to its own way.</summary>
        public static RoomCommand SetWeather(string peer, WeatherKind? kind, int minutes = 0) =>
            new RoomCommand(RoomChange.SetWeather, peer, WeatherMod, kind.HasValue ? Sky.WeatherName(kind.Value) + ":" + minutes.ToString(CultureInfo.InvariantCulture) : NaturalWeather);

        /// <summary>This peer picked up this thing. The host sends it with the grant; the change mode has no say, as it changes nothing.</summary>
        public static RoomCommand Found(string peer, ThingId thing) =>
            new RoomCommand(RoomChange.Found, peer, thing.Value.ToString(CultureInfo.InvariantCulture));

        /// <summary>The same command, asked by this peer: a host names who really asked.</summary>
        public RoomCommand As(string peer) => new RoomCommand(Change, peer, Mod, Json, Title);

        const string TimeMod = "time", WeatherMod = "weather";
        internal const string NaturalWeather = "natural";

        public RoomChange Change { get; }
        /// <summary>The peer who asked, by the name players see.</summary>
        public string Peer { get; }
        /// <summary>The mod's id (its file name in rules/mods/ without .json, or a draft's id).</summary>
        public string Mod { get; }
        /// <summary>The patch to switch on (bare or {"title", "patch"}); empty for a revert; for the clock or the weather, what to set (World.RoomSky.cs).</summary>
        public string Json { get; }
        /// <summary>The title players see; null takes the patch's own, else the id.</summary>
        public string Title { get; }
    }

    /// <summary>A mod on in a room: what it is, who added it and the tick it went on.</summary>
    public sealed class RoomMod
    {
        public string Id, Title, By, Json;
        /// <summary>The world's tick when it switched on (it is in force from the next).</summary>
        public int Tick;
    }

    /// <summary>The room a world is played in: set when the world is made, saved with it.</summary>
    public sealed class RoomState
    {
        public ChangeMode Mode;
        /// <summary>The host's name: in a Host-only room, the one peer whose changes go in.</summary>
        public string Host = "";
        /// <summary>The mods on, in the order they compose over the base rules.</summary>
        public List<RoomMod> Mods { get; } = new List<RoomMod>();
        /// <summary>The things a mod added that someone has picked up, by ThingId: the first pick-up is logged, once.</summary>
        public SortedSet<ulong> Found { get; } = new SortedSet<ulong>();
        /// <summary>The temporary materials gone from the room, lapsed or reverted (World.Lapse.cs), oldest first: what a cell still naming one turns into.</summary>
        public List<RetiredMaterial> Retired { get; } = new List<RetiredMaterial>();
        /// <summary>The other rows gone from the room with their mods (a style, a building row, a species; World.Lapse.cs), oldest first: who added each, for a refusal.</summary>
        public List<RetiredRow> RetiredRows { get; } = new List<RetiredRow>();
    }

    /// <summary>A row that went with its mod and isn't a material: the word for its table (ModAdd.Kind), its id, its mod and the tick.</summary>
    public sealed class RetiredRow
    {
        public string Kind, Id, Mod;
        public int Tick;
    }

    /// <summary>A temporary material that went with its mod: its table and id, what a cell of it turns into (null: none, or dirt for a terrain), its mod and the tick.</summary>
    public sealed class RetiredMaterial
    {
        public string Table, Id, Into, Mod;
        public int Tick;
    }

    /// <summary>What a room command came to: done or refused, and the log line saying so.</summary>
    public readonly struct RoomVerdict
    {
        public RoomVerdict(bool done, string reason, string line)
        {
            Done = done;
            Reason = reason;
            Line = line;
        }

        public bool Done { get; }
        /// <summary>Why it was refused, one sentence without its capital (null when done).</summary>
        public string Reason { get; }
        /// <summary>The world log's line: "Ana switched on Big fireball (Open room)".</summary>
        public string Line { get; }
    }

    public static class RoomPolicy
    {
        /// <summary>The longest a command may hold a weather, in minutes of play.</summary>
        public const int MaxHoldMinutes = 60;

        /// <summary>The mode as the log writes it before "room": Open, Host-only, Locked.</summary>
        public static string Words(ChangeMode mode) => mode switch
        {
            ChangeMode.HostOnly => "Host-only",
            ChangeMode.Locked => "Locked",
            _ => "Open",
        };

        /// <summary>The mode from its name (open, host-only or hostonly, locked), any case; null if none.</summary>
        public static ChangeMode? Parse(string name)
        {
            switch ((name ?? "").Trim().Replace("-", "", StringComparison.Ordinal).Replace(" ", "", StringComparison.Ordinal).ToLowerInvariant())
            {
                case "open": return ChangeMode.Open;
                case "hostonly": return ChangeMode.HostOnly;
                case "locked": return ChangeMode.Locked;
                default: return null;
            }
        }

        /// <summary>The log's line for a refused command: "Revert refused: only the host can change this room".</summary>
        public static string RefusedLine(RoomChange change, string why)
        {
            string what = change switch
            {
                RoomChange.Revert => "Revert",
                RoomChange.SetTime => "Set time",
                RoomChange.SetWeather => "Set weather",
                _ => "Switch on",
            };
            return $"{what} refused: {why}";
        }

        /// <summary>
        /// Whether the mode lets this peer change the room: null if so, else the reason. The host
        /// asks it before scheduling a change, and every peer asks it again at the tick, so a
        /// refusal is the same everywhere.
        /// </summary>
        public static string Refusal(ChangeMode mode, string host, string peer)
        {
            if (string.IsNullOrEmpty(peer)) return "the change doesn't say which peer asked for it";
            if (mode == ChangeMode.Locked) return "this room is locked, so its rules stay as it started";
            if (mode == ChangeMode.HostOnly && peer != host) return "only the host can change this room";
            return null;
        }
    }
}
