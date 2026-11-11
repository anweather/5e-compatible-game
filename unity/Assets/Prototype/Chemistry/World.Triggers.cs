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
    /// <summary>A cast a trigger made: for the view to play its show (the target mark is in the cells till it lands).</summary>
    public sealed class TriggeredCast
    {
        /// <summary>Its place in the world's running count of triggered casts: a view plays those past the last it saw.</summary>
        public int N;
        public string Trigger;
        /// <summary>The player whose event fired it.</summary>
        public string Player;
        public string Spell;
        public int X, Y;
        /// <summary>The grid way it was cast along (0 north, 1 east, 2 south, 3 west): the way the player moved.</summary>
        public int Dir;
        /// <summary>The tick it was cast on (the last whole tick when it fired).</summary>
        public int Tick;
        /// <summary>The tick it lands: Tick for one at once, later for one with a delay (it lands through LandStars).</summary>
        public int Lands;
    }

    public sealed partial class World
    {
        /// <summary>The most triggers that fire a tick in a room (J-067); budgets.json triggers.maxPerTick when the room has budgets.</summary>
        public const int DefaultMaxFiringsPerTick = 4;

        const int TriggerSalt = 0x74726967, AroundSalt = 0x61726e64;

        // The tick each trigger may next fire for each player, by "trigger/player" (ordinal order).
        readonly SortedDictionary<string, int> triggerNext = new SortedDictionary<string, int>(StringComparer.Ordinal);
        readonly List<TriggeredCast> triggered = new List<TriggeredCast>();
        readonly List<TriggeredCast> triggeredHistory = new List<TriggeredCast>();
        // Firings since the world began, triggered casts made, and the firings on firedTick (for the per-tick cap).
        int triggerFirings, triggeredCasts, firedTick, firedOnTick;

        /// <summary>The casts triggers made since this tick began, for the view to play (as NewMeteors).</summary>
        public IReadOnlyList<TriggeredCast> Triggered => triggered;

        /// <summary>The triggered casts after the <paramref name="n"/>th (within the last 600 ticks), oldest first: for a view that steps several ticks a frame.</summary>
        public IEnumerable<TriggeredCast> TriggeredAfter(int n)
        {
            foreach (var t in triggeredHistory) if (t.N > n) yield return t;
        }

        /// <summary>How many times triggers have fired since the world began.</summary>
        public int TriggerFirings => triggerFirings;

        /// <summary>How many casts triggers have made since the world began.</summary>
        public int TriggeredCasts => triggeredCasts;

        int MaxFiringsPerTick => (int)(roomBudgets?.Number("triggers.maxPerTick", DefaultMaxFiringsPerTick) ?? DefaultMaxFiringsPerTick);

        // Each new tick: this tick's list starts empty, and the history keeps its last HistoryTicks.
        void BeginTriggerTick()
        {
            triggered.Clear();
            if (triggeredHistory.Count > 0) triggeredHistory.RemoveAll(t => t.Tick <= Tick - HistoryTicks);
        }

        // A player's stand moved from (fromX, fromY) to its cell: the step triggers that are theirs fire, in table order.
        void Stepped(WizardStand me, int fromX, int fromY)
        {
            var list = Rules.TriggersOn(TriggerEvent.Step);
            if (list.Count == 0) return;
            int dx = Math.Sign(DeltaX(fromX, me.X)), dy = Math.Sign(me.Y - fromY);
            string peer = TriggerDef.PeerOf(me.Player);
            foreach (var t in list)
                if (t.IsFor(me.Player, peer) && Due(t, me.Player)) Fire(t, me, dx, dy);
        }

        // A tick begins: each tick trigger fires round each standing player it's for, when due, in table order then the
        // players' (ordinal) order, facing south as nobody stepped. Before the tick's stages, as a cast between ticks lands.
        void Ticked()
        {
            var list = Rules.TriggersOn(TriggerEvent.Tick);
            if (list.Count == 0 || stands.Count == 0) return;
            var standing = stands.Values.Where(s => !s.Down).ToList();
            foreach (var t in list)
                foreach (var me in standing)
                    if (t.IsFor(me.Player, TriggerDef.PeerOf(me.Player)) && Due(t, me.Player)) Fire(t, me, 0, 1);
        }

        // The ball went in: the holed triggers of who hit it fire at the cup, facing south as nobody stepped.
        void BallHoled(GolfState g)
        {
            var list = Rules.TriggersOn(TriggerEvent.Holed);
            if (list.Count == 0) return;
            var cup = new WizardStand { Player = g.LastBy ?? TriggerDef.SoloPlayer, X = g.HoleX, Y = g.HoleY };
            string peer = TriggerDef.PeerOf(cup.Player);
            foreach (var t in list)
                if (t.IsFor(cup.Player, peer) && Due(t, cup.Player)) Fire(t, cup, 0, 1);
        }

        // Whether a trigger of the player's fires now: past its floor, under the tick's cap, and its chance met.
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
            // The log names the player as the room does ("Player 1"), not their stand ("solo", "p0").
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

        // The cell an action lands on: the player's, along the step by By, or one picked within Within. False off the world.
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
                    (x, y) = pick[0]; // folded below
                    break;
            }
            x = FoldX(x); // a step's cell past the seam
            return InBounds(x, y);
        }

        // FNV-1a of a name, the same on every runtime (string.GetHashCode isn't).
        static int StableHash(string s)
        {
            unchecked
            {
                uint h = 0x811c9dc5;
                foreach (char c in s ?? "") h = (h ^ c) * 0x01000193u;
                return (int)h;
            }
        }

        /// <summary>
        /// The triggers' state, for CastHash: firings, casts, the tick-cap's tick and count, then each cooldown
        /// ("trigger/player:next"). "" until one fires, so worlds without hash as they did.
        /// </summary>
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
