// Live rule changes in a room (docs/RULES_MCP.md "Change modes", phase 3; ADR
// 0013). A RoomCommand asks to switch a mod on or revert one; every peer
// decides it itself, at the tick the host stamped it for: first the room's
// change mode (Room.cs, RoomPolicy), then ModValidator on the patch against the
// rules in force here, then the active mods composed again over the base rules
// in order. The same inputs give the same verdict everywhere, so a patch that
// fails the checks is refused on every peer, with the same sentence in the log.
// The room is in the save and in the checksum (CastHash). A command can also move
// the clock or the weather (World.RoomSky.cs), under the same change mode.
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

        public RoomState Room { get; private set; }

        public RuleSet RoomRules { get; private set; }

        public event Action<RoomCommand, RoomVerdict> RoomDecided;

        public void AwaitRoom(ModBudgets budgets) => roomBudgets = budgets ?? throw new ArgumentNullException(nameof(budgets));

        public void OpenRoom(ChangeMode mode, string host, Rules baseRules, ModBudgets budgets, IReadOnlyList<RoomMod> mods = null)
        {
            if (Room != null) throw new InvalidOperationException("This world already has a room; its change mode is set when it is made.");
            if (baseRules == null) throw new ArgumentNullException(nameof(baseRules));
            if (budgets == null) throw new ArgumentNullException(nameof(budgets));
            var next = mods == null || mods.Count == 0 ? RuleSet.Plain(baseRules) : RuleSet.Compose(baseRules, mods.Select(m => new ActiveMod(m.Id, m.Json)));
            roomBudgets = budgets;
            thingsFor = null;
            Room = new RoomState { Mode = mode, Host = host ?? "" };
            if (mods != null) Room.Mods.AddRange(mods);
            RoomRules = next;
            if (Rules.Fingerprint != next.Fingerprint || next.Effective.ModBuildings.Count > 0) ApplyRules(next.Effective);
        }

        public void RebaseRoom(Rules baseRules)
        {
            PutInForce(RuleSet.Compose(baseRules, Room.Mods.Select(m => new ActiveMod(m.Id, m.Json))));
        }

        public RoomVerdict ApplyRoom(RoomCommand c) => ApplyRoom(c, null);

        public RoomVerdict ApplyRoom(RoomCommand c, RoomSwitchPrep prep)
        {
            if (Room == null) throw new InvalidOperationException("This world has no room to change; OpenRoom makes one.");
            FinishTick();
            if (c.Change == RoomChange.Found) return Found(c);
            var verdict = c.Change switch
            {
                RoomChange.Revert => Revert(c),
                RoomChange.SetTime => SetTime(c),
                RoomChange.SetWeather => SetWeather(c),
                RoomChange.Act => DoAct(c),
                RoomChange.Undo => UndoAct(c),
                _ => SwitchOn(c, prep),
            };
            AddLog(verdict.Line, verdict.Done ? LogKind.Notable : LogKind.Info);
            RoomDecided?.Invoke(c, verdict);
            var also = wentWith ?? new List<RoomMod>();
            wentWith = null;
            foreach (var m in also) RoomDecided?.Invoke(RoomCommand.Revert(m.By, m.Id), verdict);
            return verdict;
        }

        List<RoomMod> wentWith;

        RoomVerdict SwitchOn(RoomCommand c, RoomSwitchPrep prep)
        {
            string title = TitleOf(c);
            RuleSet next = null;
            Rules checkedRules = null;
            string why = RoomPolicy.Refusal(Room.Mode, Room.Host, c.Peer)
                ?? (c.Mod.Length == 0 ? "the mod has no id" : null)
                ?? (Room.Mods.Any(m => m.Id == c.Mod) ? $"{title} is already on; revert it first" : null)
                ?? Checks(title, c.Json, out checkedRules, prep)
                ?? (checkedRules != null
                    ? Composed(new ActiveMod(c.Mod, c.Json), checkedRules, out next)
                    : Compose(Room.Mods.Select(m => new ActiveMod(m.Id, m.Json)).Append(new ActiveMod(c.Mod, c.Json)), $"{title} doesn't fit the mods on", out next))
                ?? SiteProblem(next.Effective, title)
                ?? LiveProblem(next, title);
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
            var gone = at < 0 ? null : WithDependents(at);
            string why = RoomPolicy.Refusal(Room.Mode, Room.Host, c.Peer)
                ?? (at < 0 ? $"{named} isn't on in this room" : null)
                ?? Compose(Without(gone), $"without {title} the other mods don't fit", out next)
                ?? OverlapProblem(next.Effective, $"without {title}");
            if (why != null) return Refused(c, why);
            TakeOff(gone, next);
            wentWith = gone.Skip(1).ToList();
            return new RoomVerdict(true, null, $"{c.Peer} switched off {title}{AlsoGone(gone)} ({RoomPolicy.Words(Room.Mode)} room)");
        }

        static RoomVerdict Refused(RoomCommand c, string why) => new RoomVerdict(false, why, RoomPolicy.RefusedLine(c.Change, why));

        static string TitleOf(RoomCommand c)
        {
            if (!string.IsNullOrWhiteSpace(c.Title)) return c.Title.Trim();
            try
            {
                var t = ModPatch.Parse(c.Json).Title;
                if (!string.IsNullOrWhiteSpace(t)) return t.Trim();
            }
#pragma warning disable S108
            catch (ModException)
            {
            }
#pragma warning restore S108
            return c.Mod;
        }

        string Checks(string title, string json, out Rules composed, RoomSwitchPrep prep)
        {
            composed = null;
            var owners = TempMaterials.Owners(TemporaryMaterials);
            var ready = prep != null && prep.Fits(Rules, RoomRules, roomBudgets, owners, json) ? prep : null;
            if (ready == null)
            {
                ready = new RoomSwitchPrep(Rules, RoomRules, roomBudgets, owners, json);
                ready.Run();
            }
            else ready.Used = true;
            if (ready.Unreadable) return $"{title} can't be read as a mod";
            var problems = ready.Problems;
            if (ReferenceEquals(Rules, RoomRules?.Effective)) composed = ready.Composed;
            if (problems.Count == 0) return null;
            if (OwnerProblem(title, json) is string owner) return owner;
            string more = problems.Count == 1 ? "" : $" (and {problems.Count - 1} more)";
            return $"{title} doesn't pass the checks: {problems[0].Message.TrimEnd('.')}{more}";
        }

        string Composed(ActiveMod mod, Rules composed, out RuleSet next)
        {
            next = RoomRules.Adding(mod, composed);
            return null;
        }

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

        public RoomSwitchPrep PrepareSwitch(RoomCommand c)
        {
            if (Room == null) throw new InvalidOperationException("This world has no room to change; OpenRoom makes one.");
            FinishTick();
            return new RoomSwitchPrep(Rules, RoomRules, roomBudgets, TempMaterials.Owners(TemporaryMaterials), c.Json);
        }

        public string RoomHash() => Fnv1a.Hash(RoomText());

        public string RoomText()
        {
            if (Room == null) return "";
            var inv = CultureInfo.InvariantCulture;
            var sb = new StringBuilder("#room ");
            sb.Append(((int)Room.Mode).ToString(inv)).Append(' ').Append(Room.Host);
            foreach (var m in Room.Mods)
                sb.Append('|').Append(m.Id).Append(':').Append(m.By).Append(':').Append(m.Tick.ToString(inv)).Append(':').Append(Fnv1a.Hash(m.Json));
            foreach (ulong t in Room.Found) sb.Append("|found:").Append(t.ToString(inv));
            foreach (var r in Room.Retired)
                sb.Append("|retired:").Append(r.Table).Append(':').Append(r.Id).Append(':').Append(r.Into ?? "").Append(':').Append(r.Mod).Append(':').Append(r.Tick.ToString(inv));
            foreach (var r in Room.RetiredRows)
                sb.Append("|gone:").Append(r.Kind).Append(':').Append(r.Id).Append(':').Append(r.Mod).Append(':').Append(r.Tick.ToString(inv));
            foreach (var m in Room.Mixes)
                sb.Append("|mix:").Append(m.Table).Append(':').Append(m.Id).Append(':').Append(m.Tick.ToString(inv));
            return sb.ToString();
        }

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
            if (Room.Found.Count > 0) save["room"]["found"] = new JArray(Room.Found.Select(t => t.ToString(CultureInfo.InvariantCulture)));
            if (Room.Retired.Count > 0) save["room"]["retired"] = new JArray(Room.Retired.Select(r => new JArray(r.Table, r.Id, r.Into, r.Mod, r.Tick)));
            if (Room.RetiredRows.Count > 0) save["room"]["retiredRows"] = new JArray(Room.RetiredRows.Select(r => new JArray(r.Kind, r.Id, r.Mod, r.Tick)));
            if (Room.Mixes.Count > 0) save["room"]["mixes"] = new JArray(Room.Mixes.Select(SavedMix));
            SaveBuildings((JObject)save["room"]);
        }

        static RuleSet SavedRoomRules(JObject room, Rules baseRules, ModBudgets budgets)
        {
            Expect(budgets != null, "a room, loaded without the budgets its mods are checked against");
            Expect((string)room["base"] == baseRules.Fingerprint, "a room saved over other base rules");
            try
            {
                return RuleSet.Compose(baseRules, (room["mods"] ?? new JArray()).Select(m => new ActiveMod((string)m[0], (string)m[4])))
                    .WithMixes((room["mixes"] ?? new JArray()).Select(LoadedMix));
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
            foreach (var t in room["found"] ?? new JArray()) Room.Found.Add(ulong.Parse((string)t, CultureInfo.InvariantCulture));
            foreach (var r in room["retired"] ?? new JArray())
                Room.Retired.Add(new RetiredMaterial { Table = (string)r[0], Id = (string)r[1], Into = (string)r[2], Mod = (string)r[3], Tick = (int)r[4] });
            foreach (var r in room["retiredRows"] ?? new JArray())
                Room.RetiredRows.Add(new RetiredRow { Kind = (string)r[0], Id = (string)r[1], Mod = (string)r[2], Tick = (int)r[3] });
            foreach (var m in room["mixes"] ?? new JArray()) Room.Mixes.Add(LoadedMix(m));
            LoadBuildings(room);
        }
    }
}
