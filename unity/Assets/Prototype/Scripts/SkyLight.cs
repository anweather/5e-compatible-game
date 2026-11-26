using Chem;
using UnityEngine;
using UnityEngine.Rendering;

// The sun, the moon, the sky and the haze, from the Chem world's clock and
// weather (Chem.Sky): the sun rises at 6:00 and sets at 18:00, a dim blue
// moon lights the night, and clouds, rain and fog dull the light and say
// how far they close the haze in (AirInPlay makes the air from them). Presentation only: the time and the weather are the core's,
// so every player sees the same sky. Also the clock line at the top right.
// The dragon darkens all of it (Menace, DragonFlight.Darkness), read from the
// core's flight, so that's the same on every machine too.
//
// The ambient light is baked from the noon sky once, at the start, and
// then scaled and tinted each frame for the hour, the cloud, the dawn and
// lightning (a Custom ambient probe). Baking it again from the skybox
// (DynamicGI.UpdateEnvironment) stalls the main thread on a GPU readback,
// up to 120 ms, so it's never done after the start.
public class SkyLight : MonoBehaviour
{
    public Light sun;
    public Light moon;
    public float bearing = -40f;
    public float tilt = 35f;

    ChemGrid grid;
    Material sky;
    float baseExposure = 1.15f, baseThickness = 0.9f;
    Color baseTint = new Color(0.45f, 0.55f, 0.75f);
    float baseFogStart, baseFogEnd;
    const float ClockGlide = 0.3f;
    double shownClock = -1;
    float cloud, fog, flash;
    SphericalHarmonicsL2 daylightAmbient;
    bool ambientBaked;
    int bakeTries;
    GUIStyle style;

    public void Flash(float amount) => flash = Mathf.Max(flash, amount);

    public float Menace { get; private set; }

    const float MenaceEase = 0.2f;

    public float Daylight { get; private set; } = 1f;

    void Start()
    {
        grid = ChemGrid.Current?.Grid;
        if (sun == null) sun = RenderSettings.sun;
        if (RenderSettings.skybox != null)
        {
            sky = new Material(RenderSettings.skybox);
            RenderSettings.skybox = sky;
            if (sky.HasProperty("_Exposure")) baseExposure = sky.GetFloat("_Exposure");
            if (sky.HasProperty("_AtmosphereThickness")) baseThickness = sky.GetFloat("_AtmosphereThickness");
            if (sky.HasProperty("_SkyTint")) baseTint = sky.GetColor("_SkyTint");
        }
        baseFogStart = RenderSettings.fogStartDistance;
        baseFogEnd = RenderSettings.fogEndDistance;
    }

    void BakeDaylight()
    {
        Place(sun.transform, Sky.MinutesPerDay / 2f);
        RenderSettings.sun = sun;
        RenderSettings.ambientMode = AmbientMode.Skybox;
        DynamicGI.UpdateEnvironment();
        var probe = RenderSettings.ambientProbe;
        if (probe[1, 0] < 0.1f && ++bakeTries < 60) return;
        daylightAmbient = probe;
        RenderSettings.ambientMode = AmbientMode.Custom;
        ambientBaked = true;
    }

    public bool Held { get; set; }

    double ShownClock(World world)
    {
        double target = world.ClockTick;
        double quarterDay = Sky.Ticks(world.Rules.Sim, world.Rules.Sim.Day.LengthMinutes) / 4.0;
        if (shownClock < 0 || target < shownClock - 1 || target - shownClock > quarterDay) shownClock = target;
        else shownClock += (target - shownClock) * Mathf.Min(1f, Time.deltaTime / ClockGlide);
        return shownClock;
    }

    void Update()
    {
        var world = grid != null ? grid.World : null;
        if (world == null || sun == null || Held) return;
        if (!ambientBaked) BakeDaylight();
        var now = world.Weather;
        cloud = Mathf.MoveTowards(cloud, CloudOf(now), Time.deltaTime * 0.3f);
        Menace = Mathf.MoveTowards(Menace, DragonFlight.Darkness(world), Time.deltaTime * MenaceEase);
        fog = Mathf.MoveTowards(fog, now.Fog / 100f, Time.deltaTime * 0.2f);
        float minute = (float)(Sky.MinutesSmooth(world.Rules.Sim, ShownClock(world)) % Sky.MinutesPerDay);
        float sunUp = Place(sun.transform, minute);
        float moonUp = moon != null ? Place(moon.transform, minute + Sky.MinutesPerDay / 2f) : 0f;
        var lit = moon != null && sunUp < -0.05f ? moon : sun;
        if (RenderSettings.sun != lit) RenderSettings.sun = lit;
        Shine(sunUp, moonUp);
    }

    static float CloudOf(WeatherNow now) => now.Kind switch
    {
        WeatherKind.Cloudy => now.Strength / 100f * 0.8f,
        WeatherKind.Rain => now.Strength / 100f * 0.85f,
        WeatherKind.Storm => now.Strength / 100f,
        WeatherKind.Snow => now.Strength / 100f * 0.7f,
        WeatherKind.Fog => now.Strength / 100f * 0.5f,
        WeatherKind.Windy => now.Strength / 100f * 0.2f,
        _ => 0f,
    };

