// A wizard's hands, in the world and the Hold (D16, docs/design/unified-casting.md; the Hold's side in the-hold.md, phase 3). A hand
// holds an element or nothing, changed only by a command: at once in the attuning room, or after a short moment anywhere while
// the Hold isn't running, and never mid-cast. One element attuned is on both hands. A hand's button is a gesture with a meter of
// its own: a tap, a button let go within tapTicks, goes off at charge 1 tapTicks after its press, with no wind-up, and rests
// tapCooldown; a button held down past that charges as World.Combos.cs's element casts do, its row's castTicks and a level
// every chargeTicks, until its release, and rests handCooldown. Inside the window that hand's other button makes it a sweep.
// While both hands make the same gesture and the table has a row for what they hold, they merge: one cast on the row's
// castTicks, tapped or held, which a release of either sets off at the lower of their two charges, with both hands' cooldowns. Anything else, each hand goes off on its own, so one can tap while
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

        public PlayerHands HandsOf(string player)
        {
            if (hands.TryGetValue(player, out var h)) return h;
            h = new PlayerHands { Player = player };
            for (int i = 0; i < HandsDef.Count; i++) h.Holds[i] = HandRules.Start[i];
            return h;
        }

        public List<(int x, int y)> TowerGround(HoldTower tower)
        {
            int reach = Rules.Sim.Hold.TowerReach;
            var cells = new List<(int x, int y)>();
            for (int y = tower.Y - reach; y <= tower.Y + reach; y++)
                for (int x = tower.X - reach; x <= tower.X + reach; x++)
                    if (At(x, y) is { } c && !IsBlocking(c)) cells.Add((x, y));
            var (hx, hy) = HoldSite.Heart;
            return cells.Select(c => (x: FoldX(c.x), y: FoldY(c.y))).OrderBy(c => System.Math.Abs(DeltaX(hx, c.x)) + System.Math.Abs(DeltaY(hy, c.y))).ThenBy(c => c.y).ThenBy(c => c.x).ToList();
        }

        public HoldTower TowerOf(string player) =>
            HoldSite != null && stands.TryGetValue(player, out var s) && !s.Down ? HoldSite.TowerAt(s.X, s.Y, Rules.Sim.Hold.TowerReach) : null;

        public bool HandsOn(string player) => HandRules.Any;

        bool InHold(string player) => HoldRunning || TowerOf(player) != null;

        public bool HoldReadsStand(int x, int y) => HoldSite != null && (HoldRunning || HoldSite.Plate == (x, y) || HoldSite.TowerAt(x, y, Rules.Sim.Hold.TowerReach) != null);

        public IReadOnlyList<DpadSpellDef> DpadOf(string player)
        {
            if (!InHold(player) || HandRules.Kit.Count == 0) return Rules.Combos.Dpad;
            return AllOpen(player) ? HandRules.Kit : HandRules.Kit.Where(k => KitOpen(player, k.Spell)).ToList();
        }

        public int HandCooling(string player, int hand) => System.Math.Max(0, HandsOf(player).Ready[hand] - Tick);

        public int HandCharge(string player, int hand) => ChargeOf(HandsOf(player), hand);

        public string HandHolds(string player, int hand) => HoldsNow(HandsOf(player), hand);

        static string HoldsNow(PlayerHands h, int hand) => h.Holds[hand] ?? h.Holds[1 - hand];

        public string HandSpellUnderWay(string player) => hands.TryGetValue(player, out var h) && h.Casting ? Coming(h).Select(c => c.row.Spell).FirstOrDefault(s => s != null) : null;

        public List<string> HandSpellsUnderWay(string player) => hands.TryGetValue(player, out var h) && h.Casting ? Coming(h).Select(c => c.row.Spell).Where(s => s != null).ToList() : new List<string>();

        const int BothHands = -1;

        HandRowDef Merged(PlayerHands h)
        {
            int left = h.Gesture[HandsDef.Left];
            return left != HandsDef.NoGesture && left == h.Gesture[HandsDef.Right] ? HandRules.Both(HoldsNow(h, HandsDef.Left), HoldsNow(h, HandsDef.Right), left) : null;
        }

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

        bool Attune(PlayerCast c, int hand, string element)
        {
            string player = c.Player;
            if (hand < 0 || hand >= HandsDef.Count || (element != null && !Rules.Combos.Elements.Contains(element)) || !HandOpen(player, hand) || !ElementOpen(player, element)) return false;
            if (HandsOf(player).Busy || c.Casting) return false;
            ReadStands();
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

        void StartHand(PlayerHands h, int hand)
        {
            var merged = Merged(h);
            var row = merged ?? HandRules.One(HoldsNow(h, hand), h.Gesture[hand]);
            h.Extra[hand] = h.Down[hand] ? (MaxChargeOf(row) - 1) * Rules.Combos.ChargeTicks : 0;
            h.Tap[hand] = merged == null && !h.Down[hand] && HandRules.TapTicks > 0;
            h.Meter[hand] = h.Total[hand] = (h.Tap[hand] ? TapTicksOf(row) : CastTicksOf(row)) + h.Extra[hand];
            h.Charge[hand] = 1;
        }

        static int CastTicksOf(HandRowDef row) => System.Math.Max(1, row?.CastTicks ?? 1);

        int TapTicksOf(HandRowDef row) => System.Math.Min(HandRules.TapTicks, CastTicksOf(row));

        int MaxChargeOf(HandRowDef row) => Chargeable(Rules.Spell(row?.Spell)) ? MaxCharge : 1;

        int ChargeOf(PlayerHands h, int hand) => h.Down[hand] ? ChargeOn(h.Extra[hand], h.Meter[hand]) : h.Charge[hand];

        bool ReleaseHand(PlayerCast c, int hand)
        {
            if (hand < 0 || hand >= HandsDef.Count || !hands.TryGetValue(c.Player, out var h) || !h.Down[hand]) return false;
            if (Merged(h) != null) LetGo(h);
            else if (HandRules.TapTicks > 0 && Tick - h.Since[hand] < HandRules.TapTicks) TapHand(h, hand);
            else EndHandHold(h, hand, ChargeOf(h, hand));
            Mirror(c, h);
            return true;
        }

        void TapHand(PlayerHands h, int hand)
        {
            int passed = h.Total[hand] - h.Meter[hand];
            h.Meter[hand] = System.Math.Max(1, TapTicksOf(HandRules.One(HoldsNow(h, hand), h.Gesture[hand])) - passed);
            h.Total[hand] = passed + h.Meter[hand];
            h.Extra[hand] = 0;
            h.Down[hand] = false;
            h.Charge[hand] = 1;
            h.Tap[hand] = true;
        }

        void LetGo(PlayerHands h)
        {
            int charge = System.Math.Min(ChargeOf(h, HandsDef.Left), ChargeOf(h, HandsDef.Right));
            EndHandHold(h, HandsDef.Left, charge);
            EndHandHold(h, HandsDef.Right, charge);
        }

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
                    int ready = Tick + CooldownOf(c.Player, holds, gesture, both, !both && h.Tap[hand], row.Cooldown);
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
            h.Down[hand] = h.Tap[hand] = false;
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

        public string HandsText() => ProgressText() + RunText(Hold) + string.Concat(hands.Values.Select(h => $"|P{h.Player}:{h.Holds[0]}:{h.Holds[1]}:{Whole(h.Ready[0])}:{Whole(h.Ready[1])}:{Whole(h.Gesture[0])}:{Whole(h.Gesture[1])}" + MeterText(h)));

        static string MeterText(PlayerHands h) => !h.Busy ? "" : string.Concat(Enumerable.Range(0, HandsDef.Count).Select(i =>
            $":{Whole(h.Meter[i])}/{Whole(h.Total[i])}/{Whole(h.Extra[i])}/{(h.Down[i] ? 1 : 0)}/{Whole(h.Charge[i])}/{Whole(h.Since[i])}" + (h.Tap[i] ? "/t" : ""))) + $":a{Whole(h.AttuneHand)}/{h.AttuneTo}/{Whole(h.AttuneLeft)}";

        static JArray SaveHands(PlayerHands h) => new JArray(h.Player, h.Holds[0], h.Holds[1], h.Ready[0], h.Ready[1], h.Gesture[0], h.Gesture[1],
            new JArray(h.Meter), new JArray(h.Total), new JArray(h.Extra), new JArray(h.Down), new JArray(h.Charge), new JArray(h.Since), h.AttuneHand, h.AttuneTo, h.AttuneLeft, new JArray(h.Tap));

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
                    h.Meter[i] = (int?)more?[7]?[i] ?? (h.Gesture[i] == HandsDef.NoGesture ? 0 : 1);
                    h.Total[i] = (int?)more?[8]?[i] ?? h.Meter[i];
                    h.Extra[i] = (int?)more?[9]?[i] ?? 0;
                    h.Down[i] = (bool?)more?[10]?[i] ?? false;
                    h.Charge[i] = (int?)more?[11]?[i] ?? 1;
                    h.Since[i] = (int?)more?[12]?[i] ?? 0;
                    h.Tap[i] = more?.Count > 16 && (bool?)more[16]?[i] == true;
                }
                h.AttuneHand = (int?)more?[13] ?? 0;
                h.AttuneTo = (string)more?[14];
                h.AttuneLeft = (int?)more?[15] ?? 0;
                hands[h.Player] = h;
            }
        }
    }
}
