using Chem;
using UnityEngine;

// The sky itself: Punchy's skybox (Zachary Franklin, ThirdParty/Punchy) of
// gradient, sun, moon, stars, cirrus and volumetric clouds, fed from our clock
// and weather. Punchy renders its weather into world maps; our weather is the
// same everywhere (Chem.Sky), so the maps here are one texel each, laid over a
// wide square round the camera. Each weather kind has its own clouds (from
// Punchy's presets), eased in as it strengthens; lightning lights the cloud
// it came from. Also colours the haze like the horizon, so distant land fades
// into the sky. Presentation only, like SkyLight, which still turns the sun
// and the moon.
[RequireComponent(typeof(SkyLight))]
public class CloudSky : MonoBehaviour
{
    /// <summary>The 3D noise the clouds are carved from, baked by ChemYardBuilder (made here at start without it).</summary>
    public Texture3D cloudNoise;

    /// <summary>The weather maps cover this square round the camera, in metres; past it the sky uses the same values.</summary>
    const float MapSize = 40000f;
    const float MaxCloudBase = 4000f, MaxCloudThickness = 9000f;

    // What a kind of weather looks like, at full strength (Punchy's presets).
    readonly struct Look
    {
        public readonly float coverage, darkness, overcast, cloudBase, thickness, rain;

        public Look(float coverage, float darkness, float overcast, float cloudBase, float thickness, float rain)
        {
            this.coverage = coverage;
            this.darkness = darkness;
            this.overcast = overcast;
            this.cloudBase = cloudBase;
            this.thickness = thickness;
            this.rain = rain;
        }

        public static Look Lerp(Look a, Look b, float t) => new(
            Mathf.Lerp(a.coverage, b.coverage, t), Mathf.Lerp(a.darkness, b.darkness, t), Mathf.Lerp(a.overcast, b.overcast, t),
            Mathf.Lerp(a.cloudBase, b.cloudBase, t), Mathf.Lerp(a.thickness, b.thickness, t), Mathf.Lerp(a.rain, b.rain, t));
    }

    static readonly Look Clear = new(0.2f, 0f, 0f, 1300f, 600f, 0f);
    static readonly Look Windy = new(0.45f, 0.1f, 0.1f, 1100f, 900f, 0f);
    static readonly Look Overcast = new(0.9f, 0.35f, 0.8f, 800f, 700f, 0f);
    static readonly Look Fog = new(0.95f, 0.2f, 0.9f, 300f, 500f, 0f);
    static readonly Look Rain = new(0.95f, 0.6f, 0.95f, 650f, 2200f, 0.6f);
    static readonly Look Storm = new(1f, 0.9f, 1f, 600f, 7500f, 1f);
    static readonly Look Snow = new(0.95f, 0.3f, 0.9f, 700f, 1500f, 0.2f);

    // The clear sky through the day, by the sun's height (0 straight down, 0.5 on the horizon, 1 overhead).
    static readonly Gradient Zenith = Make(
        (0.00f, new Color(0.006f, 0.010f, 0.030f)), (0.44f, new Color(0.03f, 0.05f, 0.13f)),
        (0.50f, new Color(0.22f, 0.30f, 0.52f)), (0.60f, new Color(0.20f, 0.40f, 0.82f)), (1.00f, new Color(0.17f, 0.39f, 0.86f)));
    static readonly Gradient Horizon = Make(
        (0.00f, new Color(0.012f, 0.018f, 0.040f)), (0.45f, new Color(0.13f, 0.11f, 0.17f)),
        (0.50f, new Color(0.92f, 0.52f, 0.30f)), (0.56f, new Color(0.86f, 0.74f, 0.64f)), (0.66f, new Color(0.70f, 0.80f, 0.93f)),
        (1.00f, new Color(0.70f, 0.80f, 0.93f)));
    static readonly Gradient SunDisc = Make(
        (0.48f, new Color(1.00f, 0.35f, 0.12f)), (0.53f, new Color(1.00f, 0.62f, 0.38f)),
        (0.62f, new Color(1.00f, 0.92f, 0.84f)), (1.00f, new Color(1.00f, 0.97f, 0.93f)));
    static readonly Color GroundColor = new(0.36f, 0.37f, 0.39f);

