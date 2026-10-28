using System.Collections;
using System.Linq;
using Chem;
using UnityEngine;

// The probe's sky scenario (docs/FAST_LOOP.md): the clock and the weather
// switched the way a mod switches them (a rule switch between ticks), and what
// the view makes of them. Noon, then night (the clock's start hour moved so
// the next tick lands there), then a storm with lightning every half second,
// then a meteor shower at night (meteors every half second), then fog.
// Screenshots of each, over the meadow toward the pond, and of the sky
// (CloudSky) at noon, sunset, night and in the storm.
public class SkyProbe : ProbeScenario
{
    SkyLight sky;
    WeatherFx fx;

    protected override IEnumerator Run()
    {
        sky = FindAnyObjectByType<SkyLight>();
        fx = FindAnyObjectByType<WeatherFx>();
        Check("sky_built", sky != null && fx != null && sky.moon != null, "Sky object with SkyLight, WeatherFx and a moon");
        if (sky == null || fx == null) yield break;
        Cut(V(40f, 10f, 1f), V(28f, 0f, 15f));

        yield return Switch(At(12f));
        yield return Wait(2f);
        Metric("noon_sun", sky.sun.intensity);
        Check("noon_is_bright", sky.sun.intensity > 1f && sky.Daylight > 0.9f, $"sun {sky.sun.intensity:0.00}, daylight {sky.Daylight:0.00}");
        Check("clock_line", SkyLight.ClockLine(World).Contains("12:0"), SkyLight.ClockLine(World));
        Shot("noon");
        yield return SkyShot("noon");

        yield return Switch(At(18.3f));
        yield return Wait(2f);
        yield return SkyShot("sunset");

        yield return Switch(At(22f));
        yield return Wait(2f);
        Metric("night_ambient", RenderSettings.ambientIntensity);
        Check("night_is_dark", sky.sun.intensity < 0.01f && sky.moon.enabled && RenderSettings.ambientIntensity < 0.4f,
            $"sun {sky.sun.intensity:0.00}, moon {sky.moon.intensity:0.00}, ambient {RenderSettings.ambientIntensity:0.00}");
        Shot("night");
        yield return SkyShot("night");

        yield return Storm();
        yield return Shower();
        yield return Snowfall();

        yield return Switch(At(10f) + ", 'weather': { 'calmPeriods': 0, 'rampMinutes': 0.02, 'weights': " + Only("fog") + " }");
        yield return Wait(6f);
        Metric("fog_end_m", RenderSettings.fogEndDistance);
        Check("fog_closes_in", World.Weather.Kind == WeatherKind.Fog && RenderSettings.fogEndDistance < 300f,
            $"{World.Weather.Kind} {World.Weather.Strength}, fog ends at {RenderSettings.fogEndDistance:0} m");
        Shot("fog");
    }

    // A storm at midday: rain falls, the wind blows, lightning strikes (the core's) and the view draws the bolts.
    IEnumerator Storm()
    {
        int logged = World.LogAppended;
        yield return Switch(At(13f) + ", 'weather': { 'calmPeriods': 0, 'rampMinutes': 0.02, 'weights': " + Only("storm") +
            ", 'storm': { 'strikeEvery': 3, 'strikeChance': 1 } }");
        bool boltSeen = false;
        // Up to 20 s (8 s was too short about half the time: the storm can take a while to come in).
        float waited = 0f;
        for (; waited < 20f && !boltSeen; waited += Time.deltaTime)
        {
            boltSeen = GameObject.Find("Lightning bolt") != null;
            yield return null;
        }
        Metric("first_bolt_s", waited);
        if (boltSeen) Shot("lightning");
        yield return Wait(4f);
        var now = World.Weather;
        var rain = fx.GetComponentInChildren<ParticleSystem>();
        int strikes = World.Log.Where(l => l.Text == "Lightning strikes").Sum(l => l.Count);
        Metric("rain_particles", rain != null ? rain.particleCount : 0);
        Metric("strikes", strikes);
        Check("storm_in_force", now.Kind == WeatherKind.Storm && now.Strength == 100 && now.WindDir >= 0,
            $"{now.Kind} {now.Strength}, wind {now.WindDir}");
        Check("rain_falls", rain != null && rain.particleCount > 500, $"{(rain != null ? rain.particleCount : 0)} drops in the air");
        Check("lightning_strikes", strikes > 0 && boltSeen, $"{strikes} strikes in the log, bolt drawn {boltSeen}");
        Check("storm_is_dim", sky.sun.intensity < 0.8f, $"sun {sky.sun.intensity:0.00}");
        Metric("log_lines_in_storm", World.LogAppended - logged);
        Shot("storm");
        var clouds = FindAnyObjectByType<CloudSky>();
        Metric("storm_cloud_cover", clouds != null ? clouds.Coverage : 0f);
        Check("storm_clouds", clouds != null && clouds.Coverage > 0.7f, $"cloud cover {(clouds != null ? clouds.Coverage : 0f):0.00}");
        yield return SkyShot("storm");
    }

