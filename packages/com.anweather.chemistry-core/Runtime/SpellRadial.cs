// The spell radial's state machine (docs/SPELL_RADIAL.md), fed one input
// sample a frame by the view: hold to open, aim at an element, rest on it or
// push further out to open its spells, release to pick. Two kinds of aiming:
// a pointer (mouse) moves a virtual cursor by its deltas, and releasing it in
// the centre cancels; a stick is absolute (its direction alone picks), and when
// it springs back to centre the last highlight stays, so cancelling is an
// explicit input (B). Also the steps that don't open the radial: next or
// previous spell in the element (Q / E, RB) and next or previous element
// (D-pad). It only reports a choice; the view casts nothing itself and still
// selects through SpellCaster.Select.
using System;
using System.Collections.Generic;
using System.Linq;

namespace Chem
{
    public sealed class RadialTuning
    {
        public double MouseDeadZone = 0.35;
        public double MousePushOut = 1.0;
        public double MouseMaxRadius = 1.6;
        public double StickDeadZone = 0.35;
        public double StickPushOut = 0.9;
        public double DwellSeconds = 0.35;
        public double TapSeconds = 0.2;
        public double SlotDegrees = 30;
        public double MaxArcDegrees = 300;
    }

    public enum RadialInputKind { Mouse, Stick }

    public readonly struct RadialInput
    {
        public RadialInput(RadialInputKind kind, double x, double y, bool held, double time, bool cancel = false)
        {
            Kind = kind;
            X = x;
            Y = y;
            Held = held;
            Time = time;
            Cancel = cancel;
        }

        public RadialInputKind Kind { get; }
        public double X { get; }
        public double Y { get; }
        public bool Held { get; }
        public double Time { get; }
        public bool Cancel { get; }

        public static RadialInput Mouse(double dx, double dy, bool held, double time, bool cancel = false) =>
            new RadialInput(RadialInputKind.Mouse, dx, dy, held, time, cancel);

        public static RadialInput Stick(double x, double y, bool held, double time, bool cancel = false) =>
            new RadialInput(RadialInputKind.Stick, x, y, held, time, cancel);
    }

    public enum RadialState
    {
        Closed,
        Elements,
        Spells,
    }

    public enum RadialOutcome
    {
        None,
        Selected,
        Cancelled,
        SwapPrevious,
    }

    public readonly struct RadialResult
    {
        public RadialResult(RadialOutcome outcome, string spellId)
        {
            Outcome = outcome;
            SpellId = spellId;
        }

        public RadialOutcome Outcome { get; }
        public string SpellId { get; }

        public static readonly RadialResult None = new RadialResult(RadialOutcome.None, null);
    }

    public sealed class SpellRadial
    {
        readonly RadialTuning tuning;
        readonly Dictionary<string, string> lastInGroup = new Dictionary<string, string>(StringComparer.Ordinal);
        double cursorX, cursorY, openedAt, dwellFrom;
        bool moved;

        public SpellRadial(RadialLayout layout, RadialTuning tuning = null)
        {
            Layout = layout ?? throw new ArgumentNullException(nameof(layout));
            this.tuning = tuning ?? new RadialTuning();
        }

        public RadialLayout Layout { get; set; }
        public RadialTuning Tuning => tuning;
        public RadialState State { get; private set; } = RadialState.Closed;
        public bool IsOpen => State != RadialState.Closed;
        public int Element { get; private set; } = -1;
        public int Spell { get; private set; } = -1;
        public double CursorX => cursorX;
        public double CursorY => cursorY;
        public string Current { get; private set; }
        public string Previous { get; private set; }

        public string HighlightedSpell => Element >= 0 && Spell >= 0 ? Layout.Groups[Element].SpellIds[Spell] : null;

        public void Chose(string spellId)
        {
            if (spellId == null) return;
            int g = Layout.GroupOfSpell(spellId);
            if (g >= 0) lastInGroup[Layout.Groups[g].Id] = spellId;
            if (spellId == Current) return;
            Previous = Current;
            Current = spellId;
        }

        public string RememberedSpell(int element)
        {
            var g = Layout.Groups[element];
            return lastInGroup.TryGetValue(g.Id, out var id) && g.SpellIds.Contains(id) ? id : g.SpellIds[0];
        }

