using System.Collections;
using System.Linq;
using Chem;
using Unity.Mathematics;
using UnityEngine;

// The probe's closeup scenario (issue #47): a deer, a valley cabin and a village
// log house at a clear noon, each from the same cameras, close enough to see
// how they are made. `--shots` saves them.
public class CloseupProbe : ProbeScenario
{
    // How far up the follow camera looks (degrees), and how far ahead of the lens the deer stands (m).
    const float LookUp = -17f, DeerOffLens = 0.05f;

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
        int deer = fauna.Spawn(fauna.SpeciesIndex("Deer"), new float3(at.x, grid.GroundAt(at), at.z), yaw);
        fauna.Puppet(deer, yaw, 0f);
        for (float t = 0f; t < 0.5f; t += Time.deltaTime)
        {
            puppet.AimCamera(wizard.transform.position + Vector3.forward * 5f, LookUp, 20f);
            yield return null;
        }
        Metric("lens_moved_m", Vector3.Distance(lens, cam.position));
        Shot("follow-deer");
        yield return null;
        yield return null;
    }

    IEnumerator Deer()
    {
        var fauna = FindAnyObjectByType<ValleyFauna>();
        Check("fauna_built", fauna != null);
        if (fauna == null) yield break;
        var p = V(27f, 0f, 26f);
        p.y = grid.GroundAt(p);
        int deer = fauna.Spawn(fauna.SpeciesIndex("Deer"), new float3(p.x, p.y, p.z), 0f);
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
        yield return Look("cabin-wall", P(1.5f, 1.4f, -3.4f), P(1.5f, 1.4f, -2.25f));
        yield return Look("cabin-corner", P(4.6f, 1.5f, -3.6f), P(3f, 1.4f, -2.25f));
        yield return Look("cabin-along", P(3.7f, 1.3f, -3.3f), P(-3f, 1.4f, -2.25f));
        yield return Look("cabin-eaves", P(1f, 1.2f, -3.2f), P(1f, 3.1f, -2.25f));
        yield return Look("cabin-gable", P(5.2f, 3f, 0.4f), P(3f, 3.6f, 0f));
        yield return Look("cabin-gable-ground", P(6.6f, 1.7f, 0.6f), P(3f, 3.7f, 0f));
        yield return Look("cabin-end", P(4.6f, 1.2f, 0.2f), P(3f, 1.4f, 0f));
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
