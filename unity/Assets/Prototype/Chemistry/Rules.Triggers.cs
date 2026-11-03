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
using System.Linq;
using Newtonsoft.Json.Linq;

namespace Chem
{
    /// <summary>What a trigger reacts to. Only a step so far; each later one is a place in the core that already runs on a command.</summary>
    public enum TriggerEvent
    {
        /// <summary>A player's stand moves to another cell (not a first stand, not while downed).</summary>
        Step,
    }

    /// <summary>Where an action lands, from the player whose event it was.</summary>
    public enum TriggerAt
    {
        /// <summary>The player's cell.</summary>
        Here,
        /// <summary>By cells on along the way the player moved.</summary>
        Ahead,
        /// <summary>By cells back along it.</summary>
        Behind,
        /// <summary>A cell within Within of the player, picked by Placement.Pick.</summary>
        Around,
    }

    /// <summary>One action of a trigger: a cast of a spell, or a place (as a spell's), at a cell worked out from the player.</summary>
    public sealed class TriggerAction
    {
        /// <summary>The spell cast, or null for a place.</summary>
        public string Cast;
        /// <summary>What's placed, or null for a cast.</summary>
        public PlaceDef Place;
        public TriggerAt At;
        /// <summary>Cells ahead or behind; Within: the cells round the player an "around" picks from.</summary>
        public int By, Within;
        /// <summary>Ticks before a cast lands, a target mark glowing until then (as Falling Star); 0 as the spell has it.</summary>
        public int Delay;
    }

    /// <summary>A row of triggers.json: on an event, by whom, how often and how likely, and what it does.</summary>
    public sealed class TriggerDef
    {
        /// <summary>The fewest ticks between two firings of one trigger for one player (J-067): a meteor at every step of a run is already two a second.</summary>
        public const int MinEvery = 3;
        /// <summary>A trigger's who for every player's events.</summary>
        public const string Each = "each";

        public string Id;
        public TriggerEvent On;
        /// <summary>The player whose events fire it (the name casts use), or Each.</summary>
        public string Who;
        /// <summary>Ticks between firings, per player; never under MinEvery.</summary>
        public int Every = MinEvery;
        /// <summary>The chance it fires on an event, 0 to 1, from WorldHash (no draw from the world's generator).</summary>
        public double Chance = 1;
        public IReadOnlyList<TriggerAction> Do = Array.Empty<TriggerAction>();
    }

    public sealed partial class Rules
    {
        /// <summary>Where the rule document keeps the triggers (rules/triggers.json), rows by id.</summary>
        public const string TriggersKey = "triggers";

        internal static readonly string[] TriggerEvents = { "step" };
        internal static readonly string[] TriggerAts = { "here", "ahead", "behind", "around" };
        static readonly string[] TriggerFields = { "on", "who", "every", "chance", "do" };
        static readonly string[] ActionFields = { "cast", "place", "at", "by", "within", "delay" };

        IReadOnlyList<TriggerDef>[] triggersOn = Array.Empty<IReadOnlyList<TriggerDef>>();

        /// <summary>Every trigger, in the order the table holds them.</summary>
        public IReadOnlyList<TriggerDef> Triggers { get; private set; } = Array.Empty<TriggerDef>();

        /// <summary>The triggers on an event, in table order; empty (one array read) with none.</summary>
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
            On = TriggerEvent.Step,
            Who = Str(o["who"]),
            Every = Math.Max(TriggerDef.MinEvery, Int(o["every"]) ?? TriggerDef.MinEvery),
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
        };

        // A mod's rows over the table: a row changes only the fields it names, a null takes it out.
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
                    if (!(row.Value is JObject t))
                    {
                        Errors.Add($"{who} must be an object");
                        continue;
                    }
                    foreach (var f in t.Properties().Where(f => !TriggerFields.Contains(f.Name))) Errors.Add($"{who}: {f.Name} isn't a field of a trigger; it takes {string.Join(", ", TriggerFields)}");
                    if (!(t["on"] is JValue on && TriggerEvents.Contains(Text(on)))) Errors.Add($"{who}: on is the event, one of {string.Join(", ", TriggerEvents)}");
                    if (string.IsNullOrEmpty(Text(t["who"]))) Errors.Add($"{who}: who is a player's name, or \"{TriggerDef.Each}\" for everyone");
                    if (t["every"] != null && !(IsWholeNumber(t["every"]) && (double)t["every"] >= TriggerDef.MinEvery))
                        Errors.Add($"{who}: every is the ticks between firings, a whole number from {TriggerDef.MinEvery}");
                    if (t["chance"] != null && !(IsNumber(t["chance"]) && (double)t["chance"] > 0 && (double)t["chance"] <= 1)) Errors.Add($"{who}: chance is above 0 and at most 1");
                    if (!(t["do"] is JArray actions) || actions.Count == 0) Errors.Add($"{who} needs \"do\": a list of actions, each a cast or a place");
                    else for (int i = 0; i < actions.Count; i++) TriggerAction($"{who}: action {i + 1}", actions[i]);
                }
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
