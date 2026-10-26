using Chem;
using UnityEngine;
using UnityEngine.Rendering;

// The sun, the moon, the sky and the haze, from the Chem world's clock and
// weather (Chem.Sky): the sun rises at 6:00 and sets at 18:00, a dim blue
// moon lights the night, and clouds, rain and fog dull the light and close
// the haze in. Presentation only: the time and the weather are the core's,
// so every player sees the same sky. Also the clock line at the top right.
public class SkyLight : MonoBehaviour
{
    public Light sun;
    public Light moon;
    /// <summary>The sun's compass bearing (degrees about up), as the yard was lit before the clock.</summary>
    public float bearing = -40f;
    /// <summary>How far the sun's path leans from overhead: noon stands this many degrees short of straight up.</summary>
    public float tilt = 35f;

    ChemGrid grid;
    Material sky;
    float baseExposure = 1.15f, baseThickness = 0.9f;
    Color baseTint = new Color(0.45f, 0.55f, 0.75f);
    Color baseFog;
    float baseFogStart, baseFogEnd;
    float cloud, fog, flash, nextAmbient;
    GUIStyle style;

    /// <summary>Lights the whole sky up for a moment (lightning; 1 is a strike close by).</summary>
    public void Flash(float amount) => flash = Mathf.Max(flash, amount);

    /// <summary>0 at night to 1 by day, the sun's light on the land (for WeatherFx and anything that dims at night).</summary>
    public float Daylight { get; private set; } = 1f;

    void Start()
    {
        grid = FindFirstObjectByType<ChemGrid>();
        if (sun == null) sun = RenderSettings.sun;
        // A copy, so the time of day never writes to the sky material asset.
        if (RenderSettings.skybox != null)
        {
            sky = new Material(RenderSettings.skybox);
            RenderSettings.skybox = sky;
            if (sky.HasProperty("_Exposure")) baseExposure = sky.GetFloat("_Exposure");
            if (sky.HasProperty("_AtmosphereThickness")) baseThickness = sky.GetFloat("_AtmosphereThickness");
            if (sky.HasProperty("_SkyTint")) baseTint = sky.GetColor("_SkyTint");
        }
        baseFog = RenderSettings.fogColor;
        baseFogStart = RenderSettings.fogStartDistance;
        baseFogEnd = RenderSettings.fogEndDistance;
    }

    void Update()
    {
        var world = grid != null ? grid.World : null;
        if (world == null || sun == null) return;
        var now = world.Weather;
        cloud = Mathf.MoveTowards(cloud, CloudOf(now), Time.deltaTime * 0.3f);
        fog = Mathf.MoveTowards(fog, now.Fog / 100f, Time.deltaTime * 0.2f);
        float minute = (float)(Sky.MinutesSmooth(world.Rules.Sim, world.Tick) % Sky.MinutesPerDay);
        float sunUp = Place(sun.transform, minute);
        float moonUp = moon != null ? Place(moon.transform, minute + Sky.MinutesPerDay / 2f) : 0f;
        // The procedural sky lights itself from RenderSettings.sun: the sun by day, the moon by night
        // (with the moon in it by day, the daytime sky drew black).
        var lit = moon != null && sunUp < -0.05f ? moon : sun;
        if (RenderSettings.sun != lit) RenderSettings.sun = lit;
        Shine(sunUp, moonUp);
        Haze();
    }

    /// <summary>How much the sky is overcast, 0..1.</summary>
    static float CloudOf(WeatherNow now) => now.Kind switch
    {
        WeatherKind.Cloudy => now.Strength / 100f * 0.8f,
        WeatherKind.Rain => now.Strength / 100f * 0.85f,
        WeatherKind.Storm => now.Strength / 100f,
        WeatherKind.Fog => now.Strength / 100f * 0.5f,
        WeatherKind.Windy => now.Strength / 100f * 0.2f,
        _ => 0f,
    };

