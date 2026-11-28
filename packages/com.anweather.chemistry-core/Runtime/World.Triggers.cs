// Triggers firing (docs/design/events-and-placing.md 2c, J-067): rules/triggers.json rows
// reacting to an event: a step, a player's Stand command whose cell differs from the one the
// core has (ApplyStrike; not a first stand, not while downed), a golf ball holed, in the
// ball's tick, at the cup, or a tick beginning (a shower round each standing player it's for). A trigger fires inside that, in order, so every peer fires the same ones, and
// its actions run there as a cast does: a cast of a spell (through Release, a delay giving it
// a target mark as Falling Star has) or a place (PlaceOn, as a spell's). The cell is the
// player's own input, as a cast's is: physics decides nothing shared (D4).
//
// Caps: a trigger waits at least TriggerDef.MinEvery ticks between firings for one player,
// and at most MaxFiringsPerTick fire a tick in the room (later ones that tick are dropped, the
// same on every peer). Its chance is a WorldHash of the seed, the trigger, the tick and the
// player: no draw from the world's generator. No chains: an action raises no event. The
// cooldowns are core state, in CastHash (TriggerText) and the save, "" until one fires, so
// worlds without triggers hash and save as they did. The view learns of the casts from
// Triggered (this tick's, as NewMeteors) or TriggeredAfter.
//
// Not a port: the sandbox has no triggers.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Newtonsoft.Json.Linq;

namespace Chem
{
    public sealed class TriggeredCast
    {
        public int N;
        public string Trigger;
        public string Player;
        public string Spell;
        public int X, Y;
        public int Dir;
        public int Tick;
        public int Lands;
    }

    public sealed partial class World
    {
        public const int DefaultMaxFiringsPerTick = 4;

        const int TriggerSalt = 0x74726967, AroundSalt = 0x61726e64;

        readonly SortedDictionary<string, int> triggerNext = new SortedDictionary<string, int>(StringComparer.Ordinal);
        readonly List<TriggeredCast> triggered = new List<TriggeredCast>();
        readonly List<TriggeredCast> triggeredHistory = new List<TriggeredCast>();
        int triggerFirings, triggeredCasts, firedTick, firedOnTick;

        public IReadOnlyList<TriggeredCast> Triggered => triggered;

        public IEnumerable<TriggeredCast> TriggeredAfter(int n)
        {
            foreach (var t in triggeredHistory) if (t.N > n) yield return t;
        }

        public int TriggerFirings => triggerFirings;

        public int TriggeredCasts => triggeredCasts;

        int MaxFiringsPerTick => (int)(roomBudgets?.Number("triggers.maxPerTick", DefaultMaxFiringsPerTick) ?? DefaultMaxFiringsPerTick);

        void BeginTriggerTick()
        {
            triggered.Clear();
            if (triggeredHistory.Count > 0) triggeredHistory.RemoveAll(t => t.Tick <= Tick - HistoryTicks);
        }

        void Stepped(WizardStand me, int fromX, int fromY)
        {
            var list = Rules.TriggersOn(TriggerEvent.Step);
            if (list.Count == 0) return;
            int dx = Math.Sign(DeltaX(fromX, me.X)), dy = Math.Sign(DeltaY(fromY, me.Y));
            string peer = TriggerDef.PeerOf(me.Player);
            foreach (var t in list)
                if (t.IsFor(me.Player, peer) && Due(t, me.Player)) Fire(t, me, dx, dy);
        }

        void Ticked()
        {
            var list = Rules.TriggersOn(TriggerEvent.Tick);
            if (list.Count == 0 || stands.Count == 0) return;
            var standing = stands.Values.Where(s => !s.Down).ToList();
            foreach (var t in list)
                foreach (var me in standing)
                    if (t.IsFor(me.Player, TriggerDef.PeerOf(me.Player)) && Due(t, me.Player)) Fire(t, me, 0, 1);
        }

        public void RaiseHoled(string by, int x, int y)
        {
            var list = Rules.TriggersOn(TriggerEvent.Holed);
            if (list.Count == 0) return;
            var cup = new WizardStand { Player = by ?? TriggerDef.SoloPlayer, X = x, Y = y };
            string peer = TriggerDef.PeerOf(cup.Player);
            foreach (var t in list)
                if (t.IsFor(cup.Player, peer) && Due(t, cup.Player)) Fire(t, cup, 0, 1);
        }

