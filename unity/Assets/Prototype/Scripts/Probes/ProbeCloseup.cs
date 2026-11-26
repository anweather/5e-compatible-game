using System.Collections;
using System.Linq;
using Chem;
using Unity.Mathematics;
using UnityEngine;

// The probe's closeup scenario (issue #47): a deer, a valley cabin and a village
// log house at a clear noon, each from the same cameras, close enough to see
// how they are made (`--shots`), and a deer across the game's own follow camera.
// Checks what you shouldn't see: the limb ends buried in a deer with the lens in
// it, into a cabin through its gable, and a mesh's far inside.
public class CloseupProbe : ProbeScenario
{
    // How far up the follow camera looks (degrees), and how far ahead of the lens the deer stands (m).
    const float LookUp = -17f, DeerOffLens = 0.25f;

    protected override IEnumerator Run()
    {
        yield return Switch(At(12f) + ", 'weather': { 'calmPeriods': 0, 'rampMinutes': 0.02, 'weights': " +
            "{ 'clear': 1, 'cloudy': 0, 'windy': 0, 'fog': 0, 'rain': 0, 'storm': 0, 'snow': 0 } }");
        yield return Wait(1f);
        yield return Follow();
        yield return Deer();
        yield return Cabin();
        yield return LogHouse();
        InsideOut();
    }

    // Every readable mesh in the scene: its triangles' winding against its normals, turned scales allowed for.
    void InsideOut()
    {
        var seen = new System.Collections.Generic.HashSet<int>();
        var wrong = new System.Collections.Generic.List<string>();
        int checkedMeshes = 0;
        // FindObjectsByType leaves out what's marked DontSave: the meshes built when a scene loads, the ones that matter here.
        foreach (var filter in Resources.FindObjectsOfTypeAll<MeshFilter>())
        {
            if (!filter.gameObject.scene.IsValid()) continue;
            var mesh = filter.sharedMesh;
            if (mesh == null || !mesh.isReadable) continue;
            // Glass is drawn from both sides, so its winding shows nothing.
            if (filter.TryGetComponent<Renderer>(out var drawn) && drawn.sharedMaterial != null && drawn.sharedMaterial.shader.name == "Prototype/WindowGlass") continue;
            var s = filter.transform.lossyScale;
            bool mirrored = s.x * s.y * s.z < 0f;
            if (!seen.Add(mesh.GetInstanceID() * (mirrored ? -1 : 1))) continue;
            checkedMeshes++;
            float share = MeshChecks.BackwardShare(mesh);
            if (mirrored) share = 1f - share;
            if (share > 0.02f) wrong.Add($"{filter.name}/{mesh.name} {share:P0}");
        }
        Metric("meshes_checked", checkedMeshes);
        Check("no_inside_out_meshes", wrong.Count == 0, wrong.Count == 0 ? $"{checkedMeshes} meshes" : string.Join(", ", wrong.Take(30)));
    }

    IEnumerator Look(string name, Vector3 from, Vector3 at)
    {
        Cut(from, at);
        yield return Wait(0.25f);
        Shot(name);
        yield return null;
        yield return null;
    }

