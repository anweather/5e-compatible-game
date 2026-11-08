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
            if (!Rules.TriggerEvents.Contains(on)) throw new RulesToolException($"A trigger reacts to step (a player stepping onto another cell), holed (a golf ball going in the cup), tick (time passing: a shower) or called (a named act), not {on}.");
            // Whoever sinks it, unless a player is named: a ball is everyone's.
            if (string.IsNullOrEmpty(who) && on != "step" && on != "tick") who = TriggerDef.Each;
            who = string.IsNullOrEmpty(who) || who == "me" ? AskerForTrigger() : who;
            var list = TriggerActions(does, on == "called");
            CheckTriggerOdds(on, every, chance);
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
            if (on == "called") return draft.Show() + $"\n{id} is a named act: once this mod is on, call_act fires it at a place, each row as act would do it.";
            if (on == "tick") return draft.Show() + "\n" + TickWords(id, who, every, chance);
            if (on != "step") return draft.Show() + $"\n{id} fires when a golf ball goes in the cup, at the cup's cell. act places a ball and a cup; simulate can't play a round.";
            int gap = Math.Max(TriggerDef.MinEvery, every ?? TriggerDef.MinEvery);
            double perSecond = InForce.Sim.TicksPerSecond / (double)gap;
            return draft.Show() + "\n" + string.Format(CultureInfo.InvariantCulture,
                "{0} fires on {1}'s steps at most once every {2} ticks ({3:0.#} a second at a run), and at most {4} triggers fire a tick in the room. Run simulate on it: it walks a loop and counts what fired.",
                id, who == TriggerDef.Each ? "everyone" : who, gap, perSecond, (int)Budgets.Number("triggers.maxPerTick", World.DefaultMaxFiringsPerTick));
        }

        // The asker's name, which matches their steps alone and online (TriggerDef.IsFor), or the reason there's none.
        string AskerForTrigger() =>
            Here?.Asking()?.Player ?? throw new RulesToolException("No player is in the game to name, so give who: a player's name, or each for everyone.");

        // do: one action or a list of them, each an object.
        static JArray TriggerActions(string does, bool called)
        {
            var actions = Value(Required(does, "do"));
            if (actions is JObject one) actions = new JArray(one);
            if (called && actions is JArray rows && rows.Count > 0 && rows.All(a => a is JObject)) return rows;
            if (!(actions is JArray list) || list.Count == 0 || list.Any(a => !(a is JObject)))
                throw new RulesToolException("do is a list of one or two actions, each {\"cast\": spell, \"at\": here, ahead, behind or around, \"by\": cells, \"delay\": ticks} or {\"place\": {...as a spell's place}, \"at\": ...}.");
            return list;
        }

        // What a shower comes to: how often round whom, and that it goes on until it's reverted.
        string TickWords(string id, string who, int? every, double? chance)
        {
            int gap = Math.Max(TriggerDef.MinTickEvery, every ?? TriggerDef.MinTickEvery);
            double perMinute = InForce.Sim.TicksPerSecond * 60.0 / gap * (chance ?? 1);
            return string.Format(CultureInfo.InvariantCulture,
                "{0} fires round {1} every {2} ticks ({3:0.#} seconds; about {4:0} a minute each), at most {5} triggers a tick in the room, for as long as the mod is on: revert it to stop. Run simulate on it: it counts what fired.",
                id, who == TriggerDef.Each ? "every player" : who, gap, gap / (double)InForce.Sim.TicksPerSecond, perMinute, (int)Budgets.Number("triggers.maxPerTick", World.DefaultMaxFiringsPerTick));
        }

        static void CheckTriggerOdds(string on, int? every, double? chance)
        {
            if (on == "tick" && every.HasValue && every.Value < TriggerDef.MinTickEvery)
                throw new RulesToolException($"A tick trigger's every is at least {TriggerDef.MinTickEvery} ticks (2 seconds) for a player: a meteor every 2 seconds is already a shower, and a room fires at most 4 triggers a tick.");
            if (every.HasValue && every.Value < TriggerDef.MinEvery)
                throw new RulesToolException($"every is at least {TriggerDef.MinEvery} ticks: a trigger fires at most twice a second for a player, so a meteor at every step of a run is already two a second.");
            if (chance.HasValue && !(chance.Value > 0 && chance.Value <= 1)) throw new RulesToolException("chance is above 0 and at most 1; leave it out for every time.");
        }

        // list triggers: each trigger in force, in words.
        IEnumerable<string> TriggerLines() =>
            ((Raw.TryGetValue(TriggersTable, out var t) ? t : null) as JObject ?? new JObject()).Properties().Select(p => $"{p.Name}: {TriggerWords(p.Value)}");

        static string TriggerWords(JToken t)
        {
            string who = Str(t["who"]) == TriggerDef.Each ? "anyone's" : Str(t["who"]) + "'s";
            var does = (t["do"] as JArray ?? new JArray()).Select(ActionWords);
            if (Str(t["on"]) == "called") return $"a named act, for {(Str(t["who"]) == TriggerDef.Each ? "anyone" : Str(t["who"]))} to call: {string.Join(", then ", does)}";
            string chance = t["chance"] == null ? "" : $", at a chance of {Num((double)t["chance"])}";
            if (Str(t["on"]) == "tick")
                return string.Format(CultureInfo.InvariantCulture, "a shower round {0}, every {1} ticks{2}: {3}",
                    Str(t["who"]) == TriggerDef.Each ? "every player" : Str(t["who"]), Math.Max(TriggerDef.MinTickEvery, (int?)t["every"] ?? TriggerDef.MinTickEvery), chance, string.Join(", then ", does));
            return string.Format(CultureInfo.InvariantCulture, "on {0} {1}, at most every {2} ticks{3}: {4}",
                who, Str(t["on"]) ?? "step", (int?)t["every"] ?? TriggerDef.MinEvery, chance, string.Join(", then ", does));
        }

        static string ActionWords(JToken a)
        {
            if (a["act"] != null) return $"act {Str(a["act"])}" + ((int?)a["count"] > 1 ? $" x{(int)a["count"]}" : "");
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

        /// <summary>Whether a patch has a step or tick trigger, so simulate walks a loop (a shower needs a player standing).</summary>
        static bool HasStepTrigger(JObject patch) =>
            (patch[TriggersTable] as JObject)?.PropertyValues().Any(t => t is JObject && ((Str(t["on"]) ?? "step") == "step" || Str(t["on"]) == "tick")) == true;
    }
}
