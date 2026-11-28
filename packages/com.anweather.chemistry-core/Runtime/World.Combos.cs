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

        public IReadOnlyList<ComboFired> CombosFired => combosFired;

        readonly List<(ComboFired fired, int tick)> firedHistory = new List<(ComboFired fired, int tick)>();

        internal void CopyCombosFiredSince(int tick, List<ComboFired> into)
        {
            foreach (var f in firedHistory) if (f.tick > tick) into.Add(f.fired);
        }

        void NoteFired(ComboFired fired)
        {
            combosFired.Add(fired);
            firedHistory.Add((fired, Tick));
        }

        public PlayerCast CastOf(string player) => casts.TryGetValue(player, out var c) ? c : null;

        public ComboDef ComboOf(PlayerCast c) => c?.First == null ? null : Rules.Combos.Combo(CombosDef.IdOf(c.First, c.Second));

        public int MaxChargeOf(ComboDef combo) => Chargeable(Rules.Spell(combo?.Spell)) ? MaxCharge : 1;

        public int HeldCharge(PlayerCast c) => c?.Holding == true ? ChargeOn(c.Extra, c.Meter) : 1;

        int ChargeOn(int extra, int meter) => 1 + Math.Max(0, extra + 1 - meter) / Rules.Combos.ChargeTicks;

        public double SecondsPerLevel => Rules.Combos.ChargeTicks / (double)Rules.Sim.TicksPerSecond;

        public double MoveFactorOf(string player)
        {
            var c = CastOf(player);
            if (c?.Casting != true) return 1;
            return c.Charging ? Rules.Combos.ChargeMoveFactor : Rules.Combos.MoveFactor;
        }

        public bool KnowsCombo(string player, string combo) => CastOf(player)?.Known.Contains(combo) == true;

        public DpadSpellDef SelectedDpad(string player)
        {
            var dpad = DpadOf(player);
            return dpad.Count == 0 ? null : dpad[(CastOf(player)?.Slot ?? 0) % dpad.Count];
        }

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
                case ComboCommandKind.Release: return ReleaseHold(c);
                case ComboCommandKind.Hand: return PressHand(c, command.Step, command.Hold ? HandsDef.Throw : HandsDef.Thrust, false);
                case ComboCommandKind.HandDown: return PressHand(c, command.Step, command.Hold ? HandsDef.Throw : HandsDef.Thrust, true);
                case ComboCommandKind.HandUp: return ReleaseHand(c, command.Step);
                case ComboCommandKind.Moment: return StartMoment(command.Element);
                case ComboCommandKind.Pick: return PickUpgrade(command.Player, command.Element);
                case ComboCommandKind.Bring: return BringPoints(command.Player, command.Step);
                case ComboCommandKind.Attune: return Attune(c, command.Step, command.Element);
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
                StartHeldMeter(c, combos.Combo(CombosDef.IdOf(element, null)));
                return true;
            }
            c.Second = element;
            c.Window = 0;
            StartHeldMeter(c, combos.Combo(CombosDef.IdOf(c.First, element)));
            return true;
        }

        bool CancelCast(PlayerCast c)
        {
            if (!c.Casting) return false;
            if (hands.TryGetValue(c.Player, out var h)) DropHands(h);
            ClearCast(c);
            return true;
        }

        bool ReleaseHold(PlayerCast c)
        {
            if (!c.Holding) return false;
            c.Charge = HeldCharge(c);
            EndHold(c);
            return true;
        }

        static void EndHold(PlayerCast c)
        {
            int passed = c.Total - c.Meter;
            c.Meter = Math.Max(1, c.Meter - c.Extra);
            c.Total = passed + c.Meter;
            c.Extra = 0;
            c.Holding = false;
        }

        bool CycleDpad(PlayerCast c, int step)
        {
            int n = DpadOf(c.Player).Count;
            if (n == 0 || step == 0) return false;
            c.Slot = (((c.Slot + Math.Sign(step)) % n) + n) % n;
            return true;
        }

        bool CastSelected(PlayerCast c)
        {
            var d = SelectedDpad(c.Player);
            if (c.Casting || d?.Spell == null) return false;
            c.DpadCasting = c.Slot % DpadOf(c.Player).Count;
            StartMeter(c, d.CastTicks);
            return true;
        }

        static void StartMeter(PlayerCast c, int ticks)
        {
            c.Meter = ticks;
            c.Total = ticks;
            c.Extra = 0;
        }

        void StartHeldMeter(PlayerCast c, ComboDef combo)
        {
            StartMeter(c, combo?.CastTicks ?? 0);
            if (!c.Holding) return;
            c.Extra = (MaxChargeOf(combo) - 1) * Rules.Combos.ChargeTicks;
            c.Meter += c.Extra;
            c.Total += c.Extra;
        }

        static void ClearCast(PlayerCast c)
        {
            c.First = c.Second = null;
            c.Meter = c.Total = c.Window = c.Extra = 0;
            c.DpadCasting = -1;
            c.Holding = false;
            c.Charge = 1;
            c.Merge = null;
        }

        void TickCombos()
        {
            foreach (var c in casts.Values)
            {
                if (hands.TryGetValue(c.Player, out var h) && h.Busy)
                {
                    TickHands(c, h);
                    continue;
                }
                if (!c.Casting) continue;
                if (c.Window > 0) c.Window--;
                if (c.Holding && c.Meter == 1) continue;
                if (--c.Meter == 0) FireCast(c);
            }
        }

        void FireCast(PlayerCast c)
        {
            var dpad = DpadOf(c.Player);
            if (c.DpadCasting >= 0)
            {
                var d = c.DpadCasting < dpad.Count ? dpad[c.DpadCasting] : null;
                if (d?.Spell != null) NoteFired(new ComboFired(c.Player, null, d.Name, d.Spell, Rules.Spell(d.Spell)?.Shape, false));
            }
            else
            {
                var def = ComboOf(c);
                if (def != null)
                {
                    NoteFired(new ComboFired(c.Player, def.Id, def.Name, def.Spell, def.Shape, c.Known.Add(def.Id), c.Charge));
                }
            }
            ClearCast(c);
        }

        public string CastHash() => Fnv1a.Hash(CastText(StrikeText()));

        public string CastText(string stands) =>
            CastState() + WhirlState() + WaveState() + BoulderState() + ModesText() + ZoneText() + CourseText() + RoomText() + stands + HoldText() + HandsText() + PlacedText() + ActText() + LaidText() + TriggerText();

        public string WhirlState() => string.Concat(whirls.Select(ww => $"|{Whole(ww.X)}:{Whole(ww.Y)}:{Whole(ww.Age)}:{ww.Carrying ?? "-"}"));

        public string CastState() => string.Join("/", casts.Values.Select(c => string.Join(":",
            c.Player, c.First ?? "-", c.Second ?? "-", Whole(c.Meter), Whole(c.Total), Whole(c.Window), Whole(c.Slot), Whole(c.DpadCasting), string.Join(",", c.Known)) + ChargeState(c)));

        static string ChargeState(PlayerCast c) => (c.Holding ? ":hold" : "") + (c.Holding || c.Charge > 1 ? ":c" + Whole(c.Charge) : "");

        public static string Whole(int n) => n.ToString(CultureInfo.InvariantCulture);
    }
}
