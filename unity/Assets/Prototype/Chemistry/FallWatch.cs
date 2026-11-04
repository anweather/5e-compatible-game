// Whether a body has gone through the ground (docs/design/maps-as-data.md, section 5): a probe's wizard fell 4.4 km with
// every check green, so every probe now watches for it.
using System;

namespace Chem
{
    /// <summary>Counts how long a body stays well under the ground: a moment under, as ground eases up past it, is no fall.</summary>
    public sealed class FallWatch
    {
        public const float MostUnder = 2f, MostSeconds = 1f;

        float under;

        /// <summary>The furthest under the ground it has been, in metres.</summary>
        public float Deepest { get; private set; }

        /// <summary>Whether it has been more than <see cref="MostUnder"/> metres under for <see cref="MostSeconds"/>.</summary>
        public bool Fell { get; private set; }

        /// <summary>One frame: how far under the ground the body is (negative above it), and the frame's seconds.</summary>
        public void Feed(float metresUnder, float seconds)
        {
            Deepest = Math.Max(Deepest, metresUnder);
            under = metresUnder > MostUnder ? under + seconds : 0f;
            if (under >= MostSeconds) Fell = true;
        }
    }
}
