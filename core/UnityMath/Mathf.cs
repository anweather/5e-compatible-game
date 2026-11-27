// A stand-in for Unity's Mathf (docs/design/architecture-review.md step 6): only what the view's plans use (Scripts/Plans),
// with UnityEngine.Mathf's formulas in single precision, so the plans give the same numbers without the engine. dotnet only.
namespace UnityEngine
{
    public static class Mathf
    {
        public const float PI = (float)System.Math.PI;
        public static readonly float Epsilon = float.Epsilon;

        public static float Sin(float f) => (float)System.Math.Sin(f);
        public static float Cos(float f) => (float)System.Math.Cos(f);
        public static float Sqrt(float f) => (float)System.Math.Sqrt(f);
        public static float Pow(float f, float p) => (float)System.Math.Pow(f, p);
        public static float Exp(float power) => (float)System.Math.Exp(power);
        public static float Log10(float f) => (float)System.Math.Log10(f);
        public static float Round(float f) => (float)System.Math.Round(f);
        public static float Sign(float f) => f >= 0f ? 1f : -1f;

        public static float Abs(float f) => System.Math.Abs(f);
        public static int Abs(int value) => System.Math.Abs(value);
        public static float Min(float a, float b) => a < b ? a : b;
        public static int Min(int a, int b) => a < b ? a : b;
        public static float Max(float a, float b) => a > b ? a : b;
        public static int Max(int a, int b) => a > b ? a : b;

        public static float Clamp(float value, float min, float max)
        {
            if (value < min) return min;
            if (value > max) return max;
            return value;
        }

        public static int Clamp(int value, int min, int max)
        {
            if (value < min) return min;
            if (value > max) return max;
            return value;
        }

        public static float Clamp01(float value) => Clamp(value, 0f, 1f);

        public static float Lerp(float a, float b, float t) => a + (b - a) * Clamp01(t);

        public static float InverseLerp(float a, float b, float value) => a != b ? Clamp01((value - a) / (b - a)) : 0f;

        public static float SmoothStep(float from, float to, float t)
        {
            t = Clamp01(t);
            t = -2f * t * t * t + 3f * t * t;
            return to * t + from * (1f - t);
        }

        public static bool Approximately(float a, float b) =>
            Abs(b - a) < Max(0.000001f * Max(Abs(a), Abs(b)), Epsilon * 8f);
    }
}
