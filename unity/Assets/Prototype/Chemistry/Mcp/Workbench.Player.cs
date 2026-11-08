// set_player (docs/RULES_MCP.md "Player stats"): a player's speed, size, jump height or jumps
// before landing, in a draft's sim player, for the player asking ("me", their slot), a slot, or
// everyone. "Make me faster" is set_player(speed, 2); "quadruple jump" is set_player(jumps, 4).
// A value past the bounds in budgets.json sim (player.speed and the rest) is cut to the bound
// and the reply says so, so the player hears what they got in the same turn.
using System;
using System.Globalization;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace Chem.Mcp
{
    public sealed partial class Workbench
    {
        const string PlayersTable = "players";

        /// <summary>set_player(): one stat of the player asking, a slot's, or everyone's, within the bounds; "normal" takes it back.</summary>
        public string SetPlayer(string draftId, string stat, string value, string who)
        {
            stat = Required(stat, "stat").Trim().ToLowerInvariant();
            if (!PlayerParams.Stats.Contains(stat))
                throw new RulesToolException($"A player's stats are {string.Join(", ", PlayerParams.Stats)}: speed, size and jump are times the wizard's own, jumps how many before landing (2 is a double jump).");
            string slot = PlayerSlot(who);
            string path = slot == null ? $"player.{stat}" : $"player.{PlayerParams.ByKey}.{slot}.{stat}";
            string whose = slot == null ? "every player" : $"the player in slot {slot}";
            var draft = DraftOf(draftId);
            if (string.Equals(value?.Trim(), "normal", StringComparison.OrdinalIgnoreCase))
            {
                // A slot's goes back to everyone's by a null, which the merge keeps, so it undoes a mod under this one too.
                if (slot == null) SetPath(Section(draft, "sim"), path, Normal(stat));
                else SlotRow(draft, slot)[stat] = JValue.CreateNull();
                draft.Changed.Add($"sim {path}");
                return draft.Show() + $"\n{whose}'s {stat} goes back to {(slot == null ? "the wizard's own" : "everyone's")} once it's applied.";
            }
            var (most, least) = PlayerBound(stat);
            if (!double.TryParse(Required(value, "value"), NumberStyles.Float, CultureInfo.InvariantCulture, out double asked) || double.IsNaN(asked) || double.IsInfinity(asked))
                throw new RulesToolException($"value is a number: {StatHelp(stat)}; or normal to take it back.");
            double v = Math.Max(least, Math.Min(most, stat == "jumps" ? Math.Round(asked) : asked));
            SetPath(Section(draft, "sim"), path, stat == "jumps" ? ((int)v).ToString(CultureInfo.InvariantCulture) : Num(v));
            draft.Changed.Add($"sim {path}");
            string cut = Math.Abs(v - asked) > 1e-9
                ? $" {Num(asked)} is past what a player may have, so it's {Num(v)}: {stat} is {Num(least)} to {Num(most)}. Tell the player."
                : "";
            return draft.Show() + $"\n{whose}: {StatWords(stat, v)}, once it's applied.{cut}";
        }

        // who: the slot it's for, or null for everyone. "me" (or nothing) is the player asking's.
        string PlayerSlot(string who)
        {
            string w = string.IsNullOrWhiteSpace(who) ? "me" : who.Trim().ToLowerInvariant();
            if (w == "everyone" || w == "each" || w == "all") return null;
            if (w == "me") return MyPlayerSlot().ToString(CultureInfo.InvariantCulture);
            if (uint.TryParse(w, NumberStyles.None, CultureInfo.InvariantCulture, out uint n)) return n.ToString(CultureInfo.InvariantCulture);
            throw new RulesToolException("who is me (the player asking), a player's slot (0 the host, then each in join order), or everyone.");
        }

        static JObject SlotRow(Draft draft, string slot)
        {
            var sim = Section(draft, "sim");
            if (!(sim["player"] is JObject player)) sim["player"] = player = new JObject();
            if (!(player[PlayerParams.ByKey] is JObject by)) player[PlayerParams.ByKey] = by = new JObject();
            if (!(by[slot] is JObject row)) by[slot] = row = new JObject();
            return row;
        }

        int MyPlayerSlot() =>
            Here?.Asking()?.Slot ?? throw new RulesToolException("No player is in the game to change, so give who: a player's slot, or everyone.");

        // A stat's bounds from budgets.json sim, as (most, least).
        (double most, double least) PlayerBound(string stat)
        {
            var b = Budgets.SimBounds.Where(x => x.path == "player." + stat).Select(x => ((double, double)?)(x.max, x.min)).FirstOrDefault();
            return b ?? (stat == "jumps" ? (8, 1) : (1, 1));
        }

        static string Normal(string stat) => stat == "jumps" ? "2" : "1";

        string StatHelp(string stat)
        {
            var (most, least) = PlayerBound(stat);
            return stat == "jumps"
                ? $"jumps before landing, {Num(least)} to {Num(most)} (2 is a double jump, 4 a quadruple jump)"
                : $"times the wizard's own {stat}, {Num(least)} to {Num(most)}";
        }

        static string StatWords(string stat, double v) => stat switch
        {
            "speed" => $"moves at {Num(v)} times the wizard's speed, walking and sprinting",
            "size" => $"is {Num(v)} times the wizard's size",
            "jump" => $"jumps {Num(v)} times as high, every jump",
            _ => $"has {Num(v)} jump{(Math.Abs(v - 1) < 1e-9 ? "" : "s")} before landing",
        };

        // list players: everyone's stats and each slot's own, in force.
        string PlayerLines()
        {
            var p = InForce.Sim.Player;
            var lines = p.Lines().ToList();
            string bounds = string.Join(", ", PlayerParams.Stats.Select(s => { var (most, least) = PlayerBound(s); return $"{s} {Num(least)} to {Num(most)}"; }));
            return (lines.Count == 0 ? "Every player is as made: their own speed, size and jump, and a double jump." : string.Join("\n", lines)) +
                $"\nset_player changes one ({bounds}): speed, size and jump are times the wizard's own, jumps how many before landing.";
        }
    }
}
