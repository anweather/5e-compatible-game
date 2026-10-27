using System.Collections;
using UnityEngine;

// The probe's critters scenario (docs/FAST_LOOP.md): the ambient fireflies and
// butterflies (AmbientCritters, from Punchy). A clear night over the meadow
// toward the pond, then a clear noon close to the grass, each with a
// screenshot. Checks that the critters have homes near the camera and come
// out at the right time of day, and that the other kind stays in.
public class CrittersProbe : ProbeScenario
{
    protected override IEnumerator Run()
    {
        var critters = FindFirstObjectByType<AmbientCritters>();
        Check("critters_built", critters != null && critters.fireflyMaterial != null && critters.butterflyMaterial != null,
            "AmbientCritters on the Sky object with both materials");
        if (critters == null) yield break;
        string clear = ", 'weather': { 'calmPeriods': 0, 'rampMinutes': 0.02, 'weights': { 'clear': 1, 'cloudy': 0, 'windy': 0, 'fog': 0, 'rain': 0, 'storm': 0 } }";

        Cut(V(40f, 6f, 1f), V(28f, 0f, 15f));
        yield return Switch(At(22f) + clear);
        yield return Wait(10f);
        var (fireflies, _) = critters.Homes();
        Metric("night_fireflies_out", critters.Shown.x);
        Metric("firefly_homes", fireflies);
        Check("fireflies_at_night", critters.Shown.x > 0.6f && critters.Shown.y < 0.05f && fireflies > 50,
            $"fireflies out {critters.Shown.x:0.00}, butterflies {critters.Shown.y:0.00}, {fireflies} firefly homes near");
        Cut(V(40f, 6f, 1f), V(28f, 0f, 15f));
        yield return null;
        Shot("fireflies");

        yield return Switch(At(12f) + clear);
        yield return Wait(12f);
        var (_, butterflies) = critters.Homes();
        Metric("noon_butterflies_out", critters.Shown.y);
        Metric("butterfly_homes", butterflies);
        Check("butterflies_by_day", critters.Shown.y > 0.6f && critters.Shown.x < 0.05f && butterflies > 50,
            $"butterflies out {critters.Shown.y:0.00}, fireflies {critters.Shown.x:0.00}, {butterflies} butterfly homes near");
        // Butterflies are a hand's width across: from inside the meadow, at knee height.
        Cut(V(27f, 2.2f, 20f), V(27f, 0.4f, 25f));
        yield return null;
        Shot("butterflies");
    }
}