    static readonly int WeatherMapId = Shader.PropertyToID("_PunchyWeatherMap");
    static readonly int WetnessMapId = Shader.PropertyToID("_PunchyWetnessMap");
    static readonly int CloudMapId = Shader.PropertyToID("_PunchyCloudMap");
    static readonly int CloudNoiseId = Shader.PropertyToID("_PunchyCloudNoise");
    static readonly int WeatherRectId = Shader.PropertyToID("_PunchyWeatherRect");
    static readonly int ClimateId = Shader.PropertyToID("_PunchyClimate");
    static readonly int SunDirectionId = Shader.PropertyToID("_PunchySunDirection");
    static readonly int MoonDirectionId = Shader.PropertyToID("_PunchyMoonDirection");
    static readonly int SunColorId = Shader.PropertyToID("_PunchySunColor");
    static readonly int ZenithId = Shader.PropertyToID("_PunchySkyZenith");
    static readonly int HorizonId = Shader.PropertyToID("_PunchySkyHorizon");
    static readonly int GroundId = Shader.PropertyToID("_PunchySkyGround");
    static readonly int CloudsId = Shader.PropertyToID("_PunchyClouds");
    static readonly int CloudOffsetId = Shader.PropertyToID("_PunchyCloudOffset");
    static readonly int NightId = Shader.PropertyToID("_PunchyNight");
    static readonly int LightningId = Shader.PropertyToID("_PunchyLightning");
    static readonly int LightningPosId = Shader.PropertyToID("_PunchyLightningPos");

    ChemGrid grid;
    SkyLight skyLight;
    Texture3D madeNoise;
    Texture2D weatherMap, wetnessMap, cloudMap;
    Look shown = Clear;
    Vector2 cloudOffset;
    Vector3 strikeAt;
    float strike;

    /// <summary>How much of the sky is cloud now, 0..1 (eased toward the weather), for the probe.</summary>
    public float Coverage => shown.coverage;

    /// <summary>Lights the cloud above a lightning strike for a moment (WeatherFx).</summary>
    public void Strike(Vector3 at, float brightness)
    {
        strikeAt = new Vector3(at.x, shown.cloudBase + shown.thickness * 0.3f, at.z);
        strike = Mathf.Max(strike, brightness);
    }

    void Start()
    {
        grid = FindFirstObjectByType<ChemGrid>();
        skyLight = GetComponent<SkyLight>();
        if (cloudNoise == null) cloudNoise = madeNoise = Punchy.Weather.CloudNoise.Create();
        weatherMap = Texel("Weather map");
        wetnessMap = Texel("Wetness map");
        cloudMap = Texel("Cloud map");
        Shader.SetGlobalTexture(CloudNoiseId, cloudNoise);
        Shader.SetGlobalTexture(WeatherMapId, weatherMap);
        Shader.SetGlobalTexture(WetnessMapId, wetnessMap);
        Shader.SetGlobalTexture(CloudMapId, cloudMap);
    }

    void OnDestroy()
    {
        if (madeNoise != null) Destroy(madeNoise);
        Destroy(weatherMap);
        Destroy(wetnessMap);
        Destroy(cloudMap);
    }

    static Texture2D Texel(string name) => new(1, 1, TextureFormat.RGBA32, false, true) { name = name, hideFlags = HideFlags.DontSave };

