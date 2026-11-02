using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

// The probe's spawners scenario (docs/FAST_LOOP.md): which species with a "spawn" really turn up. The
// wilds run fast with no cap at three places, and every animal that comes out is counted by species.
public class SpawnersProbe : ProbeScenario
{
    const float StaySeconds = 40f;
    static readonly string[] Birds = { "turkey", "quail", "heron", "ptarmigan", "roadrunner" };

    protected override IEnumerator Run()
    {
        var fauna = FindAnyObjectByType<ValleyFauna>();
        Check("fauna_built", fauna != null, "ValleyFauna");
        if (fauna == null) yield break;
        fauna.maxCreatures = 400;
        fauna.spawnRate = 60f;
        fauna.homeDensity = fauna.wildDensity;
        var seen = new Dictionary<string, HashSet<int>>();
        var start = wizard.transform.position;
        foreach (var offset in new[] { Vector3.zero, new Vector3(120f, 0f, 60f), new Vector3(-90f, 0f, 150f) })
        {
            var p = start + offset;
            Teleport(new Vector3(p.x, grid.GroundAt(p) + 0.1f, p.z));
            for (float t = 0f; t < StaySeconds; t += 0.5f)
            {
                yield return Wait(0.5f);
                for (int i = 0; i < fauna.Count; i++)
                {
                    string id = fauna.Body(fauna.SpeciesOf(i)).Id;
                    if (!seen.TryGetValue(id, out var ones)) seen[id] = ones = new HashSet<int>();
                    ones.Add(fauna.Id(i));
                }
            }
        }
        foreach (var (id, ones) in seen.OrderBy(s => s.Key, System.StringComparer.Ordinal)) Metric("seen_" + id, ones.Count);
        foreach (string bird in Birds) Check("bird_" + bird, seen.ContainsKey(bird), $"{(seen.TryGetValue(bird, out var ones) ? ones.Count : 0)} came out");
    }
}