    // The game's own follow camera, nothing cut: looking up brings the lens down to a deer's height, and
    // a deer stands across the view with its back just short of the lens.
    IEnumerator Follow()
    {
        var fauna = FindAnyObjectByType<ValleyFauna>();
        if (fauna == null) yield break;
        var from = V(27f, 0f, 20f);
        Teleport(V(from.x, grid.GroundAt(from), from.z));
        var puppet = new WizardPuppet(wizard);
        var cam = Camera.main.transform;
        for (float t = 0f; t < 2f; t += Time.deltaTime)
        {
            puppet.AimCamera(wizard.transform.position + Vector3.forward * 5f, LookUp, 20f);
            yield return null;
        }
        var lens = cam.position;
        Metric("lens_over_ground_m", lens.y - grid.GroundAt(lens));
        Metric("lens_from_wizard_m", Vector3.Distance(lens, wizard.transform.position));
        Shot("follow-empty");
        yield return null;
        yield return null;
        var ahead = Vector3.ProjectOnPlane(cam.forward, Vector3.up).normalized;
        var at = lens + ahead * DeerOffLens;
        float yaw = math.atan2(cam.right.x, cam.right.z);
        int deer = fauna.Spawn(fauna.SpeciesIndex("deer"), new float3(at.x, grid.GroundAt(at), at.z), yaw);
        fauna.Puppet(deer, yaw, 0f);
        for (float t = 0f; t < 0.5f; t += Time.deltaTime)
        {
            puppet.AimCamera(wizard.transform.position + Vector3.forward * 5f, LookUp, 20f);
            yield return null;
        }
        Metric("lens_moved_m", Vector3.Distance(lens, cam.position));
        Check("deer_at_lens_shadow_only", fauna.AtLens >= 1, $"{fauna.AtLens} animals at the lens");
        Shot("follow-deer");
        yield return null;
        yield return null;
        fauna.Puppet(deer, yaw, 4f);
        yield return Wait(2f);
        Check("deer_drawn_again_past_the_lens", fauna.AtLens == 0, $"{fauna.AtLens} animals at the lens once it's walked on");
    }

    // The share of the picture in a deer's tan, as drawn.
    static IEnumerator DeerInPicture(System.Action<float> got)
    {
        yield return new WaitForEndOfFrame();
        var shown = ScreenCapture.CaptureScreenshotAsTexture();
        var pixels = shown.GetPixels32();
        Destroy(shown);
        got(pixels.Count(p => p.r > 120 && p.r > p.g + 25 && p.g > p.b + 25) / (float)pixels.Length);
    }

    IEnumerator Deer()
    {
        var fauna = FindAnyObjectByType<ValleyFauna>();
        Check("fauna_built", fauna != null);
        if (fauna == null) yield break;
        var p = V(27f, 0f, 26f);
        p.y = grid.GroundAt(p);
        int deer = fauna.Spawn(fauna.SpeciesIndex("deer"), new float3(p.x, p.y, p.z), 0f);
        fauna.Puppet(deer, 0f, 0f);
        yield return Wait(0.5f);
        var body = p + Vector3.up * 0.85f;
        yield return Look("deer-side", body + V(1.6f, 0.1f, 0f), body);
        yield return Look("deer-quarter", body + V(1.1f, 0.3f, 1.3f), body + V(0f, 0.1f, 0.3f));
        yield return Look("deer-front", body + V(0f, 0.2f, 1.7f), body + V(0f, 0.1f, 0.5f));
        yield return Look("deer-rear", body + V(0.3f, 0.3f, -1.5f), body + V(0f, -0.1f, -0.3f));
        yield return Look("deer-low", p + V(0.9f, 0.25f, -0.2f), body + V(0f, 0f, 0f));
        yield return Look("deer-above", body + V(0.5f, 1.2f, 0f), body);
        yield return Look("deer-touching", body + V(0.55f, 0.05f, 0.1f), body + V(0f, -0.1f, 0.1f));
        Metric("near_clip_m", Camera.main.nearClipPlane);
        Metric("field_of_view", Camera.main.fieldOfView);
        yield return Look("deer-nearcut", body + V(0.3f, 0.05f, 0.1f), body + V(0f, -0.1f, 0.1f));
        float tan = 1f;
        yield return DeerInPicture(share => tan = share);
        Metric("deer_in_picture_at_lens", tan);
        Check("no_limb_ends_at_lens", tan < 0.005f, $"{tan:P1} of the picture is the deer's coat, with the near plane in its side");
        yield return Look("deer-shadow", body + V(0.1f, 0.05f, 0f), p + V(0.5f, 0f, -0.5f));
        yield return Look("deer-incide", body + V(0.1f, 0.05f, -0.2f), body + V(-0.1f, -0.1f, 0.6f));
        yield return Look("deer-inside-back", body + V(0.1f, 0.05f, 0.2f), body + V(-0.1f, -0.2f, -0.6f));
        yield return Look("deer-incut-tail", body + V(0.2f, 0.05f, -0.75f), body + V(0f, 0f, 0.2f));
    }

