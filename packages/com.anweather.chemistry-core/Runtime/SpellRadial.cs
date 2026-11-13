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
    /// <summary>
    /// The radial's distances and times. Pointer distances are in ring radii (1 is the outer
    /// edge of ring 1; the view divides pixels by its radius); stick distances are deflection, 0 to 1.
    /// </summary>
    public sealed class RadialTuning
    {
        /// <summary>Below this the pointer is in the centre: nothing is highlighted and a release cancels.</summary>
        public double MouseDeadZone = 0.35;
        /// <summary>From here out the pointer is in ring 2: it opens the arc and picks in it.</summary>
        public double MousePushOut = 1.0;
        /// <summary>The virtual cursor stops this far out, so it never wanders off.</summary>
        public double MouseMaxRadius = 1.6;
        /// <summary>The stick's inner threshold: past it, it highlights; inside it, it has sprung back.</summary>
        public double StickDeadZone = 0.35;
        /// <summary>Near full deflection: opens the arc at once.</summary>
        public double StickPushOut = 0.9;
        /// <summary>Resting on one element this long opens its arc.</summary>
        public double DwellSeconds = 0.35;
        /// <summary>Released within this long with no movement is a tap: swap to the previous spell.</summary>
        public double TapSeconds = 0.2;
        /// <summary>Each spell's share of an arc, in degrees (an arc is never narrower than its wedge).</summary>
        public double SlotDegrees = 30;
        public double MaxArcDegrees = 300;
    }

    public enum RadialInputKind { Mouse, Stick }

    /// <summary>One frame of input while the hold button might be down.</summary>
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
        /// <summary>Pointer: this frame's movement. Stick: where it points. Both with y up.</summary>
        public double X { get; }
        public double Y { get; }
        /// <summary>The hold button (Tab, LB) is down.</summary>
        public bool Held { get; }
        /// <summary>Seconds, from any clock that only goes forward.</summary>
        public double Time { get; }
        /// <summary>The cancel button (B) was pressed this frame.</summary>
        public bool Cancel { get; }

        public static RadialInput Mouse(double dx, double dy, bool held, double time, bool cancel = false) =>
            new RadialInput(RadialInputKind.Mouse, dx, dy, held, time, cancel);

        public static RadialInput Stick(double x, double y, bool held, double time, bool cancel = false) =>
            new RadialInput(RadialInputKind.Stick, x, y, held, time, cancel);
    }

    public enum RadialState
    {
        Closed,
        /// <summary>Ring 1: picking an element.</summary>
        Elements,
        /// <summary>Ring 2: the highlighted element's spells are out as an arc.</summary>
        Spells,
    }

    public enum RadialOutcome
    {
        /// <summary>Nothing to do this frame.</summary>
        None,
        /// <summary>A spell was chosen: select <see cref="RadialResult.SpellId"/>.</summary>
        Selected,
        /// <summary>Closed without a choice.</summary>
        Cancelled,
        /// <summary>A tap: <see cref="RadialResult.SpellId"/> is the previous spell, now current (null if there was none).</summary>
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

    /// <summary>The radial for one player: what's highlighted, and the current and previous spells.</summary>
    public sealed class SpellRadial
    {
        readonly RadialTuning tuning;
        /// <summary>The last spell chosen in each group, by group id: what an element alone selects.</summary>
        readonly Dictionary<string, string> lastInGroup = new Dictionary<string, string>(StringComparer.Ordinal);
        double cursorX, cursorY, openedAt, dwellFrom;
        bool moved;

        public SpellRadial(RadialLayout layout, RadialTuning tuning = null)
        {
            Layout = layout ?? throw new ArgumentNullException(nameof(layout));
            this.tuning = tuning ?? new RadialTuning();
        }

        /// <summary>Swap it when the rules change; the current and remembered spells are kept by id.</summary>
        public RadialLayout Layout { get; set; }
        public RadialTuning Tuning => tuning;
        public RadialState State { get; private set; } = RadialState.Closed;
        public bool IsOpen => State != RadialState.Closed;
        /// <summary>The highlighted wedge, or -1.</summary>
        public int Element { get; private set; } = -1;
        /// <summary>The highlighted spell in <see cref="Element"/>'s arc, or -1.</summary>
        public int Spell { get; private set; } = -1;
        /// <summary>The virtual cursor (pointer input), in ring radii with y up.</summary>
        public double CursorX => cursorX;
        public double CursorY => cursorY;
        public string Current { get; private set; }
        public string Previous { get; private set; }

        /// <summary>The highlighted spell's id, or null.</summary>
        public string HighlightedSpell => Element >= 0 && Spell >= 0 ? Layout.Groups[Element].SpellIds[Spell] : null;

        /// <summary>
        /// Records a spell as chosen, however it was (radial, click, keys, the network), so the
        /// tap and the element memory follow it.
        /// </summary>
        public void Chose(string spellId)
        {
            if (spellId == null) return;
            int g = Layout.GroupOfSpell(spellId);
            if (g >= 0) lastInGroup[Layout.Groups[g].Id] = spellId;
            if (spellId == Current) return;
            Previous = Current;
            Current = spellId;
        }

        /// <summary>The spell an element selects on its own: the last one chosen there, else its first.</summary>
        public string RememberedSpell(int element)
        {
            var g = Layout.Groups[element];
            return lastInGroup.TryGetValue(g.Id, out var id) && g.SpellIds.Contains(id) ? id : g.SpellIds[0];
        }

        /// <summary>Feeds one frame. Call it every frame the radial is open, and whenever the hold button goes down.</summary>
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

        /// <summary>
        /// Opens straight onto an element's arc with its remembered spell highlighted (number keys).
        /// Feed it while the key is held; releasing selects, as with the hold button.
        /// </summary>
        public void OpenAt(int element, double time)
        {
            if (element < 0 || element >= Layout.Count) return;
            Open(time);
            moved = true;
            Element = element;
            State = RadialState.Spells;
            Spell = IndexIn(element, RememberedSpell(element));
        }

        /// <summary>The next (+1) or previous (-1) spell in the current spell's element, wrapping; chosen. Null if there are no spells.</summary>
        public string StepInElement(int direction)
        {
            if (Layout.Count == 0) return null;
            int g = Layout.GroupOfSpell(Current);
            if (g < 0) return Choose(Layout.Groups[0].SpellIds[0]);
            var ids = Layout.Groups[g].SpellIds;
            return Choose(ids[Wrap(IndexIn(g, Current) + Math.Sign(direction), ids.Count)]);
        }

        /// <summary>The next (+1) or previous (-1) element's remembered spell, wrapping; chosen. Null if there are no spells.</summary>
        public string StepElement(int direction)
        {
            if (Layout.Count == 0) return null;
            int g = Layout.GroupOfSpell(Current);
            int next = g < 0 ? 0 : Wrap(g + Math.Sign(direction), Layout.Count);
            return Choose(RememberedSpell(next));
        }

        // ---------- the machine ----------

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
                // A stick springs back: keep what it last pointed at. A pointer in the centre points at nothing.
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

        /// <summary>
        /// With the arc out: true if aiming at <paramref name="angle"/> stays in it. A stick picks
        /// anywhere in the arc; a mouse only out in ring 2, and in ring 1 over the same element it
        /// keeps the arc out with no spell picked. Otherwise ring 1 decides.
        /// </summary>
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
