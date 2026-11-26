using System.Collections;
using Chem;
using UnityEngine;

// The probe's spawnhops scenario, on the nine (unity/Temp/map = nine): N through every spawn point of the map and Shift+N
// back (PlaytestTools.Hop), each time waiting for the hop to land, then reading where the wizard stands against the ground.
public class SpawnHopsProbe : ProbeScenario
{
    protected override IEnumerator Run()
    {
        var tools = PlaytestTools.Instance;
        var spawns = SpawnPoints.Current;
        Check("several_spawns", tools != null && spawns.Count > 1, $"{spawns.Count} spawn points");
        if (tools == null || spawns.Count < 2) yield break;
        var body = wizard.GetComponent<CharacterController>();
        bool allStood = true;
        for (int i = 0; i < spawns.Count; i++)
        {
            tools.Hop(1);
            yield return Landed(tools);
            allStood &= Stood($"hop_{i + 1}_{spawns[(PlaytestTools.NearestSpawn(spawns, grid.CellAt(wizard.transform.position)))].Name}", body);
        }
        Check("every_hop_stood_on_ground", allStood, $"{spawns.Count} hops, each within 2 m above the ground");
        var here = wizard.transform.position;
        tools.Hop(-1);
        yield return Landed(tools);
        Check("shift_n_goes_back", (wizard.transform.position - here).sqrMagnitude > 1f, $"{here} to {wizard.transform.position}");
        Stood("back_one", body);
    }

    // Waits out the hop's landing, then a second for the controller to settle on the ground.
    IEnumerator Landed(PlaytestTools tools)
    {
        yield return null;
        for (float t = 0f; tools.Landing && t < 8f; t += Time.deltaTime) yield return null;
        for (float t = 0f; t < 1f; t += Time.deltaTime) yield return null;
    }

    bool Stood(string name, CharacterController body)
    {
        var p = wizard.transform.position;
        var cell = grid.CellAt(p);
        float above = p.y - grid.GroundAt(p);
        Metric(name + "_cell_x", cell.x);
        Metric(name + "_cell_y", cell.y);
        Metric(name + "_above_ground_m", above);
        bool ok = body != null && body.enabled && above > -0.5f && above < 2f;
        if (!ok) Check(name, false, $"at {p} cell {cell.x},{cell.y}: {above:0.0} m above the ground, body {(body != null && body.enabled)}");
        return ok;
    }
}
