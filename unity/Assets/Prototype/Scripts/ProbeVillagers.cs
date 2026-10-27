using System.Collections;
using System.Linq;
using Chem;
using UnityEngine;

// The probe's villagers scenario (docs/FAST_LOOP.md): Hollowmere's folk going
// about their day (ValleyFauna.Folk). An afternoon, clear: the wizard stands by
// the square and the camera looks down on it. Checks the folk come out, walk
// about (between doors, the well and the benches) and sit, that they run from
// a house set on fire (a Fire Bolt), then at dusk that they all go indoors and
// stay in.
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
        // On the square, a few steps from the well (the wizard keeps out of the folk's way there).
        var stand = square + new Vector3(3f, 0f, -3f);
        Teleport(new Vector3(stand.x, grid.GroundAt(stand) + 0.1f, stand.z));
        var overview = ClearView(village, square);
        Cut(overview, square);
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
        if (fauna.FirstSitter(village, out var sitter, out float facing))
        {
            // A close look at one sitting, from in front of the bench and a little to the side.
            var front = new Vector3(Mathf.Sin(facing), 0f, Mathf.Cos(facing));
            Cut(sitter + front * 3f + Quaternion.Euler(0f, 90f, 0f) * front * 1.2f + Vector3.up * 1.4f, sitter + Vector3.up * 0.6f);
            yield return Wait(0.3f);
            Shot("sitting");
            yield return Wait(0.3f);
            Cut(overview, square);
        }

        // A house on fire (a Fire Bolt into it): the folk near it run from it.
        // The nearest house with walls that burn (timber or log), set alight on its street side.
        var house = village.GetComponentsInChildren<VillageHouse>().Where(h => h.walls != WallKind.Stone)
            .OrderBy(h => Vector3.Distance(h.transform.position, square)).FirstOrDefault();
        if (house != null)
        {
            var fire = house.transform.position - house.transform.forward * (house.depth / 2f - 0.3f);
            var was = fauna.FolkOf(village, out _);
            float before = MeanDistance(was, fire);
            int nearBefore = was.Count(p => Vector3.Distance(p, fire) < 16f);
            var cell = grid.CellAt(fire);
            puppet.Face(fire);
            caster.Cast(SpellIds.FireBolt, cell.x, cell.y, 0);
            yield return Wait(7f);
            Shot("fire");
            var near = fauna.FolkOf(village, out _);
            float after = MeanDistance(near, fire);
            Metric("folk_from_fire_before_m", before);
            Metric("folk_from_fire_after_m", after);
            // The brain runs from fire within 10 m: after 7 s nobody is right by the burning wall.
            int closeAfter = near.Count(p => Vector3.Distance(p, fire) < 6f);
            Metric("folk_near_fire_before", nearBefore);
            Check("folk_flee_fire", BurningCells() > 0 && closeAfter == 0,
                $"{BurningCells()} cells burning; {nearBefore} of the folk within 16 m before, {closeAfter} within 6 m after; {before:0.0} m from it on average before, {after:0.0} m after");
            PutOutFires();
            yield return Wait(3f);
        }

        // Dusk: home they go, and they stay in.
        yield return Switch(At(18.1f) + ", 'weather': { 'calmPeriods': 0, 'rampMinutes': 0.02, 'weights': " + Clear + " }");
        float went = 0f;
        for (; went < 45f && fauna.FolkOf(village, out _).Count > 0; went += 0.5f) yield return Wait(0.5f);
        Metric("go_in_seconds", went);
        Shot("dusk");
        yield return Wait(4f);
        int still = fauna.FolkOf(village, out _).Count;
        Check("folk_go_in_at_night", still == 0, still == 0 ? $"all in after {went:0} s" : $"{still} still out after {went + 4f:0} s");
    }

    const string Clear = "{ 'clear': 1, 'cloudy': 0, 'windy': 0, 'fog': 0, 'rain': 0, 'storm': 0, 'snow': 0 }";

    // A camera spot 12 m out from the square and 8 m up, on the side furthest from any house (so
    // no roof or chimney stands in the way).
    static Vector3 ClearView(Village village, Vector3 square)
    {
        var houses = village.GetComponentsInChildren<VillageHouse>().Select(h => h.transform.position).ToList();
        Vector3 best = square + new Vector3(-11f, 8f, -11f);
        float bestRoom = -1f;
        for (int a = 0; a < 360; a += 20)
        {
            var dir = Quaternion.Euler(0f, a, 0f) * Vector3.forward;
            var spot = square + dir * 12f + Vector3.up * 8f;
            float room = houses.Count == 0 ? 99f : houses.Min(h => Vector2.Distance(new Vector2(h.x, h.z), new Vector2(spot.x, spot.z)));
            if (room <= bestRoom) continue;
            bestRoom = room;
            best = spot;
        }
        return best;
    }

    static float MeanDistance(System.Collections.Generic.List<Vector3> at, Vector3 from) =>
        at.Count == 0 ? 0f : at.Average(p => Vector3.Distance(p, from));

    // How far the folk are on average from the nearest of where they were.
    static float Spread(System.Collections.Generic.List<Vector3> before, System.Collections.Generic.List<Vector3> after) =>
        after.Count == 0 || before.Count == 0 ? 0f : after.Average(q => before.Min(p => Vector3.Distance(p, q)));
}
