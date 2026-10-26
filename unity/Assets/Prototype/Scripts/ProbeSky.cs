using System.Collections;
using System.Globalization;
using System.Linq;
using Chem;
using UnityEngine;

// The probe's sky scenario (docs/FAST_LOOP.md): the clock and the weather
// switched the way a mod switches them (a rule switch between ticks), and what
// the view makes of them. Noon, then night (the clock's start hour moved so
// the next tick lands there), then a storm with lightning every half second,
// then fog. Screenshots of each, over the meadow toward the pond.
public class SkyProbe : ProbeScenario
{
    SkyLight sky;
    WeatherFx fx;

    protected override IEnumerator Run()
    {
        sky = FindFirstObjectByType<SkyLight>();
        fx = FindFirstObjectByType<WeatherFx>();
        Check("sky_built", sky != null && fx != null && sky.moon != null, "Sky object with SkyLight, WeatherFx and a moon");
        if (sky == null || fx == null) yield break;
        Cut(V(40f, 10f, 1f), V(28f, 0f, 15f));

        yield return Switch(At(12f));
        yield return Wait(2f);
        Metric("noon_sun", sky.sun.intensity);
        Check("noon_is_bright", sky.sun.intensity > 1f && sky.Daylight > 0.9f, $"sun {sky.sun.intensity:0.00}, daylight {sky.Daylight:0.00}");
        Check("clock_line", SkyLight.ClockLine(World).Contains("12:0"), SkyLight.ClockLine(World));
        Shot("noon");

        yield return Switch(At(22f));
        yield return Wait(2f);
        Metric("night_ambient", RenderSettings.ambientIntensity);
        Check("night_is_dark", sky.sun.intensity < 0.01f && sky.moon.enabled && RenderSettings.ambientIntensity < 0.4f,
            $"sun {sky.sun.intensity:0.00}, moon {sky.moon.intensity:0.00}, ambient {RenderSettings.ambientIntensity:0.00}");
        Shot("night");

        yield return Storm();

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
        for (float t = 0f; t < 8f && !boltSeen; t += Time.deltaTime)
        {
            boltSeen = GameObject.Find("Lightning bolt") != null;
            yield return null;
        }
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
    }

    static string Only(string kind) =>
        "{ " + string.Join(", ", new[] { "clear", "cloudy", "windy", "fog", "rain", "storm" }.Select(k => $"'{k}': {(k == kind ? 1 : 0)}")) + " }";

    // The clock's start hour that puts the next tick at this hour of day 1 or later.
    string At(float hour)
    {
        var p = World.Rules.Sim;
        double elapsed = (double)(World.Tick + 1) * Sky.MinutesPerDay / Sky.Ticks(p, p.Day.LengthMinutes) / 60.0;
        double start = ((hour - elapsed) % 24 + 24) % 24;
        return "'day': { 'startHour': " + start.ToString("0.###", CultureInfo.InvariantCulture) + " }";
    }

    // A rule switch to a probe mod over sim.json (replacing the last one), in force at the next tick.
    IEnumerator Switch(string sim)
    {
        var mod = new ActiveMod("probe-sky", "{ 'patch': { 'sim': { " + sim + " } } }");
        grid.QueueRules(grid.CurrentRules.WithMods(new[] { mod }));
        yield return Wait(0.5f);
    }
}
