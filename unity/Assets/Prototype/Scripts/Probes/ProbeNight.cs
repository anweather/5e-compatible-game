using System.Collections;
using System.Linq;
using Chem;
using UnityEngine;

// The probe's night scenario (docs/FAST_LOOP.md): the view's half of the night
// (Nightfall; the rules' half is World.Night). Hollowmere's square from above
// at 16:00, then dusk (18:30) and night (21:30), clear. Checks the rules' light
// level at each, the village's door lamps off by day and on from dusk, the
// windows glowing, the stars out at night, the fireflies out by the hour, and
// that the wizard, standing in the dark with no light, is told how to see.
public class NightProbe : ProbeScenario
{
    static readonly int LampGlow = Shader.PropertyToID("_LampGlow");
    static readonly int StarBright = Shader.PropertyToID("_PunchyStarBright");
    static readonly int Critters = Shader.PropertyToID("_PunchyCritters");

    const string Clear = "{ 'clear': 1, 'cloudy': 0, 'windy': 0, 'fog': 0, 'rain': 0, 'storm': 0, 'snow': 0 }";
    static string Weather => ", 'weather': { 'calmPeriods': 0, 'rampMinutes': 0.02, 'weights': " + Clear + " }";

    protected override IEnumerator Run()
    {
        var village = FindObjectsByType<Village>(FindObjectsSortMode.None).FirstOrDefault(v => v.villageName == "Hollowmere");
        Check("village_found", village != null, village != null ? "Hollowmere" : "no Hollowmere");
        if (village == null) yield break;
        var square = village.transform.position + village.wellAt;

        // Afternoon: bright, no lamps.
        yield return Switch(At(16f) + Weather);
        // Out of the village, on open ground away from its lamps, for the dark later.
        var away = square + new Vector3(40f, 0f, -40f);
        Teleport(new Vector3(away.x, grid.GroundAt(away) + 0.1f, away.z));
        Cut(square + V(-14f, 10f, -14f), square);
        yield return Wait(5f);
        Check("bright_by_day", World.Daylight == LightLevel.Bright, $"{World.Daylight} at {SkyLight.ClockLine(World)}");
        Check("lamps_off_by_day", LitLamps(village) == 0 && Shader.GetGlobalFloat(LampGlow) < 0.05f,
            $"{LitLamps(village)} lamps lit, windows {Shader.GetGlobalFloat(LampGlow):0.00}");
        Shot("afternoon");

        // Dusk: dim, the lamps come on.
        int mark = ConsoleMark;
        yield return Switch(At(18.5f) + Weather);
        yield return Wait(5f);
        Check("dim_at_dusk", World.Daylight == LightLevel.Dim, $"{World.Daylight}");
        Check("lamps_on_at_dusk", LitLamps(village) > 0 && Shader.GetGlobalFloat(LampGlow) > 0.4f,
            $"{LitLamps(village)} of {DoorLamps(village).Length} lamps lit, windows {Shader.GetGlobalFloat(LampGlow):0.00}");
        Shot("dusk");

        // Night: dark, the stars and the fireflies out, the wizard told how to see.
        yield return Switch(At(21.5f) + Weather);
        yield return Wait(8f);
        Check("dark_at_night", World.IsNight, $"{World.Daylight}");
        float stars = Shader.GetGlobalFloat(StarBright);
        Metric("star_brightness", stars);
        Check("stars_out_at_night", stars > 1f, $"stars {stars:0.00}");
        float fireflies = Shader.GetGlobalVector(Critters).x;
        Metric("fireflies", fireflies);
        Check("fireflies_by_the_hour", fireflies > 0.5f, $"fireflies {fireflies:0.00} (butterflies {Shader.GetGlobalVector(Critters).y:0.00})");
        Check("dark_hint", ConsoleSaid("It's dark:", mark), "the log tells the wizard, out in the dark with no light, how to see");
        Shot("night");
        yield return Wait(0.3f);
        // Up at the sky over the village.
        Cut(square + V(-6f, 3f, -6f), square + V(20f, 40f, 20f));
        yield return Wait(1f);
        Shot("stars");
    }

    // The lamps Village lights at dusk: the inn's and shop's door lights and the well lamp.
    static int LitLamps(Village village) => DoorLamps(village).Count(l => l.enabled);

    static Light[] DoorLamps(Village village) => village.GetComponentsInChildren<Light>(true).Where(l => l.name == "Door light" || l.name == "Well lamp").ToArray();
}
