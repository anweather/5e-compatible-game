using System.Collections;
using System.Linq;
using UnityEngine;

// The probe's papercuts scenario (docs/FAST_LOOP.md): Andrew's playtest paper
// cuts of 2026-10-27 (playtest/andrew-2026-10-27.md), each framed for a picture
// and checked where a check can say it: the signs' words (front, and nothing
// mirrored from behind), the villagers' faces and how they sit, a house's roof,
// door, shutters and window boxes, the well's wall, no wild animals on the
// beach, a burnt-down inn's door light, and the island's palms and south pier.
// It finds everything by name and component, so it runs on a build with or
// without the fixes: the before and after pictures come from the same script.
public class PaperCutsProbe : ProbeScenario
{
    protected override IEnumerator Run()
    {
        yield return Switch(At(15f) + ", 'weather': { 'calmPeriods': 0, 'rampMinutes': 0.02, 'weights': " + Clear + " }");
        yield return Signs();
        var village = FindObjectsByType<Village>(FindObjectsSortMode.None).FirstOrDefault(v => v.villageName == "Hollowmere");
        var fauna = FindAnyObjectByType<ValleyFauna>();
        if (village != null && fauna != null)
        {
            yield return Folk(village, fauna);
            yield return House(village);
            yield return Well(village);
        }
        Coast(fauna);
        yield return Island();
        if (village != null) yield return BurntLights(village);
    }

    const string Clear = "{ 'clear': 1, 'cloudy': 0, 'windy': 0, 'fog': 0, 'rain': 0, 'storm': 0, 'snow': 0 }";

    // #1: each sign's words from the front and from behind, and whether they're drawn with the
    // front-only, depth-tested SignText.
    IEnumerator Signs()
    {
        foreach (var name in new[] { "Defense sign words", "Defense banner words", "Tower sign words" })
        {
            var words = GameObject.Find(name);
            if (words == null) continue;
            var r = words.GetComponent<MeshRenderer>();
            string label = name.Replace(" words", "").Replace(' ', '-').ToLowerInvariant();
            Check(label + "_one_sided", r != null && r.sharedMaterial != null && r.sharedMaterial.shader.name == "Prototype/SignText",
                r != null && r.sharedMaterial != null ? r.sharedMaterial.shader.name : "no renderer");
            var at = words.transform.position;
            var forward = words.transform.forward;
            Teleport(at - forward * 6f + Vector3.up * 0.1f);
            Cut(at - forward * 3f + Vector3.up * 0.3f, at);
            yield return Wait(0.5f);
            Shot(label + "-front");
            Cut(at + forward * 3f + Vector3.up * 0.3f, at);
            yield return Wait(0.5f);
            Shot(label + "-back");
        }
    }

    // #2 and #3: a villager's face close up, and one sitting on a bench (where, which way).
    IEnumerator Folk(Village village, ValleyFauna fauna)
    {
        var square = village.transform.position + village.wellAt;
        Teleport(square + new Vector3(3f, 0.1f, -3f));
        yield return Wait(8f);
        var folk = fauna.FolkOf(village, out _);
        Metric("folk_out", folk.Count);
        if (folk.Count > 0)
        {
            var p = folk[0];
            var eye = p + Vector3.up * 1.55f;
            Cut(eye + (square - p).normalized * 1.4f + Vector3.up * 0.1f, eye);
            yield return Wait(0.3f);
            Shot("villager-face");
            Cut(eye + Quaternion.Euler(0f, 120f, 0f) * (square - p).normalized * 1.4f, eye);
            yield return Wait(0.3f);
            Shot("villager-face-2");
        }
        for (float t = 0f; t < 45f && !fauna.FirstSitter(village, out _, out _); t += 1f) yield return Wait(1f);
        if (fauna.FirstSitter(village, out var sitter, out float facing))
        {
            var front = new Vector3(Mathf.Sin(facing), 0f, Mathf.Cos(facing));
            var seat = Seat.Nearest(sitter, 3f);
            float off = seat != null ? Vector2.Distance(new Vector2(seat.Foot.x, seat.Foot.z), new Vector2(sitter.x, sitter.z)) : -1f;
            Metric("sitter_from_seat_m", off);
            Check("sits_on_the_seat", seat != null && off < 0.25f, $"{off:0.00} m from the seat's foot");
            Cut(sitter + front * 2.6f + Quaternion.Euler(0f, 90f, 0f) * front * 1.4f + Vector3.up * 1.3f, sitter + Vector3.up * 0.6f);
            yield return Wait(0.3f);
            Shot("sitting");
            Cut(sitter + Quaternion.Euler(0f, 90f, 0f) * front * 3f + Vector3.up * 1f, sitter + Vector3.up * 0.6f);
            yield return Wait(0.3f);
            Shot("sitting-side");
        }
        else Check("sits_on_the_seat", false, "no one sat within 45 s");
    }

