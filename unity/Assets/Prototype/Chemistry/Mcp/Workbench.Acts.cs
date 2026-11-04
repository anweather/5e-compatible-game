// act and undo_act (docs/design/world-acts.md): something that happens once, now, for everyone, with no mod. "Add 20 trees
// near me" asked twice is two acts and an unchanged mod list. It picks cells as scatter does, from the world it reads, and
// sends them as a room command (World.Acts.cs), so what's a rule stays a mod and what's an event stays out of the list.
// And superseded mods: a change the chat makes that says everything an earlier one of the same player's said
// (Green fire, then Blue fire) takes that one off, so they don't pile up, each hiding the last.
using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;

#pragma warning disable S107 // a tool's parameters are its schema (RulesTools)

namespace Chem.Mcp
{
    public sealed partial class Workbench
    {
        // Each act gets its own seed unless asked for one, so a second one near the first picks afresh.
        int actSeed;

        /// <summary>act(): count of a terrain, cover, thing or animal over open cells near something, or a spell cast there, now.</summary>
        public string Act(string what, int? count, string near, int? within, int? spacing, string side, string edge, int? seed, string[] onto, int? lasts, string peer = null)
        {
            what = Required(what, "what");
            string kind = ActKind(what) ?? throw new RulesToolException($"There is no terrain, cover, thing, animal or spell called {what}; list shows what there is.");
            int wanted = count ?? 1;
            double most = MostOf(kind);
            if (wanted < 1 || wanted > most) throw new RulesToolException(FormattableString.Invariant($"count is 1 to {most} for a {kind}."));
            if (kind == World.ActCast && InForce.Spell(what).Level > Budgets.Number("triggers.maxLevel", 7))
                throw new RulesToolException(FormattableString.Invariant($"{what} is past level {Budgets.Number("triggers.maxLevel", 7)}, the highest an act can cast."));
            if (lasts < 0 || lasts > Budgets.Number("place.maxLasts", 21600)) throw new RulesToolException("lasts is in ticks, up to an hour.");
            var picked = PickNear(wanted, string.IsNullOrWhiteSpace(near) ? "me" : near, within ?? 12, spacing ?? 0, side, edge, seed ?? ++actSeed, kind == World.ActCast ? null : onto, out string name);
            if (picked.Count == 0) return FormattableString.Invariant($"No open cell within {within ?? 12} cells of {name}; try further out, or somewhere else.");
            var ask = new JObject { ["kind"] = kind, ["what"] = what, ["cells"] = new JArray(picked.Select(c => new JArray(c.x, c.y))) };
            if (lasts > 0) ask["lasts"] = lasts.Value;
            if (onto?.Length > 0) ask["onto"] = new JArray(onto.Cast<object>().ToArray());
            string said = Decide(RoomCommand.Act(PeerOf(peer), ask.ToString(Newtonsoft.Json.Formatting.None)));
            if (Refused(said)) return said;
            string undo = kind == World.ActThing || kind == World.ActTerrain || kind == World.ActCover ? " undo_act takes it back." : "";
            return FormattableString.Invariant($"{said}\nNear {name}. It's done, for everyone, and it isn't a mod: nothing to apply or save.{undo}");
        }

        /// <summary>undo_act(): takes back an act that laid ground or placed things, the last of the player's if none is named.</summary>
        public string UndoAct(int? act, string peer = null) =>
            Decide(RoomCommand.Undo(PeerOf(peer), act));

        string ActKind(string what)
        {
            if (InForce.Terrain.ContainsKey(what)) return World.ActTerrain;
            if (InForce.Covers.ContainsKey(what)) return World.ActCover;
            if (InForce.Entities.ContainsKey(what) || InForce.Items.ContainsKey(what)) return World.ActThing;
            if (InForce.Fauna.Species(what) != null) return World.ActAnimal;
            return InForce.Spell(what) != null ? World.ActCast : null;
        }

        double MostOf(string kind)
        {
            switch (kind)
            {
                case World.ActCast: return Budgets.Number("triggers.maxPerTick", 4);
                case World.ActAnimal: return Budgets.Number("place.maxSummoned", 24);
                case World.ActThing: return Budgets.Number("place.maxLive", 64);
                default: return Budgets.Number("lay.maxCells", 256);
            }
        }

        // ---------- superseded mods ----------

        // The earlier mods this one says all of: the chat's own, by the same player. Each goes, and the line says so.
        string Superseded(string json, string id, string peer)
        {
            var now = Now();
            if (now == null) return "";
            var says = Says(json);
            if (says.Count == 0) return "";
            var ours = new HashSet<string>(drafts.Values.Select(d => d.OnAs).Where(on => on != null), StringComparer.Ordinal);
            var gone = now.Room.Mods.Where(m => m.Id != id && m.By == peer && ours.Contains(m.Id)).Where(m => Says(m.Json) is var old && old.Count > 0 && old.IsSubsetOf(says))
                .Select(m => (m.Id, m.Title)).ToList();
            var taken = new List<string>();
            foreach (var (mod, title) in gone)
                if (!Refused(Decide(RoomCommand.Revert(peer, mod)))) taken.Add(title);
            return taken.Count == 0 ? "" : $"\nIt says all that {string.Join(" and ", taken)} said, so that's off now: one change, not a pile.";
        }

        // Everything a mod says, a value to a line: where it sits, and for a list that isn't a colour or rows by id what's
        // in it, as two mods adding to one list add different things.
        static HashSet<string> Says(string json)
        {
            var lines = new HashSet<string>(StringComparer.Ordinal);
            Say(ModPatch.Parse(json).Json, "", lines);
            return lines;
        }

        static void Say(JToken token, string at, HashSet<string> lines)
        {
            if (token is JObject o)
            {
                foreach (var p in o.Properties()) Say(p.Value, at + "/" + p.Name, lines);
                return;
            }
            if (token is JArray rows && rows.Count > 0 && rows.All(r => r is JObject row && row["id"] != null))
            {
                // Rows by id, as a spell's are: one's look is the same line in two mods.
                foreach (var row in rows) Say(row, at + "/" + (string)row["id"], lines);
                return;
            }
            bool numbers = token is JArray list && list.All(v => v.Type == JTokenType.Integer || v.Type == JTokenType.Float);
            lines.Add(token is JArray && !numbers ? at + "=" + token.ToString(Newtonsoft.Json.Formatting.None) : at);
        }
    }
}
