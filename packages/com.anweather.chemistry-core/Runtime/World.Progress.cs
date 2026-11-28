// Progress in the Hold on a wizard's hands (docs/design/the-hold.md, phase 5). Inside a run each wizard picks upgrades, which
// change its hands' cooldowns, ranges, power and health; between runs its points say what's unlocked: a hand, a gesture, an
// element, a kit spell. The locks hold only in a run, so the valley stays a sandbox (#152's "the sandbox's controls and spells
// don't change"). The points, the run and its picks are the Hold's (Play/ProgressSystem.cs, Play/HoldSystem.cs): the world asks
// its modes what they hold back and open for a wizard and what it has picked (World.Play.cs), and lets go of what's locked.
using System;
using System.Collections.Generic;
using System.Linq;

namespace Chem
{
    public sealed partial class World
    {
        bool AllOpen(string player) => !installed.Any(m => m.HoldsBack(player));

        bool Has(string player, Func<UnlockDef, bool> gives) => installed.All(m => m.Opens(player, gives));

        public UnlockDef UnlockGiving(Func<UnlockDef, bool> gives) => Rules.Sim.Hold.Unlocks.FirstOrDefault(gives);

        public bool GestureOpen(string player, int gesture) => Has(player, u => u.Gesture == gesture);

        public bool HandOpen(string player, int hand) => Has(player, u => u.Hand == (hand == HandsDef.Left ? "left" : "right"));

        public bool ElementOpen(string player, string element) => element == null || Has(player, u => u.Element == element);

        public bool KitOpen(string player, string spell) => Has(player, u => u.Kit == spell);

        internal void LetGoLocked(string player)
        {
            if (AllOpen(player)) return;
            var h = OwnHands(player);
            for (int i = 0; i < HandsDef.Count; i++)
                if (!HandOpen(player, i) || !ElementOpen(player, h.Holds[i])) h.Holds[i] = null;
            if (h.Holds.All(x => x == null)) h.Holds[HandsDef.Right] = HandRules.Start[HandsDef.Right];
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

    }
}
