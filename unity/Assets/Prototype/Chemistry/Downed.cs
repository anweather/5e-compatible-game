// A wizard at 0 health (#14): it's down for sim.json's "downed" seconds, then
// it's back at the yard with full health. The Unity side is Vitals and
// WizardDown. Like breath, health is the wizard's own, not shared state, so this
// runs on real seconds rather than the world's ticks.
using System;
using Newtonsoft.Json.Linq;

namespace Chem
{
    /// <summary>sim.json's "downed": how long a wizard stays down.</summary>
    public sealed class DownedParams
    {
        public double Seconds = 3;

        /// <summary>Reads sim.json's "downed" (absent: the defaults).</summary>
        public static DownedParams Parse(JToken t)
        {
            var d = new DownedParams();
            if (t is JObject o) d.Seconds = Math.Max(0.1, (double?)o["seconds"] ?? d.Seconds);
            return d;
        }
    }

    /// <summary>One wizard's time down: Fall at 0 health, Step it each frame, Clear when it's up.</summary>
    public sealed class Downed
    {
        readonly double seconds;
        double left;

        public Downed(DownedParams p) => seconds = (p ?? new DownedParams()).Seconds;

        public bool IsDown { get; private set; }

        /// <summary>Goes down; false if it already was, so the clock doesn't start again.</summary>
        public bool Fall()
        {
            if (IsDown) return false;
            IsDown = true;
            left = seconds;
            return true;
        }

        /// <summary>A frame goes by; true once the time down is over (still down until <see cref="Clear"/>).</summary>
        public bool Step(double dt)
        {
            if (!IsDown) return false;
            left -= dt;
            return left <= 0;
        }

        /// <summary>Up again; true if it was down.</summary>
        public bool Clear()
        {
            bool was = IsDown;
            IsDown = false;
            return was;
        }
    }
}