        bool Due(TriggerDef t, string player)
        {
            if (triggerNext.TryGetValue(t.Id + "/" + player, out int next) && Tick < next) return false;
            if (firedTick == Tick && firedOnTick >= MaxFiringsPerTick) return false;
            return t.Chance >= 1 || WorldHash.HashUnit(unchecked((int)Seed), TriggerSalt ^ StableHash(t.Id), Tick, StableHash(player)) < t.Chance;
        }

        void Fire(TriggerDef t, WizardStand me, int dx, int dy)
        {
            triggerNext[t.Id + "/" + me.Player] = Tick + Math.Max(TriggerDef.MinEvery, t.Every);
            if (firedTick != Tick) (firedTick, firedOnTick) = (Tick, 0);
            firedOnTick++;
            triggerFirings++;
            foreach (var a in t.Do) Act(t, a, me, dx, dy);
        }

        void Act(TriggerDef t, TriggerAction a, WizardStand me, int dx, int dy)
        {
            if (!ActionCell(a, me, dx, dy, out int x, out int y)) return;
            if (a.Place != null)
            {
                PlaceOn(a.Place, t.Id, Placement.Around(x, y, Math.Max(0, a.Place.Within)));
                return;
            }
            var s = Rules.Spell(a.Cast);
            if (s == null) return;
            int dir = DirOf(dx, dy), delay = a.Delay > 0 ? a.Delay : s.Delay;
            if (!Release(s, (x, y), dir, 1, (dx, dy), delay, $"{TriggerDef.PeerOf(me.Player)}'s {EventWord(t.On)} calls down {s.Name}")) return;
            var cast = new TriggeredCast { N = ++triggeredCasts, Trigger = t.Id, Player = me.Player, Spell = s.Id, X = x, Y = y, Dir = dir, Tick = Tick, Lands = Tick + delay };
            triggered.Add(cast);
            triggeredHistory.Add(cast);
        }

        static string EventWord(TriggerEvent on) => on switch
        {
            TriggerEvent.Holed => "shot",
            TriggerEvent.Tick => "shower",
            _ => "step",
        };

        bool ActionCell(TriggerAction a, WizardStand me, int dx, int dy, out int x, out int y)
        {
            (x, y) = (me.X, me.Y);
            switch (a.At)
            {
                case TriggerAt.Ahead:
                    (x, y) = (me.X + dx * a.By, me.Y + dy * a.By);
                    break;
                case TriggerAt.Behind:
                    (x, y) = (me.X - dx * a.By, me.Y - dy * a.By);
                    break;
                case TriggerAt.Around:
                    int salt = unchecked((int)WorldHash.Hash4(unchecked((int)Seed), AroundSalt, Tick, triggerFirings));
                    var pick = Placement.Pick(Placement.Around(me.X, me.Y, a.Within), 1, 0, (px, py) => InBounds(px, py) && IsActiveAt(px, py), unchecked((int)Seed), salt);
                    if (pick.Count == 0) return false;
                    (x, y) = pick[0];
                    break;
            }
            x = FoldX(x);
            y = FoldY(y);
            return InBounds(x, y);
        }

        static int StableHash(string s)
        {
            unchecked
            {
                uint h = 0x811c9dc5;
                foreach (char c in s ?? "") h = (h ^ c) * 0x01000193u;
                return (int)h;
            }
        }

        public string TriggerText()
        {
            if (triggerFirings == 0) return "";
            var sb = new StringBuilder();
            sb.Append("|trig:").Append(Whole(triggerFirings)).Append(':').Append(Whole(triggeredCasts)).Append(':').Append(Whole(firedTick)).Append(':').Append(Whole(firedOnTick));
            foreach (var kv in triggerNext) sb.Append('|').Append(kv.Key).Append(':').Append(Whole(kv.Value));
            return sb.ToString();
        }

        void SaveTriggers(JObject save)
        {
            if (triggerFirings == 0) return;
            save["triggers"] = new JObject
            {
                ["firings"] = triggerFirings,
                ["casts"] = triggeredCasts,
                ["tick"] = new JArray(firedTick, firedOnTick),
                ["next"] = new JObject(triggerNext.Select(kv => new JProperty(kv.Key, kv.Value))),
            };
        }

        void LoadTriggers(JObject s)
        {
            if (!(s["triggers"] is JObject t)) return;
            triggerFirings = (int)t["firings"];
            triggeredCasts = (int)t["casts"];
            firedTick = (int)t["tick"][0];
            firedOnTick = (int)t["tick"][1];
            foreach (var p in ((JObject)t["next"]).Properties()) triggerNext[p.Name] = (int)p.Value;
        }
    }
}