    float Place(Transform body, float minute)
    {
        float turn = minute / Sky.MinutesPerDay * 360f - 90f;
        body.rotation = Quaternion.Euler(0f, bearing, 0f) * Quaternion.AngleAxis(tilt, Vector3.forward) * Quaternion.Euler(turn, 0f, 0f);
        return -body.forward.y;
    }

    public const float LowSunShadows = 0.06f;

    void Shine(float sunUp, float moonUp)
    {
        float clear = (1f - 0.65f * cloud) * (1f - 0.8f * Menace);
        float sunLight = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(-0.04f, 0.25f, sunUp));
        sun.intensity = 1.6f * sunLight * clear;
        sun.color = Color.Lerp(new Color(1f, 0.55f, 0.32f), new Color(1f, 0.92f, 0.82f), Mathf.InverseLerp(0.02f, 0.45f, sunUp));
        float shade = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(LowSunShadows, LowSunShadows * 2f, sunUp));
        sun.shadowStrength = shade;
        sun.shadows = sun.intensity > 0.02f && shade > 0.01f ? LightShadows.Soft : LightShadows.None;
        if (moon != null)
        {
            moon.intensity = 0.5f * Mathf.InverseLerp(-0.04f, 0.2f, moonUp) * (1f - 0.75f * cloud) * (1f - sunLight);
            moon.enabled = moon.intensity > 0.005f;
        }
        Daylight = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(-0.2f, 0.2f, sunUp));
        flash = Mathf.MoveTowards(flash, 0f, Time.deltaTime * 3f);
        RenderSettings.ambientIntensity = Mathf.Lerp(0.3f, 1f, Daylight) * (1f - 0.3f * cloud) + 1.5f * flash;
        RenderSettings.reflectionIntensity = Mathf.Lerp(0.15f, 1f, Daylight);
        if (!ambientBaked) return;
        Ambient(sunUp, sunLight);
        if (sky != null)
        {
            if (sky.HasProperty("_Exposure")) sky.SetFloat("_Exposure", baseExposure * Mathf.Lerp(0.25f, 1f, Daylight) * (1f - 0.45f * cloud) + flash);
            if (sky.HasProperty("_AtmosphereThickness")) sky.SetFloat("_AtmosphereThickness", baseThickness + 0.8f * cloud);
            if (sky.HasProperty("_SkyTint")) sky.SetColor("_SkyTint", Color.Lerp(baseTint, new Color(0.5f, 0.5f, 0.52f), cloud));
        }
    }

    void Ambient(float sunUp, float sunLight)
    {
        float level = RenderSettings.ambientIntensity * Mathf.Lerp(0.25f, 1f, Daylight) * (1f - 0.45f * cloud) * (1f - 0.65f * Menace);
        var tint = Color.Lerp(new Color(0.55f, 0.65f, 1f), Color.white, Daylight);
        tint *= Color.Lerp(Color.white, new Color(0.92f, 0.94f, 1f), cloud);
        float low = (1f - Mathf.InverseLerp(0.02f, 0.45f, sunUp)) * sunLight;
        tint *= Color.Lerp(Color.white, sun.color, 0.35f * low);
        var probe = daylightAmbient;
        for (int c = 0; c < 9; c++)
        {
            probe[0, c] *= tint.r * level;
            probe[1, c] *= tint.g * level;
            probe[2, c] *= tint.b * level;
        }
        RenderSettings.ambientProbe = probe;
    }

    public float Rain => Mathf.Clamp01(cloud - 0.5f) * 2f;

    public float Fog => fog;

    public (float start, float end) LinearHaze =>
        (Mathf.Lerp(Mathf.Lerp(baseFogStart, 40f, Rain), 0f, fog), Mathf.Lerp(Mathf.Lerp(baseFogEnd, 1200f, Rain), 90f, fog));

    void OnGUI()
    {
        var world = grid != null ? grid.World : null;
        if (!EventLog.HudVisible || world == null) return;
        style ??= new GUIStyle(GUI.skin.label) { fontSize = 18, richText = true, alignment = TextAnchor.UpperRight };
        GUI.Label(new Rect(Screen.width - 436, 40, 420, 30), ClockLine(world), style);
    }

    static readonly string[] WindFrom = { "south", "west", "north", "east" };

    public static string ClockLine(World world)
    {
        long minutes = world.Minutes;
        int m = Sky.MinuteOfDay(minutes);
        var now = world.Weather;
        string weather = now.Kind == WeatherKind.Clear ? "Clear" : now.Kind.ToString();
        string wind = world.WindDir >= 0 ? $" · wind from the {WindFrom[world.WindDir]}" : "";
        return $"<color=#ffffffcc>Day {Sky.Day(minutes)} · {m / 60:00}:{m % 60:00} · {weather}{wind}</color>";
    }
}
