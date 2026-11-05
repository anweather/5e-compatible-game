// A wizard's hands in the Hold (docs/design/the-hold.md, phase 3). Each hand holds an element, changed only by a command and
// only in the attuning room. During a run a hand's button starts its spell on the cast meter of World.Combos.cs; the other hand
// can join inside the window, which runs within that cast, so a one-hand spell goes off when it would have anyway. When a spell
// goes off the hands that cast it cool down. Commands are ComboCommands, so they're stamped and replayed like the rest.
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
            for (int i = 0; i < HandsDef.Count; i++) h.Element[i] = HandRules.Start[i];
            return h;
        }

        /// <summary>Whether this wizard stands up in the attuning room.</summary>
        public bool InAttuning(string player) => HoldSite != null && stands.TryGetValue(player, out var s) && !s.Down && HoldSite.InAttuning(s.X, s.Y);

        /// <summary>Whether this wizard's buttons are its hands: in a run, and in the attuning room.</summary>
        public bool HandsOn(string player) => HoldRunning || InAttuning(player);

        /// <summary>Whether a stand on this cell, now, is one the Hold reads, so a late joiner's history must keep it.</summary>
        public bool HoldReadsStand(int x, int y) => HoldSite != null && (HoldRunning || HoldSite.Plate == (x, y) || HoldSite.InAttuning(x, y));

        /// <summary>The D-pad's list for this wizard: the kit during a run, else the combo table's.</summary>
        public IReadOnlyList<DpadSpellDef> DpadOf(string player) => HandsOn(player) && HandRules.Kit.Count > 0 ? HandRules.Kit : Rules.Combos.Dpad;

        /// <summary>The ticks until this hand casts again; 0 when it's ready.</summary>
        public int HandCooling(string player, int hand) => System.Math.Max(0, HandsOf(player).Ready[hand] - Tick);

        /// <summary>The spell a hand cast under way will go off as, or null.</summary>
        public string HandSpellUnderWay(string player) => hands.TryGetValue(player, out var h) && h.Casting ? SpellOf(h)?.Spell : null;

        HandSpellDef SpellOf(PlayerHands h) => h.Both
            ? HandRules.Spell(h.Element[HandsDef.Left], h.Element[HandsDef.Right], h.Modified)
            : HandRules.Spell(h.Element[h.First], null, h.Modified);

        PlayerHands OwnHands(string player)
        {
            var h = HandsOf(player);
            hands[player] = h;
            return h;
        }

        bool Attune(string player, int hand, string element)
        {
            if (hand < 0 || hand >= HandsDef.Count || !Rules.Combos.Elements.Contains(element) || !InAttuning(player) || HandsOf(player).Casting) return false;
            OwnHands(player).Element[hand] = element;
            return true;
        }

        // The first hand starts the meter; the other, inside the window, starts it again for the two-hand spell.
        bool CastHand(PlayerCast c, int hand, bool modified)
        {
            if (!HoldRunning || hand < 0 || hand >= HandsDef.Count) return false;
            var h = HandsOf(c.Player);
            if (h.Ready[hand] > Tick) return false;
            return c.Casting ? JoinHand(c, h, hand, modified) : StartHand(c, h, hand, modified);
        }

        bool StartHand(PlayerCast c, PlayerHands h, int hand, bool modified)
        {
            var spell = HandRules.Spell(h.Element[hand], null, modified);
            if (spell == null) return false;
            h = OwnHands(c.Player);
            h.First = hand;
            h.Both = false;
            h.Modified = modified;
            c.Window = HandRules.Window;
            StartMeter(c, spell.CastTicks);
            return true;
        }

        bool JoinHand(PlayerCast c, PlayerHands h, int hand, bool modified)
        {
            if (!h.Casting || h.Both || h.First == hand || c.Window <= 0) return false;
            var spell = HandRules.Spell(h.Element[HandsDef.Left], h.Element[HandsDef.Right], h.Modified || modified);
            if (spell == null) return false;
            h.Both = true;
            h.Modified |= modified;
            c.Window = 0;
            StartMeter(c, spell.CastTicks);
            return true;
        }

        // True if this was a hand's cast, gone off now.
        bool FireHands(PlayerCast c)
        {
            if (!hands.TryGetValue(c.Player, out var h) || !h.Casting) return false;
            var spell = SpellOf(h);
            if (spell != null) NoteFired(new ComboFired(c.Player, null, Rules.Spell(spell.Spell)?.Name ?? spell.Spell, spell.Spell, Rules.Spell(spell.Spell)?.Shape, false));
            if (h.Both) h.Ready[HandsDef.Left] = h.Ready[HandsDef.Right] = Tick + HandRules.BothCooldown;
            else h.Ready[h.First] = Tick + HandRules.HandCooldown;
            DropHands(h);
            return true;
        }

        static void DropHands(PlayerHands h)
        {
            h.First = -1;
            h.Both = h.Modified = false;
        }

        /// <summary>The hands as text, "" when none has changed (so worlds without any hash as they did).</summary>
        public string HandsText() => string.Concat(hands.Values.Select(h => $"|P{h.Player}:{h.Element[0]}:{h.Element[1]}:{Whole(h.Ready[0])}:{Whole(h.Ready[1])}:{Whole(h.First)}:{(h.Both ? 1 : 0)}:{(h.Modified ? 1 : 0)}"));

        static JArray SaveHands(PlayerHands h) => new JArray(h.Player, h.Element[0], h.Element[1], h.Ready[0], h.Ready[1], h.First, h.Both, h.Modified);

        void LoadHands(JToken saved)
        {
            foreach (var a in saved ?? new JArray())
            {
                var h = new PlayerHands { Player = (string)a[0], First = (int)a[5], Both = (bool)a[6], Modified = (bool)a[7] };
                h.Element[0] = (string)a[1];
                h.Element[1] = (string)a[2];
                h.Ready[0] = (int)a[3];
                h.Ready[1] = (int)a[4];
                hands[h.Player] = h;
            }
        }
    }
}
