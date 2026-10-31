// A swimmer's breath (docs/BACKLOG.md 13): it runs down only while the head is
// under water and fills again quickly once it's out. At none left the swimmer
// drowns, taking drownDice d drownSides damage at once and again every
// drownEvery seconds while the head stays under; the drowning stops the moment
// the head comes out. The numbers are sim.json's "breath". A wizard's breath is
// its own (Vitals), not shared state, so it runs on real seconds and its own
// dice rather than the world's ticks and seeded RNG.
using System;
using Newtonsoft.Json.Linq;

namespace Chem
{
    /// <summary>sim.json's "breath": how long a held breath lasts, how fast it comes back, and drowning's damage.</summary>
    public sealed class BreathParams
    {
        /// <summary>Seconds of breath, full.</summary>
        public double Seconds = 30;
        /// <summary>Seconds to fill again from none, head out.</summary>
        public double RefillSeconds = 3;
        /// <summary>Seconds between drowning's hits.</summary>
        public double DrownEvery = 3;
        /// <summary>Each hit's damage: this many dice of this many sides.</summary>
        public int DrownDice = 1, DrownSides = 6;

        /// <summary>Reads sim.json's "breath" (absent: the defaults).</summary>
        public static BreathParams Parse(JToken t)
        {
            var b = new BreathParams();
            if (!(t is JObject o)) return b;
            b.Seconds = Math.Max(0.1, (double?)o["seconds"] ?? b.Seconds);
            b.RefillSeconds = Math.Max(0.01, (double?)o["refillSeconds"] ?? b.RefillSeconds);
            b.DrownEvery = Math.Max(0.1, (double?)o["drownEvery"] ?? b.DrownEvery);
            b.DrownDice = (int?)o["drownDice"] ?? b.DrownDice;
            b.DrownSides = Math.Max(1, (int?)o["drownSides"] ?? b.DrownSides);
            return b;
        }
    }

    /// <summary>One swimmer's breath: Step it each frame with whether its head is under water.</summary>
    public sealed class Breath
    {
        readonly BreathParams p;
        // Rolls one die of the given sides (1 to sides): the view's own dice, not the world's.
        readonly Func<int, int> roll;
        // Seconds since drowning's last hit.
        double sinceHit;

        public Breath(BreathParams p, Func<int, int> roll)
        {
            this.p = p ?? new BreathParams();
            this.roll = roll ?? throw new ArgumentNullException(nameof(roll));
            Left = this.p.Seconds;
        }

        /// <summary>Seconds of breath left.</summary>
        public double Left { get; private set; }

        /// <summary>Seconds of breath, full.</summary>
        public double Max => p.Seconds;

        /// <summary>What's left, 0 to 1.</summary>
        public double Share => Left / p.Seconds;

        /// <summary>True while it's full.</summary>
        public bool Full => Left >= p.Seconds;

        /// <summary>True while the head was under water at the last step.</summary>
        public bool Under { get; private set; }

        /// <summary>True while the head is under with no breath left.</summary>
        public bool Drowning => Under && Left <= 0;

        /// <summary>
        /// Runs <paramref name="seconds"/> on, the head under water or not; returns the drowning
        /// damage taken in that time (0 mostly). The first hit comes as the breath runs out.
        /// </summary>
        public int Step(double seconds, bool headUnder)
        {
            if (seconds < 0) seconds = 0;
            Under = headUnder;
            if (!headUnder)
            {
                Left = Math.Min(p.Seconds, Left + seconds * p.Seconds / p.RefillSeconds);
                return 0;
            }
            if (Left > seconds)
            {
                Left -= seconds;
                return 0;
            }
            // Out of breath this step: the time past the last of it counts toward the hits.
            double past = seconds - Left;
            int hits = 0;
            if (Left > 0)
            {
                Left = 0;
                sinceHit = 0;
                hits = 1;
            }
            sinceHit += past;
            while (sinceHit >= p.DrownEvery)
            {
                sinceHit -= p.DrownEvery;
                hits++;
            }
            int damage = 0;
            for (int h = 0; h < hits; h++)
                for (int d = 0; d < p.DrownDice; d++) damage += roll(p.DrownSides);
            return damage;
        }

        /// <summary>Full again (an online reset, a respawn).</summary>
        public void Refill()
        {
            Left = p.Seconds;
            sinceHit = 0;
            Under = false;
        }
    }
}
