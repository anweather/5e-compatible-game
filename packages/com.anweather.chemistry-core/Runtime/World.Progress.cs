// Progress in the Hold (docs/design/the-hold.md, phase 5). Inside a run: as a wave is beaten each wizard is offered upgrades by the
// seed, picks by command, and one that doesn't has one picked for it as the next wave starts. Between runs: a wizard's points say
// what's unlocked, brought by a command so every peer agrees; a wizard with none brought has everything, as does anyone in a
// moment, so a test, a bot or a playtest isn't held back. The locks hold only in a run: the valley stays a sandbox, and outside a
// run every wizard has everything, whatever its points (#152's "the sandbox's controls and spells don't change"). The run
// itself, with each wizard's offers and picks, is the Hold's (Play/HoldSystem.cs): the world asks its modes whether a run
// or a moment is on and what a wizard has picked (World.Play.cs), and keeps the points.
using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace Chem
{
    public sealed partial class World
    {
        public const int AllPoints = 1000000;

        readonly SortedDictionary<string, int> progress = new SortedDictionary<string, int>(StringComparer.Ordinal);

        public int? PointsOf(string player) => progress.TryGetValue(player, out int points) ? points : (int?)null;

        bool NothingHeld(string player) => !progress.ContainsKey(player) || InMoment;

        bool AllOpen(string player) => NothingHeld(player) || !InRun;

        public IEnumerable<UnlockDef> UnlocksOf(string player)
        {
            int left = PointsOf(player) ?? AllPoints;
            foreach (var row in Rules.Sim.Hold.Unlocks)
            {
                if ((left -= row.Cost) < 0) yield break;
                yield return row;
            }
        }

        public int PointsAway(string player, UnlockDef row)
        {
            if (NothingHeld(player)) return 0;
            int through = 0;
            foreach (var u in Rules.Sim.Hold.Unlocks)
            {
                through += u.Cost;
                if (u == row) return Math.Max(0, through - (PointsOf(player) ?? AllPoints));
            }
            return 0;
        }

        public UnlockDef NextUnlock(string player) => Rules.Sim.Hold.Unlocks.FirstOrDefault(u => PointsAway(player, u) > 0);

        public UnlockDef UnlockGiving(Func<UnlockDef, bool> gives) => Rules.Sim.Hold.Unlocks.FirstOrDefault(gives);

        bool Has(string player, Func<UnlockDef, bool> gives) =>
            AllOpen(player) || !Rules.Sim.Hold.Unlocks.Any(gives) || UnlocksOf(player).Any(gives);

        public bool GestureOpen(string player, int gesture) => Has(player, u => u.Gesture == gesture);

        public bool HandOpen(string player, int hand) => Has(player, u => u.Hand == (hand == HandsDef.Left ? "left" : "right"));

        public bool ElementOpen(string player, string element) => element == null || Has(player, u => u.Element == element);

        public bool KitOpen(string player, string spell) => Has(player, u => u.Kit == spell);

        bool BringPoints(string player, int points)
        {
            if (points < 0) return false;
            progress[player] = points;
            LetGoLocked(player);
            return true;
        }

        internal void LetGoLockedAll()
        {
            foreach (var player in progress.Keys.ToList()) LetGoLocked(player);
        }

        void LetGoLocked(string player)
        {
            if (AllOpen(player)) return;
            var h = OwnHands(player);
            for (int i = 0; i < HandsDef.Count; i++)
                if (!HandOpen(player, i) || !ElementOpen(player, h.Holds[i])) h.Holds[i] = null;
            if (h.Holds.All(x => x == null)) h.Holds[HandsDef.Right] = HandRules.Start[HandsDef.Right];
        }

        internal void EarnPoints(HoldParams p, int survived)
        {
            foreach (var player in progress.Keys.ToList())
                progress[player] = (int)Math.Min(AllPoints, (long)progress[player] + (long)survived * p.PointsPerWave);
        }

        public bool Suits(UpgradeDef u, string player)
        {
            var h = HandsOf(player);
            bool holds = u.Holds == null || Enumerable.Range(0, HandsDef.Count).Any(i => h.Holds[i] == u.Holds && HandOpen(player, i));
            bool both = !u.BothHands || (HandOpen(player, HandsDef.Left) && HandOpen(player, HandsDef.Right));
            return holds && both && (u.Gesture == HandsDef.NoGesture || GestureOpen(player, u.Gesture));
        }

        IEnumerable<UpgradeDef> UpgradesOf(string player) => player == null ? Enumerable.Empty<UpgradeDef>() : PickedBy(player);

        static bool Touches(UpgradeDef u, string slot, int gesture) =>
            (u.Gesture == HandsDef.NoGesture || u.Gesture == gesture) && (u.Holds == null || (slot ?? "").Split('+').Contains(u.Holds));

        static int WithPercent(int value, int percent) => Math.Max(1, value * Math.Max(0, 100 + percent) / 100);

        int CooldownOf(string player, string holds, int gesture, bool both, bool tap, int own)
        {
            int percent = UpgradesOf(player).Where(u => Touches(u, holds, gesture)).Sum(u => u.Cooldown + (both ? u.BothCooldown : 0));
            int table = HandRules.HandCooldown;
            if (both) table = HandRules.BothCooldown;
            else if (tap && HandRules.TapCooldown > 0) table = HandRules.TapCooldown;
            return WithPercent(own > 0 ? own : table, percent);
        }

        int RangeOf(string player, string holds, int gesture) =>
            WithPercent(HandRules.RangeOf(gesture), UpgradesOf(player).Where(u => Touches(u, holds, gesture)).Sum(u => u.Range));

        public int HealthBonusOf(string player) => UpgradesOf(player).Sum(u => u.Health);

        int PowerOf(string player, string spell)
        {
            var (slot, gesture) = HandRules.Find(spell);
            return slot == null ? 100 : 100 + UpgradesOf(player).Where(u => Touches(u, slot, gesture)).Sum(u => u.Damage);
        }

        string ProgressText() => string.Concat(progress.Select(kv => $"|G{kv.Key}:{Whole(kv.Value)}"));

        JObject SaveProgress() => new JObject(progress.Select(kv => new JProperty(kv.Key, kv.Value)));

        void LoadProgress(JToken saved)
        {
            foreach (var p in (saved as JObject)?.Properties() ?? Enumerable.Empty<JProperty>()) progress[p.Name] = (int)p.Value;
        }
    }
}
