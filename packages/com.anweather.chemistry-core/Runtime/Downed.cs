// A wizard at 0 health (#14): it's down for sim.json's "downed" seconds, then
// it's back at the yard with full health. The Unity side is Vitals and
// WizardDown. Like breath, health is the wizard's own, not shared state, so this
// runs on real seconds rather than the world's ticks.
using System;
using Newtonsoft.Json.Linq;

namespace Chem
{
    public sealed class DownedParams
    {
        public double Seconds = 3;

        public static DownedParams Parse(JToken t)
        {
            var d = new DownedParams();
            if (t is JObject o) d.Seconds = Math.Max(0.1, (double?)o["seconds"] ?? d.Seconds);
            return d;
        }
    }

    public sealed class Downed
    {
        readonly double seconds;
        double left;

        public Downed(DownedParams p) => seconds = (p ?? new DownedParams()).Seconds;

        public bool IsDown { get; private set; }

        public bool Fall()
        {
            if (IsDown) return false;
            IsDown = true;
            left = seconds;
            return true;
        }

        public bool Step(double dt)
        {
            if (!IsDown) return false;
            left -= dt;
            return left <= 0;
        }

        public bool Clear()
        {
            bool was = IsDown;
            IsDown = false;
            return was;
        }
    }
}
