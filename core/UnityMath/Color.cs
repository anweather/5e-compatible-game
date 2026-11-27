// Stand-ins for Unity's Color and Color32 (docs/design/architecture-review.md step 6): only what the view's plans use
// (Scripts/Plans), with UnityEngine's fields, sums and byte rounding. dotnet only.
namespace UnityEngine
{
    public struct Color : System.IEquatable<Color>
    {
        public float r, g, b, a;

        public Color(float r, float g, float b, float a)
        {
            this.r = r;
            this.g = g;
            this.b = b;
            this.a = a;
        }

        public Color(float r, float g, float b)
            : this(r, g, b, 1f)
        {
        }

        public static Color black => new(0f, 0f, 0f, 1f);
        public static Color blue => new(0f, 0f, 1f, 1f);

        public static Color Lerp(Color a, Color b, float t)
        {
            t = Mathf.Clamp01(t);
            return new Color(a.r + (b.r - a.r) * t, a.g + (b.g - a.g) * t, a.b + (b.b - a.b) * t, a.a + (b.a - a.a) * t);
        }

        public static Color operator +(Color a, Color b) => new(a.r + b.r, a.g + b.g, a.b + b.b, a.a + b.a);
        public static Color operator *(Color a, float b) => new(a.r * b, a.g * b, a.b * b, a.a * b);
        public static Color operator *(float b, Color a) => new(a.r * b, a.g * b, a.b * b, a.a * b);

        public bool Equals(Color other) => r.Equals(other.r) && g.Equals(other.g) && b.Equals(other.b) && a.Equals(other.a);
        public override bool Equals(object other) => other is Color c && Equals(c);
        public override int GetHashCode() => r.GetHashCode() ^ (g.GetHashCode() << 2) ^ (b.GetHashCode() >> 2) ^ (a.GetHashCode() >> 1);
        public override string ToString() => string.Format(System.Globalization.CultureInfo.InvariantCulture, "RGBA({0:F3}, {1:F3}, {2:F3}, {3:F3})", r, g, b, a);
    }

    public struct Color32 : System.IEquatable<Color32>
    {
        public byte r, g, b, a;

        public Color32(byte r, byte g, byte b, byte a)
        {
            this.r = r;
            this.g = g;
            this.b = b;
            this.a = a;
        }

        public static implicit operator Color32(Color c) => new(Byte(c.r), Byte(c.g), Byte(c.b), Byte(c.a));
        public static implicit operator Color(Color32 c) => new(c.r / 255f, c.g / 255f, c.b / 255f, c.a / 255f);

        static byte Byte(float f) => (byte)Mathf.Round(Mathf.Clamp01(f) * 255f);

        public bool Equals(Color32 other) => r == other.r && g == other.g && b == other.b && a == other.a;
        public override bool Equals(object other) => other is Color32 c && Equals(c);
        public override int GetHashCode() => (r << 24) | (g << 16) | (b << 8) | a;
        public override string ToString() => $"RGBA({r}, {g}, {b}, {a})";
    }
}
