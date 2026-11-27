// Whether a body has gone through the ground (docs/design/maps-as-data.md, section 5): a probe's wizard fell 4.4 km with
// every check green, so every probe now watches for it.
using System;

namespace Chem
{
    public sealed class FallWatch
    {
        public const float MostUnder = 2f, MostSeconds = 1f;

        float under;

        public float Deepest { get; private set; }

        public bool Fell { get; private set; }

        public void Feed(float metresUnder, float seconds)
        {
            Deepest = Math.Max(Deepest, metresUnder);
            under = metresUnder > MostUnder ? under + seconds : 0f;
            if (under >= MostSeconds) Fell = true;
        }
    }
}
