using System.Collections;
using System.Linq;
using UnityEngine;

// The probe's wet scenario (docs/FAST_LOOP.md): rain wets the ground (WetGround,
// J-024). The meadow north of the yard at noon, clear, then heavy rain, then
// clear again. Checks the ground soaks in the rain (darker and glossier
// terrain layers) and dries again after, and takes a dry and a wet shot from the
// same place for comparing.
public class WetProbe : ProbeScenario
{
    static string Only(string kind) =>
        "{ " + string.Join(", ", new[] { "clear", "cloudy", "windy", "fog", "rain", "storm", "snow" }
            .Select(k => $"'{k}': {(k == kind ? 1 : 0)}")) + " }";

    static string Weather(string kind) => ", 'weather': { 'calmPeriods': 0, 'rampMinutes': 0.02, 'weights': " + Only(kind) + " }";

    protected override IEnumerator Run()
    {
        var valley = grid.valley;
        Check("valley_built", valley != null);
        if (valley == null) yield break;

        yield return Switch(At(12f) + Weather("clear"));
        Place(13, 10);
        Cut(Yard(13, 4) + V(-6f, 4f, -10f), Yard(13, 0));
        yield return Wait(4f);
        Metric("dry_wetness", valley.Wetness);
        Check("dry_in_the_sun", valley.Wetness < 0.05f, $"wetness {valley.Wetness:0.00}");
        Shot("dry");

        yield return Switch(At(12f) + Weather("rain"));
        yield return Wait(14f);
        Metric("rain", World.Weather.Rain);
        Metric("wet_wetness", valley.Wetness);
        Check("soaks_in_the_rain", valley.Wetness > 0.8f, $"wetness {valley.Wetness:0.00} with rain at {World.Weather.Rain}");
        Shot("wet");

        yield return Switch(At(12.5f) + Weather("clear"));
        yield return Wait(20f);
        Metric("drying_wetness", valley.Wetness);
        Check("dries_after", valley.Wetness < 0.9f && valley.Wetness > 0f, $"wetness {valley.Wetness:0.00} 20 s after the rain stops (dries over about 90 s)");
    }
}
