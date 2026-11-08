// Progress in the Hold (docs/design/the-hold.md, phase 5). Inside a run: as a wave is beaten each wizard is offered upgrades by the
// seed, picks by command, and one that doesn't has one picked for it as the next wave starts. Between runs: a wizard's points say
// what's unlocked, brought by a command so every peer agrees; a wizard with none brought has everything, as does anyone in a
// moment, so a test, a bot or a playtest isn't held back.
using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace Chem
{
    public sealed partial class World
    {
        /// <summary>As many points as anyone needs: the dev builds' start with everything.</summary>
        public const int AllPoints = 1000000;

        readonly SortedDictionary<string, int> progress = new SortedDictionary<string, int>(StringComparer.Ordinal);

        /// <summary>The points this player brought and has earned here; null if they brought none.</summary>
        public int? PointsOf(string player) => progress.TryGetValue(player, out int points) ? points : (int?)null;

        bool AllOpen(string player) => !progress.ContainsKey(player) || Hold?.Moment != null;

        /// <summary>The unlocks this player has, in order.</summary>
        public IEnumerable<UnlockDef> UnlocksOf(string player)
        {
            int left = PointsOf(player) ?? AllPoints;
            foreach (var row in Rules.Sim.Hold.Unlocks)
            {
                if ((left -= row.Cost) < 0) yield break;
                yield return row;
            }
        }

        /// <summary>How many more points this player needs for a row: 0 if it's theirs. Rows unlock in order, so it counts those before it.</summary>
        public int PointsAway(string player, UnlockDef row)
        {
            if (AllOpen(player)) return 0;
            int through = 0;
            foreach (var u in Rules.Sim.Hold.Unlocks)
            {
                through += u.Cost;
                if (u == row) return Math.Max(0, through - (PointsOf(player) ?? AllPoints));
            }
            return 0;
        }

        /// <summary>The next row this player hasn't got, or null with everything.</summary>
        public UnlockDef NextUnlock(string player) => Rules.Sim.Hold.Unlocks.FirstOrDefault(u => PointsAway(player, u) > 0);

        /// <summary>The row that gives this, or null when a wizard starts with it.</summary>
        public UnlockDef UnlockGiving(Func<UnlockDef, bool> gives) => Rules.Sim.Hold.Unlocks.FirstOrDefault(gives);

        // Whether a thing a row gives is this player's. What no row gives, a wizard starts with.
        bool Has(string player, Func<UnlockDef, bool> gives) =>
            AllOpen(player) || !Rules.Sim.Hold.Unlocks.Any(gives) || UnlocksOf(player).Any(gives);

        public bool GestureOpen(string player, int gesture) => Has(player, u => u.Gesture == gesture);

        public bool HandOpen(string player, int hand) => Has(player, u => u.Hand == (hand == HandsDef.Left ? "left" : "right"));

        public bool ElementOpen(string player, string element) => element == null || Has(player, u => u.Element == element);

        public bool KitOpen(string player, string spell) => Has(player, u => u.Kit == spell);

        // What a player brings: every peer hears the same points. A hand that held what isn't theirs lets go of it.
        bool BringPoints(string player, int points)
        {
            if (points < 0) return false;
            progress[player] = points;
            var h = OwnHands(player);
            for (int i = 0; i < HandsDef.Count; i++)
                if (!HandOpen(player, i) || !ElementOpen(player, h.Holds[i])) h.Holds[i] = null;
            if (h.Holds.All(x => x == null)) h.Holds[HandsDef.Right] = HandRules.Start[HandsDef.Right];
            return true;
        }

        // The run's end: each wizard keeps what it earned, but a moment earns nothing.
        // Points for the waves survived: those before the one the keep fell in, or every wave of a run won.
        void EarnPoints(HoldParams p, int survived)
        {
            if (Hold.Moment != null) return;
            foreach (var player in progress.Keys.ToList())
                progress[player] = (int)Math.Min(AllPoints, (long)progress[player] + (long)survived * p.PointsPerWave);
        }

        // ---------- upgrades ----------

        /// <summary>This wizard's place in the run, or null.</summary>
        public RunWizard RunOf(string player) => Hold != null && Hold.Wizards.TryGetValue(player, out var w) ? w : null;

        static uint NameRoll(string player)
        {
            uint h = 2166136261u;
            unchecked
            {
                foreach (char c in player) h = (h ^ c) * 16777619u;
            }
            return h;
        }

        /// <summary>Whether this wizard can use a row with what it has unlocked: what an open hand holds, a gesture it has, and both
        /// hands if the row needs them.</summary>
        public bool Suits(UpgradeDef u, string player)
        {
            var h = HandsOf(player);
            bool holds = u.Holds == null || Enumerable.Range(0, HandsDef.Count).Any(i => h.Holds[i] == u.Holds && HandOpen(player, i));
            bool both = !u.BothHands || (HandOpen(player, HandsDef.Left) && HandOpen(player, HandsDef.Right));
            return holds && both && (u.Gesture == HandsDef.NoGesture || GestureOpen(player, u.Gesture));
        }

        // Each wizard's offer for the wave coming, by the seed, the wave and its name: only rows that suit it, so it may be fewer.
        void OfferUpgrades(HoldParams p)
        {
            const uint offer = 7;
            foreach (var player in stands.Keys)
            {
                if (!Hold.Wizards.TryGetValue(player, out var w)) Hold.Wizards[player] = w = new RunWizard { Player = player };
                w.Offer.Clear();
                var rows = p.Upgrades.Select((u, i) => (u, roll: WaveRoll(Hold.Wave, i, offer) ^ NameRoll(player)))
                    .Where(x => Suits(x.u, player)).OrderBy(x => x.roll).ThenBy(x => x.u.Id, StringComparer.Ordinal);
                w.Offer.AddRange(rows.Take(p.Offer).Select(x => x.u.Id));
            }
        }

        bool PickUpgrade(string player, string id)
        {
            var w = RunOf(player);
            if (!HoldRunning || w == null || !w.Offer.Contains(id)) return false;
            w.Picks.Add(id);
            w.Offer.Clear();
            return true;
        }

        // The wave's here: an offer still open is picked for the wizard, by the seed.
        void PickForTheLate()
        {
            const uint late = 8;
            foreach (var w in Hold.Wizards.Values)
                if (w.Offer.Count > 0) PickUpgrade(w.Player, w.Offer[(int)((WaveRoll(Hold.Wave, 0, late) ^ NameRoll(w.Player)) % (uint)w.Offer.Count)]);
        }

        IEnumerable<UpgradeDef> UpgradesOf(string player)
        {
            var p = Rules.Sim.Hold;
            return HoldRunning && player != null && Hold.Wizards.TryGetValue(player, out var w) ? w.Picks.Select(p.Upgrade).Where(u => u != null) : Enumerable.Empty<UpgradeDef>();
        }

        static bool Touches(UpgradeDef u, string slot, int gesture) =>
            (u.Gesture == HandsDef.NoGesture || u.Gesture == gesture) && (u.Holds == null || (slot ?? "").Split('+').Contains(u.Holds));

        // A number with a wizard's percents on it; a cooldown stops at a tick.
        static int WithPercent(int value, int percent) => Math.Max(1, value * Math.Max(0, 100 + percent) / 100);

        // A row's own cooldown (a big spell's, Meteor's) stands in for the table's.
        int CooldownOf(string player, string holds, int gesture, bool both, int own)
        {
            int percent = UpgradesOf(player).Where(u => Touches(u, holds, gesture)).Sum(u => u.Cooldown + (both ? u.BothCooldown : 0));
            int table = both ? HandRules.BothCooldown : HandRules.HandCooldown;
            return WithPercent(own > 0 ? own : table, percent);
        }

        int RangeOf(string player, string holds, int gesture) =>
            WithPercent(HandRules.RangeOf(gesture), UpgradesOf(player).Where(u => Touches(u, holds, gesture)).Sum(u => u.Range));

        /// <summary>What this wizard's upgrades add to its health.</summary>
        public int HealthBonusOf(string player) => UpgradesOf(player).Sum(u => u.Health);

        // The percent of its damage a hand spell does for this wizard; 100 for anything else.
        int PowerOf(string player, string spell)
        {
            var (slot, gesture) = HandRules.Find(spell);
            return slot == null ? 100 : 100 + UpgradesOf(player).Where(u => Touches(u, slot, gesture)).Sum(u => u.Damage);
        }

        // ---------- the checksum and the save ----------

        string ProgressText() => string.Concat(progress.Select(kv => $"|G{kv.Key}:{Whole(kv.Value)}"));

        static string RunText(HoldState h) =>
            h == null ? "" : string.Concat(h.Wizards.Values.Select(w => $"|W{w.Player}:{string.Join(",", w.Offer)}:{string.Join(",", w.Picks)}"));

        static JArray SaveWizards(HoldState h) => new JArray(h.Wizards.Values.Select(w => new JArray(w.Player, new JArray(w.Offer), new JArray(w.Picks))));

        static void LoadWizards(HoldState h, JToken saved)
        {
            foreach (var a in saved ?? new JArray())
            {
                var w = new RunWizard { Player = (string)a[0] };
                w.Offer.AddRange(a[1].Select(id => (string)id));
                w.Picks.AddRange(a[2].Select(id => (string)id));
                h.Wizards[w.Player] = w;
            }
        }

        JObject SaveProgress() => new JObject(progress.Select(kv => new JProperty(kv.Key, kv.Value)));

        void LoadProgress(JToken saved)
        {
            foreach (var p in (saved as JObject)?.Properties() ?? Enumerable.Empty<JProperty>()) progress[p.Name] = (int)p.Value;
        }
    }
}
