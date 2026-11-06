// A wizard's hands in the Hold (docs/design/the-hold.md, phase 3). A hand holds an element or nothing, changed only by a command
// and only in the attuning room. During a run a hand's button is a gesture: the first press starts the cast meter of
// World.Combos.cs, and inside the window that hand's other button makes it a sweep and the other hand's buttons join, each
// starting the meter again for what's now coming. When it runs out: two hands making the same gesture bring out the table's
// combined row, if it has one; anything else, each hand does its own, both at once, and the chemistry decides what they make.
// Commands are ComboCommands, so they're stamped and replayed like the rest.
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace Chem
{
    public sealed partial class World
    {
        readonly SortedDictionary<string, PlayerHands> hands = new SortedDictionary<string, PlayerHands>(System.StringComparer.Ordinal);

        HandsDef HandRules => Rules.Sim.Hold.Hands;

        /// <summary>A wizard's hands; as they start if nothing has changed them.</summary>
        public PlayerHands HandsOf(string player)
        {
            if (hands.TryGetValue(player, out var h)) return h;
            h = new PlayerHands { Player = player };
            for (int i = 0; i < HandsDef.Count; i++) h.Holds[i] = HandRules.Start[i];
            return h;
        }

        /// <summary>Whether this wizard stands up in the attuning room.</summary>
        public bool InAttuning(string player) => HoldSite != null && stands.TryGetValue(player, out var s) && !s.Down && HoldSite.InAttuning(s.X, s.Y);

        /// <summary>Whether this wizard's buttons are its hands: in a run, and in the attuning room.</summary>
        public bool HandsOn(string player) => HoldRunning || InAttuning(player);

        /// <summary>Whether a stand on this cell, now, is one the Hold reads, so a late joiner's history must keep it.</summary>
        public bool HoldReadsStand(int x, int y) => HoldSite != null && (HoldRunning || HoldSite.Plate == (x, y) || HoldSite.InAttuning(x, y));

        /// <summary>The D-pad's list for this wizard: the kit during a run, else the combo table's.</summary>
        public IReadOnlyList<DpadSpellDef> DpadOf(string player)
        {
            if (!HandsOn(player) || HandRules.Kit.Count == 0) return Rules.Combos.Dpad;
            return AllOpen(player) ? HandRules.Kit : HandRules.Kit.Where(k => KitOpen(player, k.Spell)).ToList();
        }

        /// <summary>The ticks until this hand casts again; 0 when it's ready.</summary>
        public int HandCooling(string player, int hand) => System.Math.Max(0, HandsOf(player).Ready[hand] - Tick);

        /// <summary>The first spell a hand cast under way will bring out, or null.</summary>
        public string HandSpellUnderWay(string player) => hands.TryGetValue(player, out var h) && h.Casting ? Coming(h).Select(c => c.row.Spell).FirstOrDefault(s => s != null) : null;

        /// <summary>Every spell a hand cast under way will bring out: two gestures together are two.</summary>
        public List<string> HandSpellsUnderWay(string player) => hands.TryGetValue(player, out var h) && h.Casting ? Coming(h).Select(c => c.row.Spell).Where(s => s != null).ToList() : new List<string>();

        // What the hands bring out as they stand: a combined row with both hands, or each hand's own.
        List<(HandRowDef row, int gesture, int hand)> Coming(PlayerHands h)
        {
            const int both = -1;
            var coming = new List<(HandRowDef, int, int)>();
            int left = h.Gesture[HandsDef.Left], right = h.Gesture[HandsDef.Right];
            if (left != HandsDef.NoGesture && left == right && HandRules.Both(h.Holds[HandsDef.Left], h.Holds[HandsDef.Right], left) is { } row)
            {
                coming.Add((row, left, both));
                return coming;
            }
            for (int hand = 0; hand < HandsDef.Count; hand++)
                if (h.Gesture[hand] != HandsDef.NoGesture && HandRules.One(h.Holds[hand], h.Gesture[hand]) is { } own) coming.Add((own, h.Gesture[hand], hand));
            return coming;
        }

        PlayerHands OwnHands(string player)
        {
            var h = HandsOf(player);
            hands[player] = h;
            return h;
        }

        // A null element empties the hand.
        bool Attune(string player, int hand, string element)
        {
            if (hand < 0 || hand >= HandsDef.Count || (element != null && !Rules.Combos.Elements.Contains(element)) || !InAttuning(player) || HandsOf(player).Casting || !HandOpen(player, hand) || !ElementOpen(player, element)) return false;
            OwnHands(player).Holds[hand] = element;
            return true;
        }

        // A hand's button: a thrust or a throw, or with the hand's other button inside the window, a sweep.
        bool PressHand(PlayerCast c, int hand, int gesture)
        {
            if (!HoldRunning || hand < 0 || hand >= HandsDef.Count || !HandOpen(c.Player, hand) || !GestureOpen(c.Player, gesture)) return false;
            var h = HandsOf(c.Player);
            if (h.Ready[hand] > Tick) return false;
            if (!c.Casting)
            {
                h = OwnHands(c.Player);
                h.Gesture[hand] = gesture;
                c.Window = HandRules.Window;
            }
            else if (!h.Casting || c.Window <= 0 || !Join(h, hand, gesture, GestureOpen(c.Player, HandsDef.Sweep))) return false;
            StartMeter(c, Coming(h).Select(x => x.row.CastTicks).DefaultIfEmpty(1).Max());
            return true;
        }

        static bool Join(PlayerHands h, int hand, int gesture, bool sweeps)
        {
            int was = h.Gesture[hand];
            if (was == HandsDef.Sweep || was == gesture || (was != HandsDef.NoGesture && !sweeps)) return false;
            h.Gesture[hand] = was == HandsDef.NoGesture ? gesture : HandsDef.Sweep;
            return true;
        }

        // True if this was the hands' cast, gone off now.
        bool FireHands(PlayerCast c)
        {
            if (!hands.TryGetValue(c.Player, out var h) || !h.Casting) return false;
            foreach (var (row, gesture, hand) in Coming(h))
            {
                var spell = Rules.Spell(row.Spell);
                string holds = hand >= 0 ? h.Holds[hand] : CombosDef.IdOf(h.Holds[HandsDef.Left], h.Holds[HandsDef.Right]);
                NoteFired(new ComboFired(c.Player, null, spell?.Name ?? row.Act, row.Spell, spell?.Shape, false).FromHand(row.Act, RangeOf(c.Player, holds, gesture)));
                if (row.Spell == null) continue;
                if (hand >= 0) h.Ready[hand] = Tick + CooldownOf(c.Player, holds, gesture, false);
                else h.Ready[HandsDef.Left] = h.Ready[HandsDef.Right] = Tick + CooldownOf(c.Player, holds, gesture, true);
            }
            DropHands(h);
            return true;
        }

        static void DropHands(PlayerHands h) => h.Gesture[HandsDef.Left] = h.Gesture[HandsDef.Right] = HandsDef.NoGesture;

        /// <summary>The hands as text, "" when none has changed (so worlds without any hash as they did).</summary>
        public string HandsText() => ProgressText() + RunText(Hold) + string.Concat(hands.Values.Select(h => $"|P{h.Player}:{h.Holds[0]}:{h.Holds[1]}:{Whole(h.Ready[0])}:{Whole(h.Ready[1])}:{Whole(h.Gesture[0])}:{Whole(h.Gesture[1])}"));

        static JArray SaveHands(PlayerHands h) => new JArray(h.Player, h.Holds[0], h.Holds[1], h.Ready[0], h.Ready[1], h.Gesture[0], h.Gesture[1]);

        void LoadHands(JToken saved)
        {
            foreach (var a in saved ?? new JArray())
            {
                var h = new PlayerHands { Player = (string)a[0] };
                for (int i = 0; i < HandsDef.Count; i++)
                {
                    h.Holds[i] = (string)a[1 + i];
                    h.Ready[i] = (int)a[3 + i];
                    h.Gesture[i] = (int)a[5 + i];
                }
                hands[h.Player] = h;
            }
        }
    }
}
