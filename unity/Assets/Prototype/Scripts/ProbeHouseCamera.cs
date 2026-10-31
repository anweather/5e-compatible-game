using System.Collections;
using System.Linq;
using Unity.Cinemachine;
using UnityEngine;

// The probe's housecam scenario (issue #20): the wizard walks up to Hollowmere's
// houses from all four sides with the follow camera behind it, as a player does.
// The camera's arm (its distance from the shoulder point) is sampled each frame,
// and at the shortest the arm's own check is repeated to name what it hit. The
// arm must not jump in with the wizard's nose to a wall and open ground behind.
public class HouseCameraProbe : ProbeScenario
{
    // The arm at or above this share of its length counts as not pulled in.
    const float KeepsShare = 0.75f;
    const float Pitch = 12f;
    static readonly Collider[] Near = new Collider[16];

    float shortest = float.MaxValue, full;
    int blocked;
    string what = "nothing";

    protected override IEnumerator Run()
    {
        yield return Switch(At(12f) + ", 'weather': { 'calmPeriods': 0, 'rampMinutes': 0.02, 'weights': " +
            "{ 'clear': 1, 'cloudy': 0, 'windy': 0, 'fog': 0, 'rain': 0, 'storm': 0, 'snow': 0 } }");
        var village = FindObjectsByType<Village>(FindObjectsSortMode.None).FirstOrDefault(v => v.villageName == "Hollowmere");
        Check("village_found", village != null);
        if (village == null) yield break;
        var follow = FindAnyObjectByType<CinemachineThirdPersonFollow>();
        var root = wizard.transform.Find(WizardRig.CameraRoot);
        var houses = village.GetComponentsInChildren<VillageHouse>().Take(4).ToArray();
        full = follow.CameraDistance;
        int runs = 0;
        foreach (var house in houses)
            foreach (var side in new[] { Vector3.forward, Vector3.right, Vector3.back, Vector3.left })
            {
                runs++;
                yield return WalkInto(house, side, follow, root);
            }
        Metric("runs", runs);
        Metric("walks_something_truly_behind", blocked);
        Metric("arm_full_m", full);
        Metric("arm_shortest_m", shortest);
        Check("arm_holds_at_a_house", shortest >= full * KeepsShare, $"shortest {shortest:F2} m of {full:F2} over {runs} walks; {what}");
        yield return Wait(0.2f);
        Shot("nose-to-the-wall");
        yield return SmallForm(houses, follow, root);
    }

    // The smallest of the animals' bodies at the same walls: its radius is under the camera's check (#20 again?).
    IEnumerator SmallForm(VillageHouse[] houses, CinemachineThirdPersonFollow follow, Transform root)
    {
        var shape = wizard.GetComponent<Shapeshift>();
        var fauna = FindAnyObjectByType<ValleyFauna>();
        var spell = World.Rules.Spell(SpellIds.Shapeshift);
        int small = -1;
        float least = float.MaxValue;
        for (int i = 0; fauna != null && fauna.SpeciesName(i) != null; i++)
        {
            var body = fauna.Body(i);
            if (body == null) continue;
            float size = body.StandHeight * (body.Size.x + body.Size.y);
            if (size < least) (least, small) = (size, i);
        }
        Check("small_form_found", small >= 0, fauna != null ? fauna.SpeciesName(small) : "no fauna");
        if (small < 0) yield break;
        var at = houses[0].transform.position + Vector3.right * (houses[0].width / 2f + 6f);
        Teleport(new Vector3(at.x, grid.GroundAt(at) + 0.1f, at.z));
        var there = at + Vector3.right * 3f;
        int animal = fauna.Spawn(small, new Unity.Mathematics.float3(there.x, grid.GroundAt(there), there.z), 0f);
        fauna.Puppet(animal, 0f, 0f);
        yield return null;
        var eye = wizard.transform.position + Vector3.up * 1.6f;
        var ray = new Ray(eye, (Vector3)fauna.State(animal).Position + Vector3.up * 0.2f - eye);
        string why = shape.Aim(spell, ray, out var cell);
        Check("small_form_aimed", why == null, why ?? fauna.SpeciesName(small));
        caster.Select(SpellIds.Shapeshift);
        caster.Cast(SpellIds.Shapeshift, cell.x, cell.y, 1);
        yield return Wait(1.2f);
        Check("small_form_taken", shape.Shifted, shape.FormName ?? "not shifted");
        Metric("small_body_radius_m", wizard.GetComponent<CharacterController>().radius);
        (shortest, what, blocked) = (float.MaxValue, "nothing", 0);
        int runs = 0;
        foreach (var house in houses.Take(2))
            foreach (var side in new[] { Vector3.forward, Vector3.right, Vector3.back, Vector3.left })
            {
                if (!shape.Shifted) break;
                runs++;
                yield return WalkInto(house, side, follow, root);
            }
        Metric("small_runs", runs);
        Metric("small_arm_shortest_m", shortest);
        Check("arm_holds_in_a_small_form", shortest >= full * KeepsShare, $"shortest {shortest:F2} m of {full:F2} over {runs} walks in the {shape.FormName}; {what}");
        Shot("small-form-at-the-wall");
        yield return null;
        yield return null;
        shape.ChangeBackByChoice();
    }

