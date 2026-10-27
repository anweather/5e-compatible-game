using System.Collections;
using System.Linq;
using UnityEngine;

// The probe's villagers scenario (docs/FAST_LOOP.md): Hollowmere's folk going
// about their day (ValleyFauna.Folk). An afternoon, clear: the wizard stands by
// the square and the camera looks down on it. Checks the folk come out, walk
// about (between doors, the well and the benches) and sit, then at dusk that
// they all go indoors and stay in.
public class VillagersProbe : ProbeScenario
{
    protected override IEnumerator Run()
    {
        var village = FindObjectsByType<Village>(FindObjectsSortMode.None).FirstOrDefault(v => v.villageName == "Hollowmere");
        var fauna = FindAnyObjectByType<ValleyFauna>();
        Check("village_found", village != null && fauna != null, village != null ? "Hollowmere" : "no Hollowmere (or no ValleyFauna)");
        if (village == null || fauna == null) yield break;
        yield return Switch(At(15f) + ", 'weather': { 'calmPeriods': 0, 'rampMinutes': 0.02, 'weights': " + Clear + " }");

        var square = village.transform.position + village.wellAt;
        var stand = square + new Vector3(10f, 0f, -10f);
        Teleport(new Vector3(stand.x, grid.GroundAt(stand) + 0.1f, stand.z));
        Cut(square + V(-16f, 14f, -16f), square);
        yield return Wait(6f);
        var first = fauna.FolkOf(village, out _);
        Metric("folk_out", first.Count);
        Check("folk_come_out", first.Count >= village.residentCount / 2, $"{first.Count} of {village.residentCount} out");
        Shot("afternoon");

        // Walking about: most of them a few metres from where they were.
        yield return Wait(12f);
        var later = fauna.FolkOf(village, out int sitting);
        Check("folk_walk_about", later.Count > 0 && Spread(first, later) > 2f, $"they spread {Spread(first, later):0.0} m on average");

        // Sooner or later someone sits on a bench.
        int mostSitting = sitting;
        for (float t = 0f; t < 40f && mostSitting == 0; t += 1f)
        {
            yield return Wait(1f);
            fauna.FolkOf(village, out sitting);
            mostSitting = Mathf.Max(mostSitting, sitting);
        }
        Metric("folk_sitting", mostSitting);
        Check("folk_sit", mostSitting > 0, $"{mostSitting} sitting at most");
        Shot("benches");

        // Dusk: home they go, and they stay in.
        yield return Switch(At(19.6f) + ", 'weather': { 'calmPeriods': 0, 'rampMinutes': 0.02, 'weights': " + Clear + " }");
        float went = 0f;
        for (; went < 45f && fauna.FolkOf(village, out _).Count > 0; went += 0.5f) yield return Wait(0.5f);
        Metric("go_in_seconds", went);
        Shot("dusk");
        yield return Wait(4f);
        int still = fauna.FolkOf(village, out _).Count;
        Check("folk_go_in_at_night", still == 0, still == 0 ? $"all in after {went:0} s" : $"{still} still out after {went + 4f:0} s");
    }

    const string Clear = "{ 'clear': 1, 'cloudy': 0, 'windy': 0, 'fog': 0, 'rain': 0, 'storm': 0, 'snow': 0 }";

    // How far the folk are on average from the nearest of where they were.
    static float Spread(System.Collections.Generic.List<Vector3> before, System.Collections.Generic.List<Vector3> after) =>
        after.Count == 0 || before.Count == 0 ? 0f : after.Average(q => before.Min(p => Vector3.Distance(p, q)));
}
