using System.Collections;
using System.Linq;
using Chem;
using UnityEngine;

// The probe's time-and-weather scenario (docs/FAST_LOOP.md): the three Time &
// Weather spells cast from the bar, as a player casts them. A clear morning;
// Hasten Time runs the clock hours ahead in seconds (and a second cast settles
// it back); Control Weather turns the sky to rain, then snow, then a storm, then
// clear, each drawn by WeatherFx; Fog Cloud lays a bank of fog that hides a
// Misty Step aimed through it, a Fire Bolt burns a hole in it and a Gust of
// Wind blows it away. Shots of the hastened sky, the rain, the snow and the fog.
public class TimeWeatherProbe : ProbeScenario
{
    // The wizard's square, the fog's centre (radius 4: x 5..13) and a blink target past it, in yard squares.
    const int MeX = 4, MeY = 10, FogX = 9, FogY = 10, PastX = 15;

    WeatherFx fx;

    protected override IEnumerator Run()
    {
        fx = FindFirstObjectByType<WeatherFx>();
        Check("weather_fx", fx != null, "the Sky's WeatherFx");
        Place(MeX, MeY);
        Cut(Yard(MeX, MeY) + V(-6f, 6f, -12f), Yard(FogX, FogY) + V(0f, 3f, 0f));
        // A clear morning to start from: clear natural weather, short ramps.
        yield return Switch(At(9f) + ", 'weather': { 'calmPeriods': 0, 'rampMinutes': 0.02, " +
            "'weights': { 'clear': 1, 'cloudy': 0, 'windy': 0, 'fog': 0, 'rain': 0, 'storm': 0, 'snow': 0 } }");
        yield return HastenTime();
        yield return ControlWeather();
        yield return FogCloud();
    }

    IEnumerator HastenTime()
    {
        long before = World.Minutes;
        float t0 = Time.time;
        yield return CastFromBar(SpellIds.HastenTime, MeX, MeY, 1, 6f);
        long gained = World.Minutes - before;
        float seconds = Time.time - t0;
        Metric("hasted_game_minutes", gained);
        Metric("hasted_real_seconds", seconds);
        Check("clock_races", World.Haste.HasValue && gained >= 90,
            $"{gained} game minutes in {seconds:0.0} s (about {seconds:0} at the clock's own pace), haste {World.Haste?.rate}x");
        Shot("hastened");
        yield return null;
        yield return null;

        // Cast again while it runs: time settles back to its own pace.
        yield return CastFromBar(SpellIds.HastenTime, MeX, MeY, 1, 0.3f);
        long settled = World.Minutes;
        yield return Wait(3f);
        long after = World.Minutes - settled;
        Metric("settled_game_minutes_3s", after);
        Check("second_cast_settles", !World.Haste.HasValue && after <= 6, $"{after} game minutes in 3 s once settled");
    }

    IEnumerator ControlWeather()
    {
        // From clear: rain, snow, storm, then clear again, a step a cast.
        var expected = new[] { WeatherKind.Rain, WeatherKind.Snow, WeatherKind.Storm, WeatherKind.Clear };
        foreach (var kind in expected)
        {
            yield return CastFromBar(SpellIds.ControlWeather, MeX, MeY, 1, 4f);
            var now = World.Weather;
            string name = kind.ToString().ToLowerInvariant();
            Check("weather_" + name, now.Kind == kind && World.WeatherSet.HasValue,
                $"weather {now.Kind} at {now.Strength}, held until tick {World.WeatherSet?.until} (now {World.Tick})");
            if (kind == WeatherKind.Rain && fx != null)
            {
                Metric("rain_shown", fx.RainShown);
                Check("rain_drawn", fx.RainShown > 0.3f, $"WeatherFx draws rain at {fx.RainShown:0.00}");
                Shot("rain");
            }
            if (kind == WeatherKind.Snow && fx != null)
            {
                Metric("snow_shown", fx.SnowShown);
                Check("snow_drawn", fx.SnowShown > 0.3f && fx.RainShown < 0.5f, $"WeatherFx draws snow at {fx.SnowShown:0.00}, rain at {fx.RainShown:0.00}");
                Shot("snow");
            }
            yield return null;
            yield return null;
        }
    }

