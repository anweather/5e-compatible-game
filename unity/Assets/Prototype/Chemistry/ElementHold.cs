// Holding the last element to charge a cast (docs/SPELL_COMBOS.md): the view's
// clock for the button that started the cast, kept out of Unity so it can be
// tested. What it works out goes into the core in a ComboCommand.Release; the
// core never reads a clock.
using System;

namespace Chem
{
    public sealed class ElementHold
    {
        /// <summary>How long a button is held for each level of charge.</summary>
        public const float SecondsPerLevel = 0.45f;

        double since;

        /// <summary>The button being held, or -1.</summary>
        public int Button { get; private set; } = -1;
        public bool Holding => Button >= 0;

        /// <summary>The charge after holding this long: 1 for a tap, a level more every <see cref="SecondsPerLevel"/>, up to <paramref name="max"/>.</summary>
        public static int ChargeOf(double seconds, int max) => Math.Max(1, Math.Min(max, 1 + (int)(seconds / SecondsPerLevel)));

        /// <summary>This button just loaded an element; it's the last one, so the clock starts again.</summary>
        public void Press(int button, double now)
        {
            Button = button;
            since = now;
        }

        /// <summary>The charge built so far; 1 when nothing is held.</summary>
        public int ChargeAt(double now, int max) => Holding ? ChargeOf(now - since, max) : 1;

        /// <summary>Forgets the button: it was let go, or the cast was cancelled.</summary>
        public void Drop() => Button = -1;
    }
}