    // #4: the house nearest the square: its roof solid, its window boxes solid, and a look at its front.
    IEnumerator House(Village village)
    {
        var square = village.transform.position + village.wellAt;
        var house = village.GetComponentsInChildren<VillageHouse>().OrderBy(h => (h.transform.position - square).sqrMagnitude).FirstOrDefault();
        if (house == null) yield break;
        var roof = house.GetComponentsInChildren<MeshRenderer>().FirstOrDefault(r => r.name == "Roof");
        bool roofSolid = roof != null && roof.GetComponent<MeshCollider>() != null;
        Check("roof_solid", roofSolid, roofSolid ? "the roof has a collider" : "no collider on the roof");
        int solids = house.GetComponentsInChildren<BoxCollider>().Count(b => b.name == "Solid");
        Metric("house_solid_extras", solids);
        // Dropped from above the roof's slope, half a metre in from the eaves, does it land on the roof?
        var t = house.transform;
        var box = house.GetComponent<BoxCollider>();
        if (box != null && roof != null)
        {
            var probe = t.TransformPoint(new Vector3(0f, 0f, box.size.z / 2f - 0.5f)) + Vector3.up * 30f;
            bool hit = Physics.Raycast(probe, Vector3.down, out var h, 40f, ~0, QueryTriggerInteraction.Ignore);
            Check("lands_on_the_roof", hit && h.collider.name == "Roof", hit ? $"hit {h.collider.name} at {h.point.y - t.position.y:0.0} m" : "hit nothing");
        }
        var front = -t.forward;
        Teleport(t.position + front * 9f + Vector3.up * 0.1f);
        Cut(t.position + front * 7f + Vector3.up * 2.2f, t.position + Vector3.up * 1.6f);
        yield return Wait(0.5f);
        Shot("house-front");
        Cut(t.position + front * 3.2f + t.right * 1.5f + Vector3.up * 1.8f, t.position + front * 0.2f + Vector3.up * 1.4f);
        yield return Wait(0.3f);
        Shot("house-close");
    }

    // #5: walking at the well from beside it, does the wall stop you?
    IEnumerator Well(Village village)
    {
        var well = village.transform.position + village.wellAt;
        bool hit = Physics.Raycast(well + new Vector3(2.5f, 0.5f, 0f), Vector3.left, out var h, 2.4f, ~0, QueryTriggerInteraction.Ignore);
        Check("well_wall_stops_you", hit && h.distance < 2.2f, hit ? $"stopped by {h.collider.name} {h.distance:0.0} m in" : "walked straight through");
        Cut(well + new Vector3(3f, 2f, -3f), well + Vector3.up * 0.6f);
        yield return Wait(0.3f);
        Shot("well");
    }

    // #8: is a point on the beach somewhere a wild animal may walk?
    void Coast(ValleyFauna fauna)
    {
        var ocean = Ocean.Instance;
        if (ocean == null || fauna == null) return;
        var beach = ocean.shoreOrigin - ocean.seaward.normalized * 10f;
        bool walkable = fauna.Walkable(new Unity.Mathematics.float2(beach.x, beach.y));
        Check("no_deer_on_the_beach", !walkable, walkable ? "the beach is walkable for wild animals" : "the beach is off-limits to them");
    }

    // #14: the island's palms and, if there is one, its south pier.
    IEnumerator Island()
    {
        var pier = GameObject.Find("South pier");
        Check("south_pier", pier != null, pier != null ? "built" : "none");
        var dock = GameObject.Find("Dock");
        var at = pier != null ? pier.transform.position : dock != null ? dock.transform.position : Vector3.zero;
        if (at == Vector3.zero) yield break;
        Teleport(at + Vector3.up * 0.5f);
        Cut(at + new Vector3(-14f, 9f, -14f), at + Vector3.up * 2f);
        yield return Wait(1f);
        Shot("island");
    }

    // #10c: at dusk, an inn whose roof is gone (as a burnt-down one's is) keeps its door light out.
    IEnumerator BurntLights(Village village)
    {
        var inn = village.GetComponentsInChildren<VillageHouse>().FirstOrDefault(h => h.GetComponentsInChildren<Light>(true).Any(l => l.name == "Door light"));
        if (inn == null) yield break;
        yield return Switch(At(21f));
        var roof = inn.GetComponentsInChildren<MeshRenderer>().FirstOrDefault(r => r.name == "Roof");
        var lamp = inn.GetComponentsInChildren<Light>(true).First(l => l.name == "Door light");
        yield return Wait(1f);
        bool litStanding = lamp.enabled;
        if (roof != null) roof.enabled = false;
        foreach (var l in inn.GetComponentsInChildren<Light>(true)) l.enabled = false; // as ValleyChemistry does when it burns down
        yield return Wait(1f);
        Check("burnt_inn_dark", litStanding && !lamp.enabled, $"lit while standing {litStanding}, lit once burnt {lamp.enabled}");
        if (roof != null) roof.enabled = true;
    }
}
