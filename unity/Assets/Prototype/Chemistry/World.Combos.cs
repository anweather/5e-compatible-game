// Casting by elements (docs/SPELL_COMBOS.md): each player loads one element or
// an ordered pair, a meter drains a tick at a time, and at zero the combo goes
// off and joins the player's grimoire. The d-pad's spells drain the same meter.
// Every peer runs the same ComboCommands, so this state agrees like the rest of
// the world; CastHash is for the netcode checksum beside CellHash. Port of
// sandbox/src/combos.ts.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace Chem
{
    public sealed partial class World
    {
        readonly SortedDictionary<string, PlayerCast> casts = new SortedDictionary<string, PlayerCast>(StringComparer.Ordinal);
        readonly List<ComboFired> combosFired = new List<ComboFired>();

        /// <summary>The casts that went off in the last tick.</summary>
        public IReadOnlyList<ComboFired> CombosFired => combosFired;

        /// <summary>A player's cast state, or null if they've sent no command yet.</summary>
        public PlayerCast CastOf(string player) => casts.TryGetValue(player, out var c) ? c : null;

        /// <summary>The combo a cast has loaded, or null (nothing loaded, or a d-pad spell).</summary>
        public ComboDef ComboOf(PlayerCast c) => c?.First == null ? null : Rules.Combos.Combo(CombosDef.IdOf(c.First, c.Second));

        /// <summary>The most a combo can be charged: <see cref="MaxCharge"/> if its spell is chargeable, else 1.</summary>
        public int MaxChargeOf(ComboDef combo) => Chargeable(Rules.Spell(combo?.Spell)) ? MaxCharge : 1;

        /// <summary>What the view multiplies this player's walking speed by: the rules' factor while the meter drains, else 1.</summary>
        public double MoveFactorOf(string player) => CastOf(player)?.Casting == true ? Rules.Combos.MoveFactor : 1;

        /// <summary>Whether the combo is in this player's grimoire.</summary>
        public bool KnowsCombo(string player, string combo) => CastOf(player)?.Known.Contains(combo) == true;

        /// <summary>The d-pad's selected spell for this player, or null if the list is empty.</summary>
        public DpadSpellDef SelectedDpad(string player)
        {
            var dpad = Rules.Combos.Dpad;
            return dpad.Count == 0 ? null : dpad[(CastOf(player)?.Slot ?? 0) % dpad.Count];
        }

        /// <summary>
        /// Applies a player's cast input, between ticks. False if it was refused: an element
        /// outside the window or past the second, a d-pad cast during a cast or of a slot with no
        /// spell, a cancel with nothing to drop, a release with nothing held.
        /// </summary>
        public bool ApplyCombo(ComboCommand command)
        {
            FinishTick();
            if (!casts.TryGetValue(command.Player, out var c)) casts[command.Player] = c = new PlayerCast { Player = command.Player };
            switch (command.Kind)
            {
                case ComboCommandKind.Load: return LoadElement(c, command.Element, command.Hold);
                case ComboCommandKind.Cancel: return CancelCast(c);
                case ComboCommandKind.Cycle: return CycleDpad(c, command.Step);
                case ComboCommandKind.CastSelected: return CastSelected(c);
                case ComboCommandKind.Release: return ReleaseHold(c, command.Charge);
                default: return false;
            }
        }

        bool LoadElement(PlayerCast c, string element, bool hold)
        {
            var combos = Rules.Combos;
            if (!combos.Elements.Contains(element) || !c.TakesElement) return false;
            c.Holding = hold;
            if (!c.Casting)
            {
                c.First = element;
                c.Window = combos.Window;
                StartMeter(c, combos.Combo(CombosDef.IdOf(element, null))?.CastTicks ?? 0);
                return true;
            }
            c.Second = element;
            c.Window = 0;
            StartMeter(c, combos.Combo(CombosDef.IdOf(c.First, element))?.CastTicks ?? 0);
            return true;
        }

        static bool CancelCast(PlayerCast c)
        {
            if (!c.Casting) return false;
            ClearCast(c);
            return true;
        }

        // The charge is kept to what the spell takes, here as well as by the view, so a client can't send more.
        bool ReleaseHold(PlayerCast c, int charge)
        {
            if (!c.Holding) return false;
            c.Holding = false;
            c.Charge = Math.Max(1, Math.Min(MaxChargeOf(ComboOf(c)), charge));
            return true;
        }

        bool CycleDpad(PlayerCast c, int step)
        {
            int n = Rules.Combos.Dpad.Count;
            if (n == 0 || step == 0) return false;
            c.Slot = (((c.Slot + Math.Sign(step)) % n) + n) % n;
            return true;
        }

        bool CastSelected(PlayerCast c)
        {
            var d = SelectedDpad(c.Player);
            if (c.Casting || d?.Spell == null) return false;
            c.DpadCasting = c.Slot % Rules.Combos.Dpad.Count;
            StartMeter(c, d.CastTicks);
            return true;
        }

        static void StartMeter(PlayerCast c, int ticks)
        {
            c.Meter = ticks;
            c.Total = ticks;
        }

        static void ClearCast(PlayerCast c)
        {
            c.First = c.Second = null;
            c.Meter = c.Total = c.Window = 0;
            c.DpadCasting = -1;
            c.Holding = false;
            c.Charge = 1;
        }

        // Drains every meter, in player order, and sets off those that reach zero. A held cast waits
        // at the meter's last tick, so the tick after its Release sets it off.
        void TickCombos()
        {
            foreach (var c in casts.Values)
            {
                if (!c.Casting) continue;
                if (c.Window > 0) c.Window--;
                if (c.Holding && c.Meter == 1) continue;
                if (--c.Meter == 0) FireCast(c);
            }
        }

        // What a mod took away since the cast began just doesn't go off.
        void FireCast(PlayerCast c)
        {
            var combos = Rules.Combos;
            if (c.DpadCasting >= 0)
            {
                var d = c.DpadCasting < combos.Dpad.Count ? combos.Dpad[c.DpadCasting] : null;
                if (d?.Spell != null) combosFired.Add(new ComboFired(c.Player, null, d.Name, d.Spell, Rules.Spell(d.Spell)?.Shape, false));
            }
            else
            {
                var def = ComboOf(c);
                if (def != null)
                {
                    string spell = def.SpellAt(c.Charge, MaxChargeOf(def));
                    string shape = spell == def.Spell ? def.Shape : Rules.Spell(spell)?.Shape;
                    combosFired.Add(new ComboFired(c.Player, def.Id, def.Name, spell, shape, c.Known.Add(def.Id), c.Charge));
                }
            }
            ClearCast(c);
        }

        /// <summary>A fingerprint (FNV-1a) of every player's cast state and grimoire and the whirlwinds still whirling, for the netcode checksum.</summary>
        public string CastHash() => Fnv1a.Hash(CastState() + WhirlState() + WaveState() + BoulderState() + GolfText());

        /// <summary>The whirlwinds as text, "" when there are none (so worlds without any hash as they did): where, its age, and what it carries.</summary>
        public string WhirlState() => string.Concat(whirls.Select(ww => $"|{Whole(ww.X)}:{Whole(ww.Y)}:{Whole(ww.Age)}:{ww.Carrying ?? "-"}"));

        /// <summary>
        /// The players' cast state as text, by player: "ann:fire:air:5:9:0:1:-1:fire,fire+air", which
        /// is player, elements ("-" for none), meter, total, window, slot, d-pad cast, then the grimoire;
        /// then, only while a cast is held or charged, ":hold:c1" or ":c3" (so other casts hash as they did).
        /// </summary>
        public string CastState() => string.Join("/", casts.Values.Select(c => string.Join(":",
            c.Player, c.First ?? "-", c.Second ?? "-", Whole(c.Meter), Whole(c.Total), Whole(c.Window), Whole(c.Slot), Whole(c.DpadCasting), string.Join(",", c.Known)) + ChargeState(c)));

        static string ChargeState(PlayerCast c) => (c.Holding ? ":hold" : "") + (c.Holding || c.Charge > 1 ? ":c" + Whole(c.Charge) : "");

        static string Whole(int n) => n.ToString(CultureInfo.InvariantCulture);
    }
}
