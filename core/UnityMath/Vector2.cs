// A stand-in for Unity's Vector2 (docs/design/architecture-review.md step 6): only the fields, operators and formulas the
// view's plans use (Scripts/Plans), the same as UnityEngine.Vector2's, in single precision. dotnet only: Unity compiles
// the plans against UnityEngine itself.
namespace UnityEngine
{
    public struct Vector2 : System.IEquatable<Vector2>
    {
        public float x, y;

        public Vector2(float x, float y)
        {
            this.x = x;
            this.y = y;
        }

        public float magnitude => (float)System.Math.Sqrt(x * x + y * y);
        public float sqrMagnitude => x * x + y * y;

        public Vector2 normalized
        {
            get
            {
                float mag = magnitude;
                return mag > 1e-5f ? this / mag : new Vector2(0f, 0f);
            }
        }

        public static Vector2 Lerp(Vector2 a, Vector2 b, float t)
        {
            t = Mathf.Clamp01(t);
            return new Vector2(a.x + (b.x - a.x) * t, a.y + (b.y - a.y) * t);
        }

        public static Vector2 operator +(Vector2 a, Vector2 b) => new(a.x + b.x, a.y + b.y);
        public static Vector2 operator -(Vector2 a, Vector2 b) => new(a.x - b.x, a.y - b.y);
        public static Vector2 operator -(Vector2 a) => new(-a.x, -a.y);
        public static Vector2 operator *(Vector2 a, float d) => new(a.x * d, a.y * d);
        public static Vector2 operator *(float d, Vector2 a) => new(a.x * d, a.y * d);
        public static Vector2 operator /(Vector2 a, float d) => new(a.x / d, a.y / d);

        public static bool operator ==(Vector2 lhs, Vector2 rhs)
        {
            float dx = lhs.x - rhs.x, dy = lhs.y - rhs.y;
            return dx * dx + dy * dy < 1e-5f * 1e-5f;
        }

        public static bool operator !=(Vector2 lhs, Vector2 rhs) => !(lhs == rhs);

        public bool Equals(Vector2 other) => x.Equals(other.x) && y.Equals(other.y);
        public override bool Equals(object other) => other is Vector2 v && Equals(v);
        public override int GetHashCode() => x.GetHashCode() ^ (y.GetHashCode() << 2);
        public override string ToString() => string.Format(System.Globalization.CultureInfo.InvariantCulture, "({0:F2}, {1:F2})", x, y);
    }
}
