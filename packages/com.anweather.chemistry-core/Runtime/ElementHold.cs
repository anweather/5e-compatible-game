// The button that started a cast by elements (docs/SPELL_COMBOS.md), kept out of
// Unity so it can be tested: letting go of it sends a ComboCommand.Release. The
// charge itself is the core's (World.HeldCharge), counted in ticks of the meter.
// The radial layout's Cast, which has no meter, charges by the clock (ChargeOf).
using System;

namespace Chem
{
    public sealed class ElementHold
    {
        /// <summary>The button being held, or -1.</summary>
        public int Button { get; private set; } = -1;
        public bool Holding => Button >= 0;

        /// <summary>The charge after holding Cast this long: 1 for a tap, a level more every <paramref name="secondsPerLevel"/> (World.SecondsPerLevel), up to <paramref name="max"/>.</summary>
        public static int ChargeOf(double seconds, int max, double secondsPerLevel) => Math.Max(1, Math.Min(max, 1 + (int)(seconds / secondsPerLevel)));

        /// <summary>This button just loaded an element; it's the last one.</summary>
        public void Press(int button) => Button = button;

        /// <summary>Forgets the button: it was let go, or the cast was cancelled.</summary>
        public void Drop() => Button = -1;
    }
}