    // Into the wall from one side and held there, as a player does.
    IEnumerator WalkInto(VillageHouse house, Vector3 side, CinemachineThirdPersonFollow follow, Transform root)
    {
        var centre = house.transform.position;
        float half = Mathf.Abs(side.z) > 0.5f ? house.depth / 2f : house.width / 2f;
        var start = centre + house.transform.TransformDirection(side) * (half + 5.5f);
        start.y = grid.GroundAt(start) + 0.1f;
        var body = wizard.GetComponent<CharacterController>();
        body.enabled = false;
        wizard.transform.position = start;
        body.enabled = true;
        for (float t = 0f; t < 0.3f; t += Time.deltaTime)
        {
            puppet.AimCamera(centre, Pitch, 1000f);
            yield return null;
        }
        var dir = (centre - start).normalized;
        float walkShortest = float.MaxValue;
        string walkHit = "";
        bool truly = false;
        for (float t = 0f; t < 3f; t += Time.deltaTime)
        {
            puppet.AimCamera(centre, Pitch, 1000f);
            var cam = Camera.main.transform;
            var flat = Vector3.ProjectOnPlane(cam.forward, Vector3.up).normalized;
            puppet.Move(new Vector2(Vector3.Dot(dir, cam.right), Vector3.Dot(dir, flat)).normalized);
            yield return null;
            float arm = Vector3.Distance(cam.position, root.position);
            if (t < 1f || arm >= walkShortest) continue;
            walkShortest = arm;
            walkHit = $"{house.name} side {side}: {ArmHit(follow, root, out var behind)}";
            truly |= behind != null && !behind.transform.IsChildOf(house.transform);
        }
        puppet.Move(Vector2.zero);
        // Another house or a tree truly behind the wizard is the arm doing its job: not this one's fault.
        if (truly) blocked++;
        else if (walkShortest < shortest) (shortest, what) = (walkShortest, walkHit);
    }

    // Repeats the follow's check: the shoulder out from the root, the arm back from there.
    static string ArmHit(CinemachineThirdPersonFollow follow, Transform root, out Collider behind)
    {
        behind = null;
        var avoid = follow.AvoidObstacles;
        float radius = avoid.CameraRadius;
        var rot = root.rotation;
        var offset = follow.ShoulderOffset;
        offset.x *= follow.CameraSide * 2f - 1f;
        var hand = root.position + rot * offset;
        var tip = hand - rot * Vector3.forward * follow.CameraDistance;
        string along = Cast(root.position, hand, radius * 1.01f, avoid, out _);
        return $"radius {radius}, root-to-hand {along}, hand-to-tip {Cast(hand, tip, radius, avoid, out behind)}";
    }

    static string Cast(Vector3 from, Vector3 to, float radius, CinemachineThirdPersonFollow.ObstacleSettings avoid, out Collider first)
    {
        first = null;
        var dir = to - from;
        int near = Physics.OverlapSphereNonAlloc(from, radius, Near, avoid.CollisionFilter, QueryTriggerInteraction.Ignore);
        for (int i = 0; i < near; i++)
            if (!Near[i].CompareTag(avoid.IgnoreTag))
            {
                first = Near[i];
                return $"starts inside {first.name} ({first.GetType().Name})";
            }
        if (!Physics.SphereCast(from, radius, dir.normalized, out var hit, dir.magnitude, avoid.CollisionFilter, QueryTriggerInteraction.Ignore)) return "clear";
        first = hit.collider;
        return $"{first.name} at {hit.distance:F2}";
    }
}
