// Colour theory for looks (docs/design/world-tools.md, "Colour theory: mixing"):
// colours mix the way paint does, because that's what a player expects. Blue and
// yellow make green, red and blue purple, red and yellow orange; averaging RGB
// would give grey, and adding light white. The model is the painter's wheel, RYB:
// a colour's whiteness is set aside, its hue goes from RGB to red, yellow and blue
// (Sugita and Takahashi's conversion, which goes back exactly), the weighted
// average is taken there and converted back, and its brightness is the weighted
// average of the colours' own, so mixing two bright paints doesn't muddy them.
// A colour mixed with itself comes back unchanged.
//
// Pure and allocation-free: ColourMixer is a struct to Add colours to, and the
// order they're added in doesn't change the result (beyond a float's last bit).
// The view's only, like every look: nothing here reaches a tick or the cell hash.
//
// Not a port: the sandbox has no looks.
using System;

namespace Chem
{
    /// <summary>Paint-like colour mixing: <see cref="Mix(LookColour, LookColour, float)"/>, and <see cref="ColourMixer"/> for more.</summary>
    public static class Colors
    {
        /// <summary>
        /// <paramref name="a"/> and <paramref name="b"/> mixed like paint, <paramref name="t"/> of the way
        /// to <paramref name="b"/> (0 is all a, 1 all b, 0.5 even).
        /// </summary>
        public static LookColour Mix(LookColour a, LookColour b, float t = 0.5f)
        {
            var m = new ColourMixer();
            m.Add(a, 1 - t);
            m.Add(b, t);
            return m.Result(a);
        }

        // ---------- RGB and RYB, both 0 to 1 a channel ----------

        internal static void ToRyb(float r, float g, float b, out float red, out float yellow, out float blue)
        {
            float white = Math.Min(r, Math.Min(g, b));
            r -= white;
            g -= white;
            b -= white;
            float topG = Math.Max(r, Math.Max(g, b));
            float y = Math.Min(r, g);
            r -= y;
            g -= y;
            if (b > 0 && g > 0)
            {
                b /= 2;
                g /= 2;
            }
            y += g;
            b += g;
            float topY = Math.Max(r, Math.Max(y, b));
            if (topY > 0)
            {
                float n = topG / topY;
                r *= n;
                y *= n;
                b *= n;
            }
            red = r + white;
            yellow = y + white;
            blue = b + white;
        }

        internal static LookColour FromRyb(float r, float y, float b)
        {
            float white = Math.Min(r, Math.Min(y, b));
            r -= white;
            y -= white;
            b -= white;
            float topY = Math.Max(r, Math.Max(y, b));
            float g = Math.Min(y, b);
            y -= g;
            b -= g;
            if (b > 0 && g > 0)
            {
                b *= 2;
                g *= 2;
            }
            r += y;
            g += y;
            float topG = Math.Max(r, Math.Max(g, b));
            if (topG > 0)
            {
                float n = topY / topG;
                r *= n;
                g *= n;
                b *= n;
            }
            return new LookColour(r + white, g + white, b + white);
        }

        internal static float Brightness(LookColour c) => Math.Max(c.R, Math.Max(c.G, c.B));
    }

    /// <summary>
    /// Colours added with weights, mixed like paint (<see cref="Colors"/>). A struct, so mixing allocates
    /// nothing; a weight of 0 or less is left out.
    /// </summary>
    public struct ColourMixer
    {
        float red, yellow, blue, bright, weight;

        /// <summary>The total weight added so far.</summary>
        public float Weight => weight;

        public void Add(LookColour c, float w = 1)
        {
            if (!(w > 0)) return;
            Colors.ToRyb(c.R, c.G, c.B, out float r, out float y, out float b);
            red += r * w;
            yellow += y * w;
            blue += b * w;
            bright += Colors.Brightness(c) * w;
            weight += w;
        }

        /// <summary>The mix, or <paramref name="none"/> when nothing with a weight was added.</summary>
        public LookColour Result(LookColour none)
        {
            if (!(weight > 0)) return none;
            var c = Colors.FromRyb(red / weight, yellow / weight, blue / weight);
            float top = Colors.Brightness(c), want = bright / weight;
            if (top <= 0) return c;
            float n = want / top;
            return new LookColour(Math.Min(1, c.R * n), Math.Min(1, c.G * n), Math.Min(1, c.B * n));
        }
    }
}
