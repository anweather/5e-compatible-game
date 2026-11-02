// Live rule changes in a room (docs/RULES_MCP.md "Change modes", phase 3; ADR
// 0013). A RoomCommand asks to switch a mod on or revert one; every peer
// decides it itself, at the tick the host stamped it for: first the room's
// change mode (Room.cs, RoomPolicy), then ModValidator on the patch against the
// rules in force here, then the active mods composed again over the base rules
// in order. The same inputs give the same verdict everywhere, so a patch that
// fails the checks is refused on every peer, with the same sentence in the log.
// The room is in the save and in the checksum (CastHash).
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using Newtonsoft.Json.Linq;

namespace Chem
{
    public sealed partial class World
    {
        ModBudgets roomBudgets;

        /// <summary>The room this world is played in, or null for a world made without one.</summary>
        public RoomState Room { get; private set; }

        /// <summary>The room's base rules and its active mods composed over them (what Rules is); null without a room.</summary>
        public RuleSet RoomRules { get; private set; }

        /// <summary>A room command was decided, on this peer, as the log says. For the view: it never touches the world.</summary>
        public event Action<RoomCommand, RoomVerdict> RoomDecided;

        /// <summary>
        /// Makes this world a room, as it is made: the change mode, the host's name, the base rules
        /// every mod composes over, the budgets the checks use (rules/budgets.json) and the mods it
        /// starts with, if any (a rebuilt world keeps the ones on). Puts all that in force if it
        /// isn't already. A set of mods that doesn't compose throws ModException.
        /// </summary>
        public void OpenRoom(ChangeMode mode, string host, Rules baseRules, ModBudgets budgets, IReadOnlyList<RoomMod> mods = null)
        {
            if (Room != null) throw new InvalidOperationException("This world already has a room; its change mode is set when it is made.");
            if (baseRules == null) throw new ArgumentNullException(nameof(baseRules));
            if (budgets == null) throw new ArgumentNullException(nameof(budgets));
            var next = mods == null || mods.Count == 0 ? RuleSet.Plain(baseRules) : RuleSet.Compose(baseRules, mods.Select(m => new ActiveMod(m.Id, m.Json)));
            roomBudgets = budgets;
            Room = new RoomState { Mode = mode, Host = host ?? "" };
            if (mods != null) Room.Mods.AddRange(mods);
            RoomRules = next;
            if (Rules.Fingerprint != next.Fingerprint || next.Effective.ModBuildings.Count > 0) ApplyRules(next.Effective);
        }

        /// <summary>
        /// The base rules were reloaded (solo, rules/*.json saved in the Editor): the room's mods
        /// compose over the new base. A set that no longer composes throws ModException.
        /// </summary>
        public void RebaseRoom(Rules baseRules)
        {
            PutInForce(RuleSet.Compose(baseRules, Room.Mods.Select(m => new ActiveMod(m.Id, m.Json))));
        }

        /// <summary>
        /// Decides a room command, between ticks: switches the mod on or reverts it, or refuses it with
        /// one sentence. Logs the verdict either way and returns it.
        /// </summary>
        public RoomVerdict ApplyRoom(RoomCommand c)
        {
            if (Room == null) throw new InvalidOperationException("This world has no room to change; OpenRoom makes one.");
            FinishTick(); // a rule switch lands between whole ticks
            var verdict = c.Change == RoomChange.Revert ? Revert(c) : SwitchOn(c);
            AddLog(verdict.Line, verdict.Done ? LogKind.Notable : LogKind.Info);
            RoomDecided?.Invoke(c, verdict);
            return verdict;
        }

        RoomVerdict SwitchOn(RoomCommand c)
        {
            string title = TitleOf(c);
            RuleSet next = null;
            string why = RoomPolicy.Refusal(Room.Mode, Room.Host, c.Peer)
                ?? (c.Mod.Length == 0 ? "the mod has no id" : null)
                ?? (Room.Mods.Any(m => m.Id == c.Mod) ? $"{title} is already on; revert it first" : null)
                ?? Checks(title, c.Json)
                ?? Compose(Room.Mods.Select(m => new ActiveMod(m.Id, m.Json)).Append(new ActiveMod(c.Mod, c.Json)), $"{title} doesn't fit the mods on", out next)
                ?? SiteProblem(next.Effective, title);
            if (why != null) return Refused(c, why);
            Room.Mods.Add(new RoomMod { Id = c.Mod, Title = title, By = c.Peer, Tick = Tick, Json = c.Json });
            PutInForce(next);
            return new RoomVerdict(true, null, $"{c.Peer} switched on {title} ({RoomPolicy.Words(Room.Mode)} room)");
        }

