// A wizard's hands, in the world and the Hold (D16, docs/design/unified-casting.md; the Hold's side in the-hold.md, phase 3). A hand
// holds an element or nothing, changed only by a command: at once in the attuning room, or after a short moment anywhere while
// the Hold isn't running, and never mid-cast. One element attuned is on both hands. A hand's button is a gesture with a meter of
// its own: a tap goes off at charge 1 when the meter runs out, and a button held down charges as World.Combos.cs's element casts
// do, a level every chargeTicks, until its release. Inside the window that hand's other button makes it a sweep. While both hands
// make the same gesture and the table has a row for what they hold, they merge: one cast, which a release of either sets off at
// the lower of their two charges, with both hands' cooldowns. Anything else, each hand goes off on its own, so one can tap while
// the other charges, and the chemistry decides what they make. Commands are ComboCommands, so they're stamped and replayed.
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

        /// <summary>The ground a wizard stands on to attune at this tower: the open cells within its reach, nearest the keep's heart first (inside the walls), then in row order. The view decks them out.</summary>
        public List<(int x, int y)> TowerGround(HoldTower tower)
        {
            int reach = Rules.Sim.Hold.TowerReach;
            var cells = new List<(int x, int y)>();
            for (int y = tower.Y - reach; y <= tower.Y + reach; y++)
                for (int x = tower.X - reach; x <= tower.X + reach; x++)
                    if (At(x, y) is { } c && !IsBlocking(c)) cells.Add((x, y));
            var (hx, hy) = HoldSite.Heart;
            return cells.OrderBy(c => System.Math.Abs(c.x - hx) + System.Math.Abs(c.y - hy)).ThenBy(c => c.y).ThenBy(c => c.x).ToList();
        }

        /// <summary>The tower this wizard stands up at, whose element its hands may attune to; null at none (hold-batch).</summary>
        public HoldTower TowerOf(string player) =>
            HoldSite != null && stands.TryGetValue(player, out var s) && !s.Down ? HoldSite.TowerAt(s.X, s.Y, Rules.Sim.Hold.TowerReach) : null;

        /// <summary>Whether this wizard's buttons are its hands: everywhere the rules have a hand table (D16).</summary>
        public bool HandsOn(string player) => HandRules.Any;

        // In a run or at a tower, where the Hold's own rules (its kit, its towers) hold.
        bool InHold(string player) => HoldRunning || TowerOf(player) != null;

        /// <summary>Whether a stand on this cell, now, is one the Hold reads, so a late joiner's history must keep it.</summary>
        public bool HoldReadsStand(int x, int y) => HoldSite != null && (HoldRunning || HoldSite.Plate == (x, y) || HoldSite.TowerAt(x, y, Rules.Sim.Hold.TowerReach) != null);

        /// <summary>The D-pad's list for this wizard: the kit in the Hold, else the combo table's.</summary>
        public IReadOnlyList<DpadSpellDef> DpadOf(string player)
        {
            if (!InHold(player) || HandRules.Kit.Count == 0) return Rules.Combos.Dpad;
            return AllOpen(player) ? HandRules.Kit : HandRules.Kit.Where(k => KitOpen(player, k.Spell)).ToList();
        }

        /// <summary>The ticks until this hand casts again; 0 when it's ready.</summary>
        public int HandCooling(string player, int hand) => System.Math.Max(0, HandsOf(player).Ready[hand] - Tick);

        /// <summary>The charge this hand's gesture has reached: 1 for a tap, up to its spell's most while its button is down.</summary>
        public int HandCharge(string player, int hand) => ChargeOf(HandsOf(player), hand);

        /// <summary>What this hand brings out: what it holds, or with nothing, what the other hand holds (one element is on both).</summary>
        public string HandHolds(string player, int hand) => HoldsNow(HandsOf(player), hand);

        static string HoldsNow(PlayerHands h, int hand) => h.Holds[hand] ?? h.Holds[1 - hand];

        /// <summary>The first spell a hand cast under way will bring out, or null.</summary>
        public string HandSpellUnderWay(string player) => hands.TryGetValue(player, out var h) && h.Casting ? Coming(h).Select(c => c.row.Spell).FirstOrDefault(s => s != null) : null;

        /// <summary>Every spell a hand cast under way will bring out: two gestures together are two.</summary>
        public List<string> HandSpellsUnderWay(string player) => hands.TryGetValue(player, out var h) && h.Casting ? Coming(h).Select(c => c.row.Spell).Where(s => s != null).ToList() : new List<string>();

        const int BothHands = -1;

        // The row both hands make together now: the same gesture on both, and a row for what they hold; null when they don't merge.
        HandRowDef Merged(PlayerHands h)
        {
            int left = h.Gesture[HandsDef.Left];
            return left != HandsDef.NoGesture && left == h.Gesture[HandsDef.Right] ? HandRules.Both(HoldsNow(h, HandsDef.Left), HoldsNow(h, HandsDef.Right), left) : null;
        }

        // What the hands bring out as they stand: a merged row with both hands, or each hand's own.
        List<(HandRowDef row, int gesture, int hand)> Coming(PlayerHands h)
        {
            var coming = new List<(HandRowDef, int, int)>();
            if (Merged(h) is { } row)
            {
                coming.Add((row, h.Gesture[HandsDef.Left], BothHands));
                return coming;
            }
            for (int hand = 0; hand < HandsDef.Count; hand++)
                if (h.Gesture[hand] != HandsDef.NoGesture && HandRules.One(HoldsNow(h, hand), h.Gesture[hand]) is { } own) coming.Add((own, h.Gesture[hand], hand));
            return coming;
        }

        PlayerHands OwnHands(string player)
        {
            var h = HandsOf(player);
            hands[player] = h;
            return h;
        }

        // A null element empties the hand. It always takes a moment (attuneTicks). At a tower a hand takes only the tower's element,
        // run or not; in a run only a tower attunes; elsewhere, anything, anywhere.
        bool Attune(PlayerCast c, int hand, string element)
        {
            string player = c.Player;
            if (hand < 0 || hand >= HandsDef.Count || (element != null && !Rules.Combos.Elements.Contains(element)) || !HandOpen(player, hand) || !ElementOpen(player, element)) return false;
            if (HandsOf(player).Busy || c.Casting) return false;
            var tower = TowerOf(player);
            if (tower != null ? element != tower.Element : HoldRunning) return false;
            if (stands.TryGetValue(player, out var s) && s.Down) return false;
            var h = OwnHands(player);
            h.AttuneHand = hand;
            h.AttuneTo = element;
            h.AttuneLeft = System.Math.Max(1, HandRules.AttuneTicks);
            Mirror(c, h);
            return true;
        }

        // A hand's button: a thrust or a throw, or with the hand's other button inside the window, a sweep. Held, it charges.
        bool PressHand(PlayerCast c, int hand, int gesture, bool held)
        {
            if (!HandsOn(c.Player) || hand < 0 || hand >= HandsDef.Count || !HandOpen(c.Player, hand) || !GestureOpen(c.Player, gesture)) return false;
            var h = HandsOf(c.Player);
            if (h.Ready[hand] > Tick || h.AttuneLeft > 0 || (c.Casting && !h.Casting)) return false;
            if (h.Gesture[hand] == HandsDef.NoGesture)
            {
                h = OwnHands(c.Player);
                h.Gesture[hand] = gesture;
                h.Since[hand] = Tick;
                h.Down[hand] = held;
            }
            else if (Tick - h.Since[hand] >= HandRules.Window || !Sweeps(h, hand, gesture, GestureOpen(c.Player, HandsDef.Sweep))) return false;
            else h.Down[hand] |= held;
            StartHand(h, hand);
            // A merge goes off on either hand's release, and a tap is one.
            if (Merged(h) != null && h.Down[HandsDef.Left] != h.Down[HandsDef.Right]) LetGo(h);
            Mirror(c, h);
            return true;
        }

        static bool Sweeps(PlayerHands h, int hand, int gesture, bool sweeps)
        {
            int was = h.Gesture[hand];
            if (was == HandsDef.Sweep || was == gesture || !sweeps) return false;
            h.Gesture[hand] = HandsDef.Sweep;
            return true;
        }

        // A hand's meter, for what it brings out now: a merged row's time if it has merged. Held, there's room for the full charge.
        void StartHand(PlayerHands h, int hand)
        {
            var row = Merged(h) ?? HandRules.One(HoldsNow(h, hand), h.Gesture[hand]);
            h.Extra[hand] = h.Down[hand] ? (MaxChargeOf(row) - 1) * Rules.Combos.ChargeTicks : 0;
            h.Meter[hand] = h.Total[hand] = System.Math.Max(1, row?.CastTicks ?? 1) + h.Extra[hand];
            h.Charge[hand] = 1;
        }

        int MaxChargeOf(HandRowDef row) => Chargeable(Rules.Spell(row?.Spell)) ? MaxCharge : 1;

        int ChargeOf(PlayerHands h, int hand) => h.Down[hand] ? ChargeOn(h.Extra[hand], h.Meter[hand]) : h.Charge[hand];

        // A hand's release: alone it goes off at its own charge, merged both do at the lower of theirs.
        bool ReleaseHand(PlayerCast c, int hand)
        {
            if (hand < 0 || hand >= HandsDef.Count || !hands.TryGetValue(c.Player, out var h) || !h.Down[hand]) return false;
            if (Merged(h) != null) LetGo(h);
            else EndHandHold(h, hand, ChargeOf(h, hand));
            Mirror(c, h);
            return true;
        }

        void LetGo(PlayerHands h)
        {
            int charge = System.Math.Min(ChargeOf(h, HandsDef.Left), ChargeOf(h, HandsDef.Right));
            EndHandHold(h, HandsDef.Left, charge);
            EndHandHold(h, HandsDef.Right, charge);
        }

        // As EndHold: the hold's extra ticks come off, so a hand let go early still takes a tap's time.
        static void EndHandHold(PlayerHands h, int hand, int charge)
        {
            h.Charge[hand] = charge;
            if (!h.Down[hand]) return;
            int passed = h.Total[hand] - h.Meter[hand];
            h.Meter[hand] = System.Math.Max(1, h.Meter[hand] - h.Extra[hand]);
            h.Total[hand] = passed + h.Meter[hand];
            h.Extra[hand] = 0;
            h.Down[hand] = false;
        }

        // A tick of one wizard's hands: an attuning moment runs out, each meter drains (a held one waits at its last tick),
        // and what's run out goes off: a merge once both hands have, anything else hand by hand.
        void TickHands(PlayerCast c, PlayerHands h)
        {
            if (h.AttuneLeft > 0 && --h.AttuneLeft == 0) h.Holds[h.AttuneHand] = h.AttuneTo;
            for (int i = 0; i < HandsDef.Count; i++)
                if (h.Gesture[i] != HandsDef.NoGesture && h.Meter[i] > 0 && !(h.Down[i] && h.Meter[i] == 1)) h.Meter[i]--;
            if (Merged(h) is { } row)
            {
                if (h.Meter[HandsDef.Left] == 0 && h.Meter[HandsDef.Right] == 0) FireHand(c, h, row, h.Gesture[HandsDef.Left], BothHands);
            }
            else
            {
                for (int i = 0; i < HandsDef.Count; i++)
                    if (h.Gesture[i] != HandsDef.NoGesture && h.Meter[i] == 0) FireHand(c, h, HandRules.One(HoldsNow(h, i), h.Gesture[i]), h.Gesture[i], i);
            }
            Mirror(c, h);
        }

        // What a mod took away since the gesture began just doesn't go off. A hand's cooldown starts as it fires.
        void FireHand(PlayerCast c, PlayerHands h, HandRowDef row, int gesture, int hand)
        {
            bool both = hand == BothHands;
            if (row != null)
            {
                var spell = Rules.Spell(row.Spell);
                string holds = both ? CombosDef.IdOf(HoldsNow(h, HandsDef.Left), HoldsNow(h, HandsDef.Right)) : HoldsNow(h, hand);
                int charge = both ? System.Math.Min(h.Charge[HandsDef.Left], h.Charge[HandsDef.Right]) : h.Charge[hand];
                NoteFired(new ComboFired(c.Player, null, spell?.Name ?? row.Act, row.Spell, spell?.Shape, false, charge).FromHand(row.Act, RangeOf(c.Player, holds, gesture)));
                if (row.Spell != null)
                {
                    int ready = Tick + CooldownOf(c.Player, holds, gesture, both);
                    if (both) h.Ready[HandsDef.Left] = h.Ready[HandsDef.Right] = ready;
                    else h.Ready[hand] = ready;
                }
            }
            for (int i = 0; i < HandsDef.Count; i++)
                if (both || i == hand) DropHand(h, i);
        }

        static void DropHand(PlayerHands h, int hand)
        {
            h.Gesture[hand] = HandsDef.NoGesture;
            h.Meter[hand] = h.Total[hand] = h.Extra[hand] = h.Since[hand] = 0;
            h.Down[hand] = false;
            h.Charge[hand] = 1;
        }

        static void DropHands(PlayerHands h)
        {
            DropHand(h, HandsDef.Left);
            DropHand(h, HandsDef.Right);
            h.AttuneLeft = 0;
            h.AttuneTo = null;
            h.AttuneHand = 0;
        }

        // The cast state shows the hands' longest meter, so what reads a cast (the move factor, the view's bar, a refused Load)
        // sees them, and the merge, so the view can show it while the hands are held. An attuning moment is a meter too.
        void Mirror(PlayerCast c, PlayerHands h)
        {
            int at = -1;
            for (int i = 0; i < HandsDef.Count; i++)
                if (h.Gesture[i] != HandsDef.NoGesture && (at < 0 || h.Meter[i] > h.Meter[at])) at = i;
            c.Window = 0;
            c.Charge = 1;
            c.Merge = Merged(h)?.Spell;
            c.Holding = h.Down[HandsDef.Left] || h.Down[HandsDef.Right];
            c.Meter = at >= 0 ? System.Math.Max(1, h.Meter[at]) : h.AttuneLeft;
            c.Total = at >= 0 ? System.Math.Max(c.Meter, h.Total[at]) : System.Math.Max(h.AttuneLeft, HandRules.AttuneTicks);
            c.Extra = at >= 0 ? h.Extra[at] : 0;
            if (!h.Busy) c.Total = 0;
        }

        /// <summary>The hands as text, "" when none has changed (so worlds without any hash as they did).</summary>
        public string HandsText() => ProgressText() + RunText(Hold) + string.Concat(hands.Values.Select(h => $"|P{h.Player}:{h.Holds[0]}:{h.Holds[1]}:{Whole(h.Ready[0])}:{Whole(h.Ready[1])}:{Whole(h.Gesture[0])}:{Whole(h.Gesture[1])}" + MeterText(h)));

        // Each hand's meter, only while one is under way or attuning (so hands at rest hash as they did).
        static string MeterText(PlayerHands h) => !h.Busy ? "" : string.Concat(Enumerable.Range(0, HandsDef.Count).Select(i =>
            $":{Whole(h.Meter[i])}/{Whole(h.Total[i])}/{Whole(h.Extra[i])}/{(h.Down[i] ? 1 : 0)}/{Whole(h.Charge[i])}/{Whole(h.Since[i])}")) + $":a{Whole(h.AttuneHand)}/{h.AttuneTo}/{Whole(h.AttuneLeft)}";

        static JArray SaveHands(PlayerHands h) => new JArray(h.Player, h.Holds[0], h.Holds[1], h.Ready[0], h.Ready[1], h.Gesture[0], h.Gesture[1],
            new JArray(h.Meter), new JArray(h.Total), new JArray(h.Extra), new JArray(h.Down), new JArray(h.Charge), new JArray(h.Since), h.AttuneHand, h.AttuneTo, h.AttuneLeft);

        void LoadHands(JToken saved)
        {
            foreach (var a in saved ?? new JArray())
            {
                var h = new PlayerHands { Player = (string)a[0] };
                var more = a is JArray row && row.Count > 7 ? row : null;
                for (int i = 0; i < HandsDef.Count; i++)
                {
                    h.Holds[i] = (string)a[1 + i];
                    h.Ready[i] = (int)a[3 + i];
                    h.Gesture[i] = (int)a[5 + i];
                    // Saves from before the hands had meters of their own: a gesture under way goes off on the next tick.
                    h.Meter[i] = (int?)more?[7]?[i] ?? (h.Gesture[i] == HandsDef.NoGesture ? 0 : 1);
                    h.Total[i] = (int?)more?[8]?[i] ?? h.Meter[i];
                    h.Extra[i] = (int?)more?[9]?[i] ?? 0;
                    h.Down[i] = (bool?)more?[10]?[i] ?? false;
                    h.Charge[i] = (int?)more?[11]?[i] ?? 1;
                    h.Since[i] = (int?)more?[12]?[i] ?? 0;
                }
                h.AttuneHand = (int?)more?[13] ?? 0;
                h.AttuneTo = (string)more?[14];
                h.AttuneLeft = (int?)more?[15] ?? 0;
                hands[h.Player] = h;
            }
        }
    }
}
