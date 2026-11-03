// add_trigger (docs/design/events-and-placing.md 2c, 5): a triggers.json row in a draft, a rule
// that reacts to an event. "A meteor at every step I take" is add_trigger(id: meteor-steps,
// do: [{cast: meteor, at: ahead, by: 2, delay: 6}]), the asker's steps by default (J-067).
// list triggers and describe triggers read them; remove takes one back; simulate walks a loop
// when the draft has a step trigger (Simulator.Walk).
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Chem.Mcp
{
    public sealed partial class Workbench
    {
        const string TriggersTable = Rules.TriggersKey;

        /// <summary>add_trigger(): a trigger row in a draft, checked against the rules at once; the answer says how often it can fire.</summary>
        public string AddTrigger(string draftId, string id, string on, string who, string does, int? every, double? chance)
        {
            id = Required(id, "id");
            on = string.IsNullOrEmpty(on) ? "step" : on.ToLowerInvariant();
            if (!Rules.TriggerEvents.Contains(on)) throw new RulesToolException($"A trigger reacts to {string.Join(" or ", Rules.TriggerEvents)} so far (a player stepping onto another cell), not {on}.");
            who = string.IsNullOrEmpty(who) || who == "me" ? Here?.Asking()?.Player ?? throw new RulesToolException("No player is in the game to name, so give who: a player's name, or each for everyone.") : who;
            var actions = Value(Required(does, "do"));
            if (actions is JObject one) actions = new JArray(one);
            if (!(actions is JArray list) || list.Count == 0 || list.Any(a => !(a is JObject)))
                throw new RulesToolException("do is a list of one or two actions, each {\"cast\": spell, \"at\": here, ahead, behind or around, \"by\": cells, \"delay\": ticks} or {\"place\": {...as a spell's place}, \"at\": ...}.");
            if (every.HasValue && every.Value < TriggerDef.MinEvery)
                throw new RulesToolException($"every is at least {TriggerDef.MinEvery} ticks: a trigger fires at most twice a second for a player, so a meteor at every step of a run is already two a second.");
            if (chance.HasValue && !(chance.Value > 0 && chance.Value <= 1)) throw new RulesToolException("chance is above 0 and at most 1; leave it out for every time.");
            var draft = DraftOf(draftId);
            var row = new JObject { ["on"] = on, ["who"] = who };
            if (every.HasValue) row["every"] = every.Value;
            if (chance.HasValue) row["chance"] = chance.Value;
            row["do"] = list;
            var table = Section(draft, TriggersTable);
            var before = table[id]?.DeepClone();
            table[id] = row;
            try
            {
                RulesWith(draft.Patch.ToString(Formatting.None));
            }
            catch (RulesToolException)
            {
                if (before != null) table[id] = before;
                else table.Remove(id);
                Tidy(draft.Patch);
                throw;
            }
            draft.Changed.Add($"{TriggersTable} {id}");
            int gap = Math.Max(TriggerDef.MinEvery, every ?? TriggerDef.MinEvery);
            double perSecond = InForce.Sim.TicksPerSecond / (double)gap;
            return draft.Show() + "\n" + string.Format(CultureInfo.InvariantCulture,
                "{0} fires on {1}'s steps at most once every {2} ticks ({3:0.#} a second at a run), and at most {4} triggers fire a tick in the room. Run simulate on it: it walks a loop and counts what fired.",
                id, who == TriggerDef.Each ? "everyone" : who, gap, perSecond, (int)Budgets.Number("triggers.maxPerTick", World.DefaultMaxFiringsPerTick));
        }

        // list triggers: each trigger in force, in words.
        IEnumerable<string> TriggerLines() =>
            ((Raw.TryGetValue(TriggersTable, out var t) ? t : null) as JObject ?? new JObject()).Properties().Select(p => $"{p.Name}: {TriggerWords(p.Value)}");

        static string TriggerWords(JToken t)
        {
            string who = Str(t["who"]) == TriggerDef.Each ? "anyone's" : Str(t["who"]) + "'s";
            var does = (t["do"] as JArray ?? new JArray()).Select(ActionWords);
            string chance = t["chance"] == null ? "" : $", at a chance of {Num((double)t["chance"])}";
            return string.Format(CultureInfo.InvariantCulture, "on {0} {1}, at most every {2} ticks{3}: {4}",
                who, Str(t["on"]) ?? "step", (int?)t["every"] ?? TriggerDef.MinEvery, chance, string.Join(", then ", does));
        }

        static string ActionWords(JToken a)
        {
            string at = Str(a["at"]) ?? "here";
            string where = at switch
            {
                "ahead" or "behind" => $"{(int?)a["by"] ?? 0} cells {at}",
                "around" => $"within {(int?)a["within"] ?? 0} cells",
                _ => "where they stand",
            };
            if (a["cast"] != null)
                return $"cast {Str(a["cast"])} {where}" + ((int?)a["delay"] > 0 ? $", landing {(int)a["delay"]} ticks later" : "");
            var p = a["place"];
            string what = Str(p?["thing"]) ?? Str(p?["animal"]) ?? Str(p?["terrain"]) ?? Str(p?["cover"]) ?? "?";
            return $"place {(int?)p?["count"] ?? 1} {what} {where}";
        }

        // apply's words for a trigger of the draft.
        static string TriggerChange(Draft draft, string id)
        {
            var row = draft.Patch[TriggersTable]?[id];
            return row is JObject ? $"a trigger, {id}: {TriggerWords(row)}" : $"the trigger {id} taken out";
        }

        /// <summary>Whether a patch has a step trigger, so simulate walks a loop.</summary>
        static bool HasStepTrigger(JObject patch) =>
            (patch[TriggersTable] as JObject)?.PropertyValues().Any(t => t is JObject && (Str(t["on"]) ?? "step") == "step") == true;
    }
}
