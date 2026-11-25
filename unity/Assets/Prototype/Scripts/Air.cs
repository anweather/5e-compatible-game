using UnityEngine;

// The air between the eye and what it sees (docs/design/hills.md "One air", after the boat game's B166/B167): one value
// made once a frame by AirInPlay, its one writer, from the weather, the map's dials and the sky. How much of a shape is
// left at a distance (Seen) and the colour the rest goes to on a bearing (Toward) are the same sums as Air.hlsl's AirSeen
// and AirToward, which every shader of the land, its water and the far ring draws by; URP's own fog, for the things drawn
// with URP's shaders (the grass, the wizard, the props), is matched to it at LitMatchedAt. A map is either exponential
// (generated: clear air over kilometres times the weather's gaussian) or linear (the valley's fade, and under water).
public readonly struct Air
{
    /// <summary>Exponential-squared density × distance where the gaussian haze is ~98% (Punchy's constant).</summary>
    public const float HazeK = 1.98f;

    /// <summary>
    /// Where URP's own fog (the Lit things': the grass, the wizard, the props, the trees' meshes) is matched to the air (m),
    /// and again at LitMatchedTo times that: the near things are there. URP's fog is always linear: a player keeps only the
    /// fog the scenes it's built from use (the yard's linear), so an exponential-squared fog drew nothing there.
    /// </summary>
    public const float LitMatchedAt = 200f, LitMatchedTo = 4f;

    /// <summary>Where fog closes the haze in to (m): ~98% here at full fog.</summary>
    public const float FogMetres = 90f;

    /// <summary>The skybox's horizon glow round the sun (Punchy's Sky.hlsl): 0.25 of the sun's colour, a sixth of it left at the horizon line.</summary>
    public const float GlowPower = 12f, GlowShare = 0.25f * 0.15f;

    /// <summary>Clear air per metre (exponential), the gaussian per metre, and a linear map's fade (m; End 0 for none).</summary>
    public readonly float Linear, Gaussian, Start, End;

    /// <summary>Where everything gives way to the air wholly (m; FarTo 0 for nowhere): the far land's last.</summary>
    public readonly float FarFrom, FarTo;

    /// <summary>The clear weather's gaussian per metre (a far landmark's own see-through is made from it, FarLand.shader).</summary>
    public readonly float ClearGaussian;

    /// <summary>The sky's horizon (as the sky takes it, sRGB) and the sun's glow on it (linear), and the sun's way.</summary>
    public readonly Color Horizon;
    public readonly Vector3 Glow, Sun;

    Air(float linear, float gaussian, float start, float end, float farFrom, float farTo, float clearGaussian, Color horizon, Vector3 glow, Vector3 sun)
    {
        Linear = linear;
        Gaussian = gaussian;
        Start = start;
        End = end;
        FarFrom = farFrom;
        FarTo = farTo;
        ClearGaussian = clearGaussian;
        Horizon = horizon;
        Glow = glow;
        Sun = sun;
    }

    /// <summary>A generated map's air: clear air over <paramref name="reach"/> metres, a gaussian ~98% at <paramref name="gaussianMetres"/>.</summary>
    public static Air Exponential(float reach, float gaussianMetres, float clearGaussianMetres, float farFrom, float farTo, Color horizon, Vector3 glow, Vector3 sun) =>
        new(reach > 0f ? 1f / reach : 0f, HazeK / Mathf.Max(1f, gaussianMetres), 0f, 0f, farFrom, farTo, HazeK / Mathf.Max(1f, clearGaussianMetres), horizon, glow, sun);

    /// <summary>A linear fade from <paramref name="start"/> to <paramref name="end"/> metres (the valley, under water).</summary>
    public static Air Faded(float start, float end, Color horizon, Vector3 glow, Vector3 sun) =>
        new(0f, 0f, start, Mathf.Max(start + 1f, end), 0f, 0f, 0f, horizon, glow, sun);

    /// <summary>No air at all (fog off).</summary>
    public static Air None(Color horizon) => new(0f, 0f, 0f, 0f, 0f, 0f, 0f, horizon, Vector3.zero, Vector3.up);

    /// <summary>
    /// The gaussian's metres in this weather: <paramref name="clear"/> in clear air, <paramref name="rainy"/> in rain, and
    /// FogMetres in fog (<paramref name="rain"/> and <paramref name="fog"/> 0..1, as SkyLight eases them).
    /// </summary>
    public static float GaussianMetres(float clear, float rainy, float rain, float fog) => Mathf.Lerp(Mathf.Lerp(clear, rainy, rain), FogMetres, fog);

    /// <summary>Whether this is a linear fade.</summary>
    public bool IsLinear => End > 0f;

    /// <summary>How much of a shape is left at <paramref name="dist"/> metres, 0..1 (Air.hlsl's AirSeen).</summary>
    public float Seen(float dist)
    {
        if (IsLinear) return Mathf.Clamp01((End - dist) / Mathf.Max(End - Start, 1e-3f));
        float thick = dist * Gaussian;
        float seen = Mathf.Exp(-dist * Linear) * Mathf.Exp(-thick * thick);
        if (FarTo > 0f) seen *= 1f - SmoothStep(FarFrom, FarTo, dist);
        return seen;
    }

    /// <summary>The air's colour toward <paramref name="toward"/> (linear; Air.hlsl's AirToward): the horizon with the sun's glow on that bearing.</summary>
    public Vector3 Toward(Vector3 toward)
    {
        var flat = new Vector2(toward.x, toward.z);
        flat /= Mathf.Max(flat.magnitude, 1e-3f);
        float sun = Mathf.Clamp01(Vector2.Dot(flat, new Vector2(Sun.x, Sun.z)));
        var h = Horizon.linear;
        return new Vector3(h.r, h.g, h.b) + Glow * Mathf.Pow(sun, GlowPower);
    }

    /// <summary>The air's colour averaged round the horizon (linear): URP's fog colour, for the Lit things.</summary>
    public Vector3 Mean
    {
        get
        {
            const int Ways = 64;
            var sum = Vector3.zero;
            for (int i = 0; i < Ways; i++)
            {
                float a = i * 2f * Mathf.PI / Ways;
                sum += Toward(new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)));
            }
            return sum / Ways;
        }
    }

    /// <summary>
    /// The two distances URP's linear fog is matched to the air at (m): LitMatchedAt and LitMatchedTo times it, or nearer
    /// where the air hides more than 10% and 70% of a shape before them (fog, rain).
    /// </summary>
    public (float a, float b) LitMatches => (Mathf.Min(LitMatchedAt, Where(0.9f)), Mathf.Min(LitMatchedAt * LitMatchedTo, Where(0.3f)));

    /// <summary>URP's linear fog (start, end in m) that leaves what Seen does at both LitMatches.</summary>
    public (float start, float end) LinearFog
    {
        get
        {
            if (IsLinear) return (Start, End);
            var (a, b) = LitMatches;
            float sa = Seen(a), sb = Seen(b), slope = (sa - sb) / Mathf.Max(b - a, 1e-3f);
            if (slope < 1e-7f) return (1e6f, 1e6f + 1f);
            float end = a + sa / slope;
            return (end - 1f / slope, end);
        }
    }

    // Where the air leaves this much of a shape (m), within 20 km: Seen never rises with the distance.
    float Where(float left)
    {
        float lo = 0f, hi = 20000f;
        if (Seen(hi) >= left) return hi;
        for (int i = 0; i < 32; i++)
        {
            float mid = (lo + hi) * 0.5f;
            if (Seen(mid) > left) lo = mid;
            else hi = mid;
        }
        return lo;
    }

    /// <summary>_Air: clear air and gaussian per metre, a linear fade's start and end.</summary>
    public Vector4 ForShaders => new(Linear, Gaussian, Start, End);

    /// <summary>_AirFar: where all gives way, by when, and the clear gaussian per metre.</summary>
    public Vector4 FarForShaders => new(FarFrom, FarTo, ClearGaussian, 0f);

    static float SmoothStep(float a, float b, float x)
    {
        float t = Mathf.Clamp01((x - a) / (b - a));
        return t * t * (3f - 2f * t);
    }
}
