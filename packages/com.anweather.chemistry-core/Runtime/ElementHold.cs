// The button that started a cast by elements (docs/SPELL_COMBOS.md), kept out of
// Unity so it can be tested: letting go of it sends a ComboCommand.Release. The
// charge itself is the core's (World.HeldCharge), counted in ticks of the meter.
// The radial layout's Cast, which has no meter, charges by the clock (ChargeOf).
using System;

namespace Chem
{
    public sealed class ElementHold
    {
        public int Button { get; private set; } = -1;
        public bool Holding => Button >= 0;

        public static int ChargeOf(double seconds, int max, double secondsPerLevel) => Math.Max(1, Math.Min(max, 1 + (int)(seconds / secondsPerLevel)));

        public void Press(int button) => Button = button;

        public void Drop() => Button = -1;
    }
}
