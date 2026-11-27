// Triggers (docs/design/events-and-placing.md 2c): rules/triggers.json, rows by id, each
// reacting to an event with one or two actions. The base rules have none; a mod adds rows
// ("triggers": {"<id>": {...}}, a null taking one out). The rows are indexed by event kind at
// each rule switch (D9 seam 2: an integer kind in hot state, the table in the rules), so a step
// with no trigger costs one array read. An empty table stays out of the fingerprint, so rules
// without triggers fingerprint as they did. World.Triggers.cs fires them.
//
// Not a port: the sandbox has no triggers.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace Chem
{
    public enum TriggerEvent
    {
        Step,
        Holed,
        Called,
        Tick,
    }

    public enum TriggerAt
    {
        Here,
        Ahead,
        Behind,
        Around,
    }

    public sealed class TriggerAction
    {
        public string Cast;
        public PlaceDef Place;
        public TriggerAt At;
        public int By, Within;
        public int Delay;
        public JObject Act;
    }

    public sealed class TriggerDef
    {
        public const int MinEvery = 3;
        public const int MinTickEvery = 12;
        public const string Each = "each";

        public string Id;
        public TriggerEvent On;
        public string Who;
        public int Every = MinEvery;
        public double Chance = 1;
        public IReadOnlyList<TriggerAction> Do = Array.Empty<TriggerAction>();

        public const string SoloPlayer = "solo";

        public bool IsFor(string player, string peer) =>
            Who == Each || string.Equals(Who, player, StringComparison.Ordinal) || string.Equals(Who, peer, StringComparison.Ordinal);

        public static string PeerOf(string player)
        {
            if (player == SoloPlayer) return "Player 1";
            if (player != null && player.Length > 1 && player[0] == 'p'
                && ulong.TryParse(player.AsSpan(1), NumberStyles.None, CultureInfo.InvariantCulture, out ulong client) && client < ulong.MaxValue)
                return "Player " + (client + 1).ToString(CultureInfo.InvariantCulture);
            return player;
        }
    }

    public sealed partial class Rules
    {
        public const string TriggersKey = "triggers";

        internal static readonly string[] TriggerEvents = { "step", "holed", "called", "tick" };
        internal static readonly string[] TriggerAts = { "here", "ahead", "behind", "around" };
        static readonly string[] TriggerFields = { "on", "who", "every", "chance", "do" };
        static readonly string[] ActionFields = { "cast", "place", "at", "by", "within", "delay" };
        static readonly string[] ActFields = { "act", "count", "within", "spacing", "seed", "onto", "lasts", "profile", "radius", "rise", "rough", "along" };

        IReadOnlyList<TriggerDef>[] triggersOn = Array.Empty<IReadOnlyList<TriggerDef>>();

        public IReadOnlyList<TriggerDef> Triggers { get; private set; } = Array.Empty<TriggerDef>();

        public IReadOnlyList<TriggerDef> TriggersOn(TriggerEvent on) =>
            (int)on < triggersOn.Length ? triggersOn[(int)on] : Array.Empty<TriggerDef>();

        void ParseTriggers(JObject d)
        {
            if (!(d[TriggersKey] is JObject rows) || rows.Count == 0) return;
            Triggers = rows.Properties().Where(p => p.Value is JObject).Select(p => ParseTrigger(p.Name, (JObject)p.Value)).ToList();
            var kinds = (TriggerEvent[])Enum.GetValues(typeof(TriggerEvent));
            triggersOn = kinds.Select(k => (IReadOnlyList<TriggerDef>)Triggers.Where(t => t.On == k).ToList()).ToArray();
        }

        static TriggerDef ParseTrigger(string id, JObject o) => new TriggerDef
        {
            Id = id,
            On = (TriggerEvent)Math.Max(0, Array.IndexOf(TriggerEvents, Str(o["on"]))),
            Who = Str(o["who"]),
            Every = Math.Max(Str(o["on"]) == "tick" ? TriggerDef.MinTickEvery : TriggerDef.MinEvery, Int(o["every"]) ?? TriggerDef.MinEvery),
            Chance = Num(o["chance"]) ?? 1,
            Do = (o["do"] as JArray)?.OfType<JObject>().Select(ParseAction).ToList() ?? (IReadOnlyList<TriggerAction>)Array.Empty<TriggerAction>(),
        };

        static TriggerAction ParseAction(JObject a) => new TriggerAction
        {
            Cast = Str(a["cast"]),
            Place = ParsePlace(a["place"]),
            At = (TriggerAt)Math.Max(0, Array.IndexOf(TriggerAts, Str(a["at"]) ?? "here")),
            By = Int(a["by"]) ?? 0,
            Within = Int(a["within"]) ?? 0,
            Delay = Int(a["delay"]) ?? 0,
            Act = a["act"] != null ? a : null,
        };

        static void ApplyTriggers(JObject r, JObject p)
        {
            if (!(p[TriggersKey] is JObject given)) return;
            if (!(r[TriggersKey] is JObject rows)) r[TriggersKey] = rows = new JObject();
            foreach (var row in given.Properties())
            {
                if (row.Value.Type == JTokenType.Null) rows.Remove(row.Name);
                else rows[row.Name] = MergedEntry(rows[row.Name], row.Value);
            }
        }

        sealed partial class Checker
        {
            public void Triggers()
            {
                if (!(r[TriggersKey] is JObject rows)) return;
                foreach (var row in rows.Properties())
                {
                    string who = $"trigger {row.Name}";
                    if (row.Value is JObject t) TriggerRow(who, t);
                    else Errors.Add($"{who} must be an object");
                }
            }

            void TriggerRow(string who, JObject t)
            {
                foreach (var f in t.Properties().Where(f => !TriggerFields.Contains(f.Name))) Errors.Add($"{who}: {f.Name} isn't a field of a trigger; it takes {string.Join(", ", TriggerFields)}");
                if (!(t["on"] is JValue on && TriggerEvents.Contains(Text(on)))) Errors.Add($"{who}: on is the event, one of {string.Join(", ", TriggerEvents)}");
                if (string.IsNullOrEmpty(Text(t["who"]))) Errors.Add($"{who}: who is a player's name, or \"{TriggerDef.Each}\" for everyone");
                if (t["every"] != null && !(IsWholeNumber(t["every"]) && (double)t["every"] >= TriggerDef.MinEvery))
                    Errors.Add($"{who}: every is the ticks between firings, a whole number from {TriggerDef.MinEvery}");
                if (t["chance"] != null && !(IsNumber(t["chance"]) && (double)t["chance"] > 0 && (double)t["chance"] <= 1)) Errors.Add($"{who}: chance is above 0 and at most 1");
                if (Text(t["on"]) == "tick" && t["every"] != null && IsWholeNumber(t["every"]) && (double)t["every"] < TriggerDef.MinTickEvery)
                    Errors.Add($"{who}: a tick trigger fires at most every {TriggerDef.MinTickEvery} ticks (2 seconds) for a player, so every is {TriggerDef.MinTickEvery} or more");
                bool called = Text(t["on"]) == "called";
                if (called && (t["every"] != null || t["chance"] != null)) Errors.Add($"{who}: a called trigger fires when it's called, so it takes no every or chance");
                TriggerActions(who, t["do"], called);
            }

            void TriggerActions(string who, JToken does, bool called)
            {
                if (!(does is JArray actions) || actions.Count == 0) Errors.Add($"{who} needs \"do\": a list of actions, each {(called ? "an act" : "a cast or a place")}");
                else
                {
                    for (int i = 0; i < actions.Count; i++)
                    {
                        if (called) CalledAction($"{who}: action {i + 1}", actions[i]);
                        else TriggerAction($"{who}: action {i + 1}", actions[i]);
                    }
                }
            }

            void CalledAction(string who, JToken a)
            {
                if (!(a is JObject o))
                {
                    Errors.Add($"{who} must be an object");
                    return;
                }
                foreach (var f in o.Properties().Where(f => !ActFields.Contains(f.Name))) Errors.Add($"{who}: {f.Name} isn't a field of an act; it takes {string.Join(", ", ActFields)}");
                if (string.IsNullOrEmpty(Text(o["act"]))) Errors.Add($"{who} needs act: what act would be asked for");
                foreach (var k in new[] { "count", "within", "spacing", "seed", "lasts", "radius", "rise", "rough" })
                    if (o[k] != null && !IsWholeNumber(o[k])) Errors.Add($"{who}: {k} must be a whole number");
            }

            void TriggerAction(string who, JToken a)
            {
                if (!(a is JObject o))
                {
                    Errors.Add($"{who} must be an object");
                    return;
                }
                foreach (var f in o.Properties().Where(f => !ActionFields.Contains(f.Name))) Errors.Add($"{who}: {f.Name} isn't a field of an action; it takes {string.Join(", ", ActionFields)}");
                if ((o["cast"] == null) == (o["place"] == null)) Errors.Add($"{who} needs exactly one of cast (a spell) or place");
                if (o["cast"] != null && !(r["spells"] as JArray ?? new JArray()).Any(s => Text(s["id"]) == Text(o["cast"]))) Errors.Add($"{who}: unknown spell {Show(o["cast"])}");
                if (o["place"] != null) PlaceRefs(o["place"], $"{who}: place");
                if (o["at"] != null && !TriggerAts.Contains(Text(o["at"]))) Errors.Add($"{who}: at is one of {string.Join(", ", TriggerAts)}");
                foreach (var k in new[] { "by", "within", "delay" })
                    if (o[k] != null && !(IsWholeNumber(o[k]) && (double)o[k] >= 0)) Errors.Add($"{who}: {k} must be a whole number from 0");
                if (o["delay"] != null && o["place"] != null) Errors.Add($"{who}: only a cast has a delay");
            }
        }
    }
}
