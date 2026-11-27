// A room's data (docs/RULES_MCP.md "Change modes", phase 3; ADR 0013): the
// change mode chosen when the world is made, the host, and the ordered list of
// mods on, with who added each and when. The command that asks to switch a mod
// on or revert one, and the verdict every peer reaches on it. World.Room.cs is
// the system. Strings and whole numbers in a fixed order, in the save and the
// checksum, so every peer in lockstep agrees.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace Chem
{
    public enum ChangeMode { Open, HostOnly, Locked }

    public enum RoomChange { Apply, Revert, SetTime, SetWeather, Found, Act, Undo }

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

        public static RoomCommand Act(string peer, string json) => new RoomCommand(RoomChange.Act, peer, ActMod, json);

        public static RoomCommand Undo(string peer, int? act = null) =>
            new RoomCommand(RoomChange.Undo, peer, ActMod, act?.ToString(CultureInfo.InvariantCulture));

        public static RoomCommand SetTime(string peer, int minuteOfDay) =>
            new RoomCommand(RoomChange.SetTime, peer, TimeMod, minuteOfDay.ToString(CultureInfo.InvariantCulture));

        public static RoomCommand SetWeather(string peer, WeatherKind? kind, int minutes = 0) =>
            new RoomCommand(RoomChange.SetWeather, peer, WeatherMod, kind.HasValue ? Sky.WeatherName(kind.Value) + ":" + minutes.ToString(CultureInfo.InvariantCulture) : NaturalWeather);

        public static RoomCommand Found(string peer, ThingId thing) =>
            new RoomCommand(RoomChange.Found, peer, thing.Value.ToString(CultureInfo.InvariantCulture));

        public RoomCommand As(string peer) => new RoomCommand(Change, peer, Mod, Json, Title);

        const string TimeMod = "time", WeatherMod = "weather";
        public const string ActMod = "act";
        internal const string NaturalWeather = "natural";

        public RoomChange Change { get; }
        public string Peer { get; }
        public string Mod { get; }
        public string Json { get; }
        public string Title { get; }
    }

    public sealed class RoomMod
    {
        public string Id, Title, By, Json;
        public int Tick;
    }

    public sealed class RoomState
    {
        public ChangeMode Mode;
        public string Host = "";
        public List<RoomMod> Mods { get; } = new List<RoomMod>();
        public SortedSet<ulong> Found { get; } = new SortedSet<ulong>();
        public List<RetiredMaterial> Retired { get; } = new List<RetiredMaterial>();
        public List<RetiredRow> RetiredRows { get; } = new List<RetiredRow>();
        public List<RoomMix> Mixes { get; } = new List<RoomMix>();
    }

    public sealed class RetiredRow
    {
        public string Kind, Id, Mod;
        public int Tick;
    }

    public sealed class RetiredMaterial
    {
        public string Table, Id, Into, Mod;
        public int Tick;
    }

    public readonly struct RoomVerdict
    {
        public RoomVerdict(bool done, string reason, string line)
        {
            Done = done;
            Reason = reason;
            Line = line;
        }

        public bool Done { get; }
        public string Reason { get; }
        public string Line { get; }
    }

    public static class RoomPolicy
    {
        public const int MaxHoldMinutes = 60;

        public static string Words(ChangeMode mode) => mode switch
        {
            ChangeMode.HostOnly => "Host-only",
            ChangeMode.Locked => "Locked",
            _ => "Open",
        };

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

        public static string RefusedLine(RoomChange change, string why)
        {
            string what = change switch
            {
                RoomChange.Revert => "Revert",
                RoomChange.SetTime => "Set time",
                RoomChange.SetWeather => "Set weather",
                RoomChange.Act => "Act",
                RoomChange.Undo => "Undo",
                _ => "Switch on",
            };
            return $"{what} refused: {why}";
        }

        public static string Refusal(ChangeMode mode, string host, string peer)
        {
            if (string.IsNullOrEmpty(peer)) return "the change doesn't say which peer asked for it";
            if (mode == ChangeMode.Locked) return "this room is locked, so its rules stay as it started";
            if (mode == ChangeMode.HostOnly && peer != host) return "only the host can change this room";
            return null;
        }
    }

    public sealed class RoomSwitchPrep
    {
        readonly Rules rules;
        readonly RuleSet roomRules;
        readonly ModBudgets budgets;
        readonly IReadOnlyDictionary<string, string> owners;
        readonly string json;

        internal RoomSwitchPrep(Rules rules, RuleSet roomRules, ModBudgets budgets, IReadOnlyDictionary<string, string> owners, string json)
        {
            this.rules = rules;
            this.roomRules = roomRules;
            this.budgets = budgets;
            this.owners = owners;
            this.json = json;
        }

        public bool Ran { get; private set; }
        public bool Used { get; internal set; }

        internal IReadOnlyList<ModProblem> Problems { get; private set; }
        internal Rules Composed { get; private set; }
        internal bool Unreadable { get; private set; }

        public void Run()
        {
            if (Ran) return;
            try
            {
                Problems = ModValidator.Validate(rules, budgets, json, owners, out var composed);
                Composed = composed;
            }
            catch (Exception e) when (e is FormatException || e is InvalidCastException || e is ArgumentException || e is OverflowException || e is InvalidOperationException)
            {
                Unreadable = true;
            }
            Ran = true;
        }

        internal bool Fits(Rules now, RuleSet room, ModBudgets roomBudgets, IReadOnlyDictionary<string, string> ownersNow, string patch) =>
            Ran && ReferenceEquals(rules, now) && ReferenceEquals(roomRules, room) && ReferenceEquals(budgets, roomBudgets) && json == patch
            && owners.Count == ownersNow.Count && owners.All(o => ownersNow.TryGetValue(o.Key, out var v) && v == o.Value);
    }
}