    // After SkyLight has turned the sun and moon and set the haze for this frame.
    void LateUpdate()
    {
        var world = grid != null ? grid.World : null;
        var cam = Camera.main;
        if (world == null || cam == null || skyLight.sun == null) return;
        var now = world.Weather;
        shown = Look.Lerp(shown, Wanted(now), 1f - Mathf.Exp(-Time.deltaTime * 0.3f));
        Drift(world.WindDir);
        strike = Mathf.MoveTowards(strike, 0f, Time.deltaTime * 2.5f);

        var sunDir = -skyLight.sun.transform.forward;
        var moonDir = skyLight.moon != null ? -skyLight.moon.transform.forward : -sunDir;
        float key = sunDir.y * 0.5f + 0.5f;
        float night = 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(-0.15f, 0.05f, sunDir.y));
        var disc = SunDisc.Evaluate(key) * Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(-0.06f, 0.03f, sunDir.y));

        // Overcast greys the sky toward its own brightness, so it works at noon and at midnight alike.
        var clearZenith = Zenith.Evaluate(key);
        var clearHorizon = Horizon.Evaluate(key);
        float brightness = clearHorizon.grayscale;
        var grey = new Color(brightness, brightness * 1.02f, brightness * 1.06f);
        var zenith = Color.Lerp(clearZenith, grey * 0.78f, shown.overcast) * (1f - 0.45f * shown.darkness);
        var horizon = Color.Lerp(clearHorizon, grey, shown.overcast) * (1f - 0.35f * shown.darkness);
        var ground = GroundColor * (Mathf.Clamp01(brightness / 0.8f) * (1f - 0.4f * shown.darkness));

        Shader.SetGlobalVector(SunDirectionId, sunDir);
        Shader.SetGlobalVector(MoonDirectionId, moonDir);
        Shader.SetGlobalColor(SunColorId, disc);
        Shader.SetGlobalColor(ZenithId, zenith);
        Shader.SetGlobalColor(HorizonId, horizon);
        Shader.SetGlobalColor(GroundId, ground);
        Shader.SetGlobalVector(CloudsId, new Vector4(shown.coverage, 0f, 1f, 0f));
        Shader.SetGlobalVector(CloudOffsetId, cloudOffset);
        Shader.SetGlobalFloat(NightId, night);
        var toStrike = (strikeAt - cam.transform.position).normalized;
        Shader.SetGlobalVector(LightningId, new Vector4(toStrike.x, toStrike.y, toStrike.z, strike));
        Shader.SetGlobalVector(LightningPosId, new Vector4(strikeAt.x, strikeAt.y, strikeAt.z, strike));
        PublishMaps(cam.transform.position);

        // The haze is the horizon's colour, so the land fades into the sky behind it.
        RenderSettings.fogColor = horizon;
    }

    // The weather's clouds: clear sky blending into the kind's look as it strengthens.
    static Look Wanted(WeatherNow now)
    {
        float s = now.Strength / 100f;
        return now.Kind switch
        {
            WeatherKind.Cloudy => Look.Lerp(Clear, Overcast, s),
            WeatherKind.Windy => Look.Lerp(Clear, Windy, s),
            WeatherKind.Fog => Look.Lerp(Clear, Fog, s),
            WeatherKind.Rain => Look.Lerp(Clear, Rain, s),
            WeatherKind.Storm => Look.Lerp(Clear, Storm, s),
            WeatherKind.Snow => Look.Lerp(Clear, Snow, s),
            _ => Clear,
        };
    }

    // The clouds drift with the wind (toward its direction: 0..3 N E S W, north is +z), and slowly without.
    void Drift(int windDir)
    {
        var dir = windDir switch
        {
            0 => Vector2.up,
            1 => Vector2.right,
            2 => Vector2.down,
            3 => Vector2.left,
            _ => new Vector2(0.8f, 0.6f),
        };
        float speed = windDir >= 0 ? 16f : 4f;
        cloudOffset += dir * (speed * Time.deltaTime / 1800f);
    }

    // One texel of weather over a wide square round the camera (Sky.hlsl's maps).
    void PublishMaps(Vector3 camera)
    {
        weatherMap.SetPixel(0, 0, new Color(shown.coverage, shown.darkness, shown.rain, 0f));
        weatherMap.Apply(false);
        wetnessMap.SetPixel(0, 0, new Color(shown.rain, shown.rain, 0f, 1f));
        wetnessMap.Apply(false);
        float baseFraction = shown.cloudBase / MaxCloudBase, thickFraction = shown.thickness / MaxCloudThickness;
        cloudMap.SetPixel(0, 0, new Color(baseFraction, thickFraction, 0f, 1f));
        cloudMap.Apply(false);
        Shader.SetGlobalVector(WeatherRectId, new Vector4(camera.x - MapSize / 2f, camera.z - MapSize / 2f, 1f / MapSize, 1f / MapSize));
        Shader.SetGlobalVector(ClimateId, new Vector4(shown.coverage, shown.darkness, baseFraction, thickFraction));
    }

    static Gradient Make(params (float time, Color color)[] keys)
    {
        var colorKeys = new GradientColorKey[keys.Length];
        for (int i = 0; i < keys.Length; i++) colorKeys[i] = new GradientColorKey(keys[i].color, keys[i].time);
        var gradient = new Gradient();
        gradient.SetKeys(colorKeys, new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, 1f) });
        return gradient;
    }
}