    IEnumerator Cabin()
    {
        var cabin = GameObject.Find("Cabin");
        Check("cabin_found", cabin != null);
        if (cabin == null) yield break;
        var t = cabin.transform;
        Vector3 P(float x, float y, float z) => t.TransformPoint(x, y, z);
        Teleport(P(0f, 0f, -6f));
        yield return Wait(0.2f);
        GableClosed(t);
        yield return Look("cabin-wall", P(1.5f, 1.4f, -3.4f), P(1.5f, 1.4f, -2.25f));
        yield return Look("cabin-corner", P(4.6f, 1.5f, -3.6f), P(3f, 1.4f, -2.25f));
        yield return Look("cabin-along", P(3.7f, 1.3f, -3.3f), P(-3f, 1.4f, -2.25f));
        yield return Look("cabin-eaves", P(1f, 1.2f, -3.2f), P(1f, 3.1f, -2.25f));
        yield return Look("cabin-gable", P(5.2f, 3f, 0.4f), P(3f, 3.6f, 0f));
        yield return Look("cabin-gable-ground", P(6.6f, 1.7f, 0.6f), P(3f, 3.7f, 0f));
        yield return Look("cabin-end", P(4.6f, 1.2f, 0.2f), P(3f, 1.4f, 0f));
    }

    // A cabin's width, depth and rise under its ridge, as ValleyBuilder builds it.
    const float CabinWidth = 6f, CabinLength = 4.5f, CabinWallTop = 2.88f, CabinRise = 1.51f;

    // Lines across the gable end, on a grid under the roof: each must meet something of the cabin's, or it
    // looks straight in. The parts are boxes and cylinders, so their mesh bounds do.
    void GableClosed(Transform cabin)
    {
        var parts = cabin.GetComponentsInChildren<MeshFilter>().Where(f => f.sharedMesh != null && !f.name.StartsWith("Roof", System.StringComparison.Ordinal)).ToList();
        int lines = 0, open = 0;
        for (float y = CabinWallTop + 0.2f; y < CabinWallTop + CabinRise - 0.1f; y += 0.1f)
        {
            float half = (CabinLength / 2f) * (1f - (y - CabinWallTop) / CabinRise) - 0.1f;
            for (float z = -half; z <= half; z += 0.1f)
            {
                lines++;
                bool met = false;
                for (float x = CabinWidth / 2f - 0.2f; x <= CabinWidth / 2f + 0.2f && !met; x += 0.02f)
                {
                    var at = cabin.TransformPoint(x, y, z);
                    met = parts.Any(f => f.sharedMesh.bounds.Contains(f.transform.InverseTransformPoint(at)));
                }
                if (!met) open++;
            }
        }
        Metric("cabin_gable_lines", lines);
        Metric("cabin_gable_open", open);
        Check("cabin_gable_closed", lines > 100 && open == 0, $"{open} of {lines} lines across the gable look straight in");
    }

    IEnumerator LogHouse()
    {
        var house = FindObjectsByType<VillageHouse>(FindObjectsSortMode.None).Where(h => h.walls == WallKind.Log)
            .OrderBy(h => h.name, System.StringComparer.Ordinal).FirstOrDefault();
        Check("log_house_found", house != null);
        if (house == null) yield break;
        var t = house.transform;
        float ex = house.width / 2f, ez = house.depth / 2f;
        Vector3 P(float x, float y, float z) => t.TransformPoint(x, y, z);
        Teleport(P(0f, 0f, -ez - 4f));
        yield return Wait(0.2f);
        yield return Look("house-wall", P(ex * 0.5f, 1.5f, -ez - 1.3f), P(ex * 0.5f, 1.4f, -ez));
        yield return Look("house-corner", P(ex + 1.5f, 1.6f, -ez - 1.5f), P(ex, 1.4f, -ez));
        yield return Look("house-along", P(ex + 0.6f, 1.4f, -ez - 0.4f), P(-ex, 1.5f, -ez));
    }
}
