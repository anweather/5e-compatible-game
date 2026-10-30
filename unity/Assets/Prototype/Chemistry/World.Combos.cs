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
        /// spell, a cancel with nothing to drop.
        /// </summary>
        public bool ApplyCombo(ComboCommand command)
        {
            FinishTick();
            if (!casts.TryGetValue(command.Player, out var c)) casts[command.Player] = c = new PlayerCast { Player = command.Player };
            switch (command.Kind)
            {
                case ComboCommandKind.Load: return LoadElement(c, command.Element);
                case ComboCommandKind.Cancel: return CancelCast(c);
                case ComboCommandKind.Cycle: return CycleDpad(c, command.Step);
                case ComboCommandKind.CastSelected: return CastSelected(c);
                default: return false;
            }
        }

        bool LoadElement(PlayerCast c, string element)
        {
            var combos = Rules.Combos;
            if (!combos.Elements.Contains(element)) return false;
            if (!c.Casting)
            {
                c.First = element;
                c.Window = combos.Window;
                StartMeter(c, combos.Combo(CombosDef.IdOf(element, null))?.CastTicks ?? 0);
                return true;
            }
            if (c.First == null || c.Second != null || c.Window == 0) return false;
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
        }

        // Drains every meter, in player order, and sets off those that reach zero.
        void TickCombos()
        {
            foreach (var c in casts.Values)
            {
                if (!c.Casting) continue;
                if (c.Window > 0) c.Window--;
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
                var def = combos.Combo(CombosDef.IdOf(c.First, c.Second));
                if (def != null) combosFired.Add(new ComboFired(c.Player, def.Id, def.Name, def.Spell, def.Shape, c.Known.Add(def.Id)));
            }
            ClearCast(c);
        }

        /// <summary>A fingerprint (FNV-1a) of every player's cast state and grimoire and the whirlwinds still whirling, for the netcode checksum.</summary>
        public string CastHash() => Fnv1a.Hash(CastState() + WhirlState());

        /// <summary>The whirlwinds as text, "" when there are none (so worlds without any hash as they did): where, its age, and what it carries.</summary>
        public string WhirlState() => string.Concat(whirls.Select(ww => $"|{Whole(ww.X)}:{Whole(ww.Y)}:{Whole(ww.Age)}:{ww.Carrying ?? "-"}"));

        /// <summary>
        /// The players' cast state as text, by player: "ann:fire:air:5:9:0:1:-1:fire,fire+air", which
        /// is player, elements ("-" for none), meter, total, window, slot, d-pad cast, then the grimoire.
        /// </summary>
        public string CastState() => string.Join("/", casts.Values.Select(c => string.Join(":",
            c.Player, c.First ?? "-", c.Second ?? "-", Whole(c.Meter), Whole(c.Total), Whole(c.Window), Whole(c.Slot), Whole(c.DpadCasting), string.Join(",", c.Known))));

        static string Whole(int n) => n.ToString(CultureInfo.InvariantCulture);
    }
}
