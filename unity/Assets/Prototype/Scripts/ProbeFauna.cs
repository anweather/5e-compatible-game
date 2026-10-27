using System.Collections;
using System.Linq;
using Unity.Mathematics;
using UnityEngine;

// The probe's fauna scenario (docs/FAST_LOOP.md): the valley's animals
// (ValleyFauna, from Punchy's creatures). Waits for them to come out round
// the camera and take up their activities (grazing, resting, drinking...),
// puts a deer close to the wizard to see it bolt, then lines up a deer, a
// rabbit, a turkey and a fox walking through the meadow for a screenshot.
public class FaunaProbe : ProbeScenario
{
    protected override IEnumerator Run()
    {
        var fauna = FindFirstObjectByType<ValleyFauna>();
        Check("fauna_built", fauna != null && fauna.material != null && fauna.species.Count >= 10,
            $"ValleyFauna with {(fauna != null ? fauna.species.Count : 0)} species");
        if (fauna == null) yield break;

        yield return Wait(10f);
        Metric("animals_out", fauna.Count);
        Check("animals_out", fauna.Count >= 10, $"{fauna.Count} animals round the camera");

        // Their days: after a while, they're up to several different things (grazing, resting, drinking...).
        yield return Wait(20f);
        var doing = Enumerable.Range(0, fauna.Count).Select(k => fauna.State(k).Mode).GroupBy(m => m)
            .OrderBy(g => g.Key).Select(g => $"{g.Key} {g.Count()}").ToList();
        int kinds = doing.Count(d => !d.StartsWith("Idle", System.StringComparison.Ordinal) && !d.StartsWith("Wander", System.StringComparison.Ordinal));
        Metric("activities", kinds);
        Check("activities", kinds >= 2, string.Join(", ", doing));

        // A deer eight metres north of the wizard (open meadow), facing it: deer run from anyone within 18 m.
        var w = wizard.transform.position;
        int deer = fauna.SpeciesIndex("Deer");
        var start = new float3(w.x, w.y, w.z + 8f);
        int i = fauna.Spawn(deer, start, math.PI);
        yield return Wait(3f);
        var s = fauna.State(i);
        float moved = math.distance(s.Position.xz, start.xz);
        float away = math.distance(s.Position.xz, new float2(w.x, w.z));
        Metric("deer_ran_m", moved);
        Check("deer_bolts", away > 15f && moved > 8f, $"{moved:0.0} m in 3 s, now {away:0.0} m from the wizard ({s.Mode})");

        // A little parade in the meadow, under the probe's control, for the picture.
        var origin = V(27f, 0f, 26f);
        string[] parade = { "Deer", "Rabbit", "Turkey", "RedFox", "Deer" };
        for (int k = 0; k < parade.Length; k++)
        {
            var p = origin + V(-6f + k * 3f, 0f, (k % 2) * 2f);
            int index = fauna.Spawn(fauna.SpeciesIndex(parade[k]), new float3(p.x, grid.GroundAt(p), p.z));
            fauna.Puppet(index, math.PI / 2f, k == 1 ? 0f : 1.2f);
        }
        Cut(V(27f, 1.6f, 18f), V(26f, 0.5f, 26f));
        yield return Wait(1.5f);
        Shot("parade");
        yield return null;
        yield return null;
        Cut(V(40f, 12f, 1f), V(30f, 0f, 30f));
        yield return Wait(0.5f);
        Shot("valley");
    }
}