    // A meteor shower at night: a clear stretch that is one, meteors coming down (the core's) and drawn falling.
    IEnumerator Shower()
    {
        yield return Switch(At(23f) + ", 'weather': { 'calmPeriods': 0, 'rampMinutes': 0.02, 'weights': " + Only("clear") +
            ", 'meteors': { 'showerChance': 1, 'every': 3, 'chance': 1, 'fallTicks': 11 } }");
        Cut(V(40f, 6f, 1f), V(28f, 30f, 60f));
        GameObject meteor = null;
        for (float t = 0f; t < 8f && meteor == null; t += Time.deltaTime)
        {
            meteor = GameObject.Find("Meteor");
            yield return null;
        }
        Check("meteor_shower", World.Weather.Meteors > 0, $"{World.Weather.Kind}, meteors {World.Weather.Meteors}");
        Check("meteors_fall", meteor != null, meteor != null ? $"one {meteor.transform.position.y:0} m up" : "none drawn in 8 s");
        yield return Wait(0.6f);
        Shot("meteors");
        yield return Wait(3f);
    }

    // Snow at noon over puddles laid in the yard: flakes fall, the sky greys, the puddles freeze over.
    IEnumerator Snowfall()
    {
        var puddles = new System.Collections.Generic.List<Cell>();
        for (int x = 16; x < 24; x++)
            for (int y = 2; y < 6; y++)
            {
                var cell = grid.YardCell(x, y);
                var c = World.At(cell.x, cell.y);
                if (c == null || c.Water > 0 || World.IsBlocking(c)) continue;
                c.Water = 1;
                puddles.Add(c);
            }
        yield return Switch(At(12f) + ", 'weather': { 'calmPeriods': 0, 'rampMinutes': 0.02, 'weights': " + Only("snow") +
            ", 'snow': { 'freezeEvery': 4, 'freezeChance': 0.5 } }");
        yield return Wait(8f);
        var snowGo = fx.transform.Find("Snow");
        var flakes = snowGo != null ? snowGo.GetComponent<ParticleSystem>() : null;
        int frozen = puddles.Count(c => c.Frozen && c.Water > 0);
        Metric("snowflakes", flakes != null ? flakes.particleCount : 0);
        Metric("puddles_frozen", frozen);
        Check("snow_falls", World.Weather.Kind == WeatherKind.Snow && flakes != null && flakes.particleCount > 2000,
            $"{World.Weather.Kind} {World.Weather.Strength}, {(flakes != null ? flakes.particleCount : 0)} flakes in the air");
        Check("puddles_freeze", puddles.Count > 5 && frozen > puddles.Count / 2, $"{frozen} of {puddles.Count} puddles froze");
        Shot("snow");
        yield return SkyShot("snow");
    }

    // The sky itself (CloudSky): looking up over the trees toward the mountains, then back to the meadow.
    IEnumerator SkyShot(string label)
    {
        Cut(V(40f, 4f, 1f), V(-40f, 45f, 160f));
        yield return null;
        Shot(label + "-sky");
        // The capture is taken at the end of a later frame: hold the shot until it is.
        for (int i = 0; i < 3; i++) yield return null;
        Cut(V(40f, 10f, 1f), V(28f, 0f, 15f));
    }

    static string Only(string kind) =>
        "{ " + string.Join(", ", new[] { "clear", "cloudy", "windy", "fog", "rain", "storm", "snow" }.Select(k => $"'{k}': {(k == kind ? 1 : 0)}")) + " }";

}