        RoomVerdict Revert(RoomCommand c)
        {
            int at = Room.Mods.FindIndex(m => m.Id == c.Mod);
            string title = at >= 0 ? Room.Mods[at].Title : c.Mod;
            string named = c.Mod.Length == 0 ? "that mod" : c.Mod;
            RuleSet next = null;
            string why = RoomPolicy.Refusal(Room.Mode, Room.Host, c.Peer)
                ?? (at < 0 ? $"{named} isn't on in this room" : null)
                ?? Compose(Room.Mods.Where((m, i) => i != at).Select(m => new ActiveMod(m.Id, m.Json)), $"without {title} the other mods don't fit", out next)
                ?? OverlapProblem(next.Effective, $"without {title}");
            if (why != null) return Refused(c, why);
            Room.Mods.RemoveAt(at);
            PutInForce(next);
            return new RoomVerdict(true, null, $"{c.Peer} switched off {title} ({RoomPolicy.Words(Room.Mode)} room)");
        }

        static RoomVerdict Refused(RoomCommand c, string why) => new RoomVerdict(false, why, RoomPolicy.RefusedLine(c.Change, why));

        void PutInForce(RuleSet next)
        {
            RoomRules = next;
            ApplyRules(next.Effective);
        }

        /// <summary>The command's title, else the patch's own, else the mod's id.</summary>
        static string TitleOf(RoomCommand c)
        {
            if (!string.IsNullOrWhiteSpace(c.Title)) return c.Title.Trim();
            try
            {
                var t = ModPatch.Parse(c.Json).Title;
                if (!string.IsNullOrWhiteSpace(t)) return t.Trim();
            }
            catch (ModException)
            {
                // the checks say what is wrong with it
            }
            return c.Mod;
        }

        /// <summary>ModValidator on the patch against the rules in force here: null if it passes, else its first problem.</summary>
        string Checks(string title, string json)
        {
            IReadOnlyList<ModProblem> problems;
            try
            {
                problems = ModValidator.Validate(Rules, roomBudgets, json);
            }
            catch (Exception e) when (e is FormatException || e is InvalidCastException || e is ArgumentException || e is OverflowException)
            {
                return $"{title} can't be read as a mod";
            }
            if (problems.Count == 0) return null;
            string more = problems.Count == 1 ? "" : $" (and {problems.Count - 1} more)";
            return $"{title} doesn't pass the checks: {problems[0].Message.TrimEnd('.')}{more}";
        }

        /// <summary>The room's base rules with these mods over them, in order: null if they fit, else why not.</summary>
        string Compose(IEnumerable<ActiveMod> mods, string what, out RuleSet next)
        {
            try
            {
                next = RoomRules.WithMods(mods);
                return null;
            }
            catch (ModException e)
            {
                next = null;
                return $"{what}: {e.Errors.FirstOrDefault()?.TrimEnd('.')}";
            }
        }

        // ---------- the checksum ----------

        /// <summary>A fingerprint (FNV-1a) of the room: its mode, host and active mods. Part of CastHash, so in the checksum.</summary>
        public string RoomHash() => Fnv1a.Hash(RoomText());

        /// <summary>
        /// The room as text, "" without one (so worlds without a room hash as they did):
        /// "#room mode host" and then "|id:by:tick:patch hash" for each mod on, in order.
        /// </summary>
        public string RoomText()
        {
            if (Room == null) return "";
            var inv = CultureInfo.InvariantCulture;
            var sb = new StringBuilder("#room ");
            sb.Append(((int)Room.Mode).ToString(inv)).Append(' ').Append(Room.Host);
            foreach (var m in Room.Mods)
                sb.Append('|').Append(m.Id).Append(':').Append(m.By).Append(':').Append(m.Tick.ToString(inv)).Append(':').Append(Fnv1a.Hash(m.Json));
            return sb.ToString();
        }

        // ---------- the save ----------

        void SaveRoom(JObject save)
        {
            if (Room == null) return;
            save["room"] = new JObject
            {
                ["mode"] = (int)Room.Mode,
                ["host"] = Room.Host,
                ["base"] = RoomRules.Base.Fingerprint,
                ["mods"] = new JArray(Room.Mods.Select(m => new JArray(m.Id, m.Title, m.By, m.Tick, m.Json))),
            };
            SaveBuildings((JObject)save["room"]);
        }

        /// <summary>A saved room's rules: the base with its mods composed again, in order.</summary>
        static RuleSet SavedRoomRules(JObject room, Rules baseRules, ModBudgets budgets)
        {
            Expect(budgets != null, "a room, loaded without the budgets its mods are checked against");
            Expect((string)room["base"] == baseRules.Fingerprint, "a room saved over other base rules");
            try
            {
                return RuleSet.Compose(baseRules, (room["mods"] ?? new JArray()).Select(m => new ActiveMod((string)m[0], (string)m[4])));
            }
            catch (ModException e)
            {
                throw new SaveException("This save doesn't fit: its room's mods don't compose (" + string.Join("; ", e.Errors) + ")");
            }
        }

        void LoadRoom(JObject room, RuleSet rules, ModBudgets budgets)
        {
            roomBudgets = budgets;
            RoomRules = rules;
            Room = new RoomState { Mode = (ChangeMode)(int)room["mode"], Host = (string)room["host"] ?? "" };
            foreach (var m in room["mods"] ?? new JArray())
                Room.Mods.Add(new RoomMod { Id = (string)m[0], Title = (string)m[1], By = (string)m[2], Tick = (int)m[3], Json = (string)m[4] });
            LoadBuildings(room);
        }
    }
}