    // Turns a body to where it stands at this minute (6:00 rising, 12:00 at its height); returns its height, -1..1.
    float Place(Transform body, float minute)
    {
        float turn = minute / Sky.MinutesPerDay * 360f - 90f;
        body.rotation = Quaternion.Euler(0f, bearing, 0f) * Quaternion.AngleAxis(tilt, Vector3.forward) * Quaternion.Euler(turn, 0f, 0f);
        return -body.forward.y;
    }

    void Shine(float sunUp, float moonUp)
    {
        float clear = 1f - 0.65f * cloud;
        float sunLight = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(-0.04f, 0.25f, sunUp));
        sun.intensity = 1.6f * sunLight * clear;
        sun.color = Color.Lerp(new Color(1f, 0.55f, 0.32f), new Color(1f, 0.92f, 0.82f), Mathf.InverseLerp(0.02f, 0.45f, sunUp));
        sun.shadows = sun.intensity > 0.02f ? LightShadows.Soft : LightShadows.None;
        if (moon != null)
        {
            moon.intensity = 0.5f * Mathf.InverseLerp(-0.04f, 0.2f, moonUp) * (1f - 0.75f * cloud) * (1f - sunLight);
            moon.enabled = moon.intensity > 0.005f;
        }
        Daylight = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(-0.2f, 0.2f, sunUp));
        flash = Mathf.MoveTowards(flash, 0f, Time.deltaTime * 3f);
        RenderSettings.ambientIntensity = Mathf.Lerp(0.3f, 1f, Daylight) * (1f - 0.3f * cloud) + 1.5f * flash;
        RenderSettings.reflectionIntensity = Mathf.Lerp(0.15f, 1f, Daylight);
        if (sky != null)
        {
            if (sky.HasProperty("_Exposure")) sky.SetFloat("_Exposure", baseExposure * Mathf.Lerp(0.25f, 1f, Daylight) * (1f - 0.45f * cloud) + flash);
            if (sky.HasProperty("_AtmosphereThickness")) sky.SetFloat("_AtmosphereThickness", baseThickness + 0.8f * cloud);
            if (sky.HasProperty("_SkyTint")) sky.SetColor("_SkyTint", Color.Lerp(baseTint, new Color(0.5f, 0.5f, 0.52f), cloud));
        }
        // The ambient light comes from the sky: bake it again now and then as the sky changes.
        if (Time.unscaledTime >= nextAmbient)
        {
            nextAmbient = Time.unscaledTime + 1.5f;
            DynamicGI.UpdateEnvironment();
        }
    }

    // The haze darkens with the light and greys under cloud; fog closes it right in, rain partway.
    void Haze()
    {
        var grey = Color.Lerp(baseFog, new Color(0.6f, 0.62f, 0.64f), cloud);
        RenderSettings.fogColor = Color.Lerp(new Color(0.04f, 0.05f, 0.08f), grey, Daylight);
        float rain = Mathf.Clamp01(cloud - 0.5f) * 2f;
        RenderSettings.fogStartDistance = Mathf.Lerp(Mathf.Lerp(baseFogStart, 40f, rain), 0f, fog);
        RenderSettings.fogEndDistance = Mathf.Lerp(Mathf.Lerp(baseFogEnd, 1200f, rain), 90f, fog);
    }

    // Day, time and weather at the top right, under the online status line.
    void OnGUI()
    {
        var world = grid != null ? grid.World : null;
        if (!EventLog.HudVisible || world == null) return;
        style ??= new GUIStyle(GUI.skin.label) { fontSize = 18, richText = true, alignment = TextAnchor.UpperRight };
        GUI.Label(new Rect(Screen.width - 436, 40, 420, 30), ClockLine(world), style);
    }

    static readonly string[] WindFrom = { "south", "west", "north", "east" };

    /// <summary>"Day 1 · 09:00 · Rain · wind from the west": the wind blows toward its direction, so it comes from the opposite side.</summary>
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
