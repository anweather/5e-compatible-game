// What the night does in the rules: the light on each cell, from the day clock
// (Sky.Daylight) and the fires burning near it, in the SRD's three levels.
// Worked out from the clock and the cells when asked, never kept, so it costs
// nothing a tick, adds nothing to the hash and is the same on every peer.
// (Night's hold on water, snow freezing faster and ice keeping, is in
// World.Weather.cs.) Port of sandbox/src/night.ts.
using System;

namespace Chem
{
    public sealed partial class World
    {
        LightLevel[] oneLight;

        /// <summary>The light the sky gives now (Sky.Daylight at the clock).</summary>
        public LightLevel Daylight => Sky.Daylight(Rules.Sim.Day, Minutes);

        /// <summary>Whether it's night: dark between dusk and dawn.</summary>
        public bool IsNight => Daylight == LightLevel.Dark;

        /// <summary>The light on one cell: the daylight, or a fire's near it if brighter.</summary>
        public LightLevel LightAt(int x, int y)
        {
            oneLight ??= new LightLevel[1];
            FillLight(x, y, 1, 1, oneLight);
            return oneLight[0];
        }

        /// <summary>
        /// The light on each cell of a w by h block from (x0, y0), row by row into
        /// <paramref name="into"/> (at least w·h long): the daylight, raised by every burning cell
        /// within reach, bright out to day.fireLight.bright feet and dim beyond. Distance is the
        /// SRD's grid distance (a diagonal step is 5 ft).
        /// </summary>
        public void FillLight(int x0, int y0, int w, int h, LightLevel[] into)
        {
            var day = Daylight;
            for (int i = 0; i < w * h; i++) into[i] = day;
            if (day == LightLevel.Bright) return;
            var d = Rules.Sim.Day;
            int bright = FeetToCells(d.FireBright), reach = FeetToCells(d.FireBright + d.FireDim);
            for (int y = y0 - reach; y < y0 + h + reach; y++)
                for (int x = x0 - reach; x < x0 + w + reach; x++)
                {
                    var c = Locate(x, y, out _, out _);
                    if (c != null && c.Fire > 0) Shine(x, y);
                }

            // One fire's light on the block: bright within `bright` cells, dim out to `reach`.
            void Shine(int fx, int fy)
            {
                for (int y = Math.Max(y0, fy - reach); y <= Math.Min(y0 + h - 1, fy + reach); y++)
                    for (int x = Math.Max(x0, fx - reach); x <= Math.Min(x0 + w - 1, fx + reach); x++)
                    {
                        var level = Math.Max(Math.Abs(x - fx), Math.Abs(y - fy)) <= bright ? LightLevel.Bright : LightLevel.Dim;
                        int i = (y - y0) * w + (x - x0);
                        if (level > into[i]) into[i] = level;
                    }
            }
        }

        static int FeetToCells(double feet) => (int)Math.Floor(feet / 5);
    }
}