    IEnumerator FogCloud()
    {
        var spell = World.Rules.Spell(SpellIds.MistyStep);
        var me = grid.YardCell(MeX, MeY);
        var past = grid.YardCell(PastX, MeY);
        // The storm's lightning may have lit something, and fire would burn the new fog off.
        PutOutFires();
        string before = MistyStep.Check(grid, spell, wizard.transform, past.x, past.y);

        yield return CastFromBar(SpellIds.FogCloud, FogX, FogY, 1, 1.5f);
        var squares = SpellCells(SpellIds.FogCloud, FogX, FogY, 1);
        int thick = squares.Count(s => World.Fogged(World.At(s.x, s.y)));
        int drawn = grid.Fog.SquaresShown;
        Metric("fog_squares", thick);
        Metric("fog_drawn", drawn);
        Check("fog_lies", thick >= squares.Count * 2 / 3, $"{thick}/{squares.Count} squares in thick fog");
        Check("fog_drawn", drawn >= thick * 2 / 3, $"FogBank draws {drawn} squares");
        Check("fog_hides", World.FogBetween(me.x, me.y, past.x, past.y) && !World.LineOfSight(me.x, me.y, past.x, past.y),
            "no line of sight through the fog");
        Shot("fog");
        yield return null;
        yield return null;

        // Misty Step aimed through it, from the bar: it won't go.
        string through = MistyStep.Check(grid, spell, wizard.transform, past.x, past.y);
        var from = wizard.transform.position;
        yield return CastFromBar(SpellIds.MistyStep, PastX, MeY, 1, 0.8f);
        var now = grid.CellAt(wizard.transform.position);
        Check("blink_refused", through != null && through.Contains("fog", System.StringComparison.Ordinal) && now.x < past.x - 1,
            $"before the fog: {before ?? "ok"}; through it: {through ?? "ok"}; the wizard moved {Vector3.Distance(from, wizard.transform.position):0.0} m");

        // A Fire Bolt into the middle burns the fog off there.
        // The fog round it drifts straight back in, so the rule's own line in the log counts too.
        var mid = grid.YardCell(FogX, FogY);
        int boltTick = World.Tick;
        yield return CastFromBar(SpellIds.FireBolt, FogX, FogY, 1, 0.8f);
        var c = World.At(mid.x, mid.y);
        bool burnt = LogSince(boltTick).Any(l => l.Contains("burns the fog away", System.StringComparison.Ordinal));
        Check("fire_burns_fog", burnt || c.Gas != GasIds.Fog, $"burnt {burnt}; the middle square's gas now: {c.Gas ?? "none"} {c.GasAmt}");
        PutOutFires();

        // A Gust of Wind east through it blows it away.
        // As the core's test measures it: the fog's middle (weighted by how thick) moves east.
        float beforeGust = FogMiddleX(mid);
        yield return CastFromBar(SpellIds.GustOfWind, MeX + 3, MeY, 1, 2f);
        float afterGust = FogMiddleX(mid);
        Metric("fog_middle_x_before_gust", beforeGust);
        Metric("fog_middle_x_after_gust", afterGust);
        Check("gust_blows_fog", afterGust > beforeGust + 1f, $"the fog's middle from square x {beforeGust:0.0} to {afterGust:0.0}");
    }

    // The x of the fog's middle round a square (20 squares each way), weighted by how thick it is.
    float FogMiddleX(Vector2Int around)
    {
        float sum = 0f, weight = 0f;
        for (int y = around.y - 20; y <= around.y + 20; y++)
            for (int x = around.x - 20; x <= around.x + 20; x++)
            {
                var c = World.At(x, y);
                if (c == null || c.Gas != GasIds.Fog) continue;
                sum += x * c.GasAmt;
                weight += c.GasAmt;
            }
        return weight > 0f ? sum / weight : around.x;
    }
}