        public RadialResult Feed(RadialInput input)
        {
            if (State == RadialState.Closed)
            {
                if (!input.Held || Layout.Count == 0) return RadialResult.None;
                Open(input.Time);
            }
            if (input.Cancel) return Close(RadialOutcome.Cancelled, null);
            if (!input.Held) return Release(input.Time);

            bool stick = input.Kind == RadialInputKind.Stick;
            double x = input.X, y = input.Y;
            if (!stick)
            {
                cursorX += input.X;
                cursorY += input.Y;
                double len = Math.Sqrt(cursorX * cursorX + cursorY * cursorY);
                if (len > tuning.MouseMaxRadius)
                {
                    cursorX *= tuning.MouseMaxRadius / len;
                    cursorY *= tuning.MouseMaxRadius / len;
                }
                x = cursorX;
                y = cursorY;
            }
            Aim(x, y, input.Time, stick);
            return RadialResult.None;
        }

        public void OpenAt(int element, double time)
        {
            if (element < 0 || element >= Layout.Count) return;
            Open(time);
            moved = true;
            Element = element;
            State = RadialState.Spells;
            Spell = IndexIn(element, RememberedSpell(element));
        }

        public string StepInElement(int direction)
        {
            if (Layout.Count == 0) return null;
            int g = Layout.GroupOfSpell(Current);
            if (g < 0) return Choose(Layout.Groups[0].SpellIds[0]);
            var ids = Layout.Groups[g].SpellIds;
            return Choose(ids[Wrap(IndexIn(g, Current) + Math.Sign(direction), ids.Count)]);
        }

        public string StepElement(int direction)
        {
            if (Layout.Count == 0) return null;
            int g = Layout.GroupOfSpell(Current);
            int next = g < 0 ? 0 : Wrap(g + Math.Sign(direction), Layout.Count);
            return Choose(RememberedSpell(next));
        }

        void Open(double time)
        {
            State = RadialState.Elements;
            Element = -1;
            Spell = -1;
            cursorX = cursorY = 0;
            openedAt = dwellFrom = time;
            moved = false;
        }

        void Aim(double x, double y, double time, bool stick)
        {
            double r = Math.Sqrt(x * x + y * y);
            if (r < (stick ? tuning.StickDeadZone : tuning.MouseDeadZone))
            {
                if (!stick) Highlight(RadialState.Elements, -1, -1, time);
                return;
            }
            moved = true;
            double a = RadialGeometry.AngleOf(x, y);
            int e = RadialGeometry.WedgeAt(a, Layout.Count);
            bool outer = r >= (stick ? tuning.StickPushOut : tuning.MousePushOut);

            if (State == RadialState.Spells && AimInArc(a, e, stick, outer)) return;
            if (e != Element) Highlight(RadialState.Elements, e, -1, time);
            if (State == RadialState.Elements && (outer || time - dwellFrom >= tuning.DwellSeconds))
                Highlight(RadialState.Spells, e, stick || outer ? SlotAt(a, e) : -1, time);
        }

        bool AimInArc(double angle, int element, bool stick, bool outer)
        {
            int slot = SlotAt(angle, Element);
            if (slot >= 0 && (stick || outer))
            {
                Spell = slot;
                return true;
            }
            if (stick || outer || element != Element) return false;
            Spell = -1;
            return true;
        }

        void Highlight(RadialState state, int element, int spell, double time)
        {
            if (element != Element) dwellFrom = time;
            State = state;
            Element = element;
            Spell = spell;
        }

        RadialResult Release(double time)
        {
            if (!moved && time - openedAt <= tuning.TapSeconds)
            {
                var back = Previous;
                if (back != null) Chose(back);
                return Close(RadialOutcome.SwapPrevious, back);
            }
            var id = HighlightedSpell ?? (Element >= 0 ? RememberedSpell(Element) : null);
            if (id == null) return Close(RadialOutcome.Cancelled, null);
            Chose(id);
            return Close(RadialOutcome.Selected, id);
        }

        RadialResult Close(RadialOutcome outcome, string spellId)
        {
            State = RadialState.Closed;
            Element = -1;
            Spell = -1;
            cursorX = cursorY = 0;
            return new RadialResult(outcome, spellId);
        }

        string Choose(string id)
        {
            Chose(id);
            return id;
        }

        int SlotAt(double angle, int element) =>
            RadialGeometry.SlotAt(angle, element, Layout.Count, Layout.Groups[element].SpellIds.Count, tuning);

        int IndexIn(int element, string spellId)
        {
            var ids = Layout.Groups[element].SpellIds;
            for (int i = 0; i < ids.Count; i++)
                if (ids[i] == spellId) return i;
            return -1;
        }

        static int Wrap(int i, int n) => ((i % n) + n) % n;
    }
}
