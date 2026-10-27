using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Chem;
using UnityEngine;

// The probe's sleep scenario (docs/FAST_LOOP.md): physics props sleep with
// their chunk (PhysicsProp, ChemGrid.ChunkSwitched). The wizard walks off
// beyond the keep distance, so the yard's chunk deactivates: its props go
// kinematic and stay where they are. Back in the yard they wake, and a shove
// moves one again.
public class SleepProbe : ProbeScenario
{
    protected override IEnumerator Run()
    {
        var yard = grid.yardCells;
        var home = new Vector2Int(yard.x + yard.width / 2, yard.y + yard.height / 2);
        var homeChunk = new Vector2Int(Chunks.ChunkOf(home.x), Chunks.ChunkOf(home.y));
        var spawner = FindAnyObjectByType<ThingSpawner>();
        var props = spawner == null ? new List<PhysicsProp>() : spawner.Live
            .Select(t => t.prop)
            .Where(p => p != null && p.State != null && grid.ChunkAt(p.transform.position) == homeChunk)
            .ToList();
        Metric("yard_props", props.Count);
        Check("yard_has_props", props.Count > 0, $"{props.Count} props in the yard's chunk {homeChunk}");
        if (props.Count == 0) yield break;
        Check("awake_while_active", props.All(p => !p.Asleep));

        if (!FindFar(home, out var far))
        {
            Check("far_ground_found", false, "no dry ground beyond the keep distance");
            yield break;
        }
        Place(far.x - yard.x, far.y - yard.y);
        // The yard may linger while anything there is still live; wait for it to deactivate.
        for (float t = 0f; t < 20f && grid.IsChunkActive(homeChunk.x, homeChunk.y); t += 0.5f)
            yield return Wait(0.5f);
        Check("yard_inactive", !grid.IsChunkActive(homeChunk.x, homeChunk.y));
        yield return Wait(0.5f);
        int asleep = props.Count(p => p != null && p.Asleep && p.GetComponent<Rigidbody>().isKinematic);
        Metric("asleep", asleep);
        Check("props_sleep_with_chunk", asleep == props.Count(p => p != null), $"{asleep} of {props.Count} asleep and kinematic");
        int awakeElsewhere = FindObjectsByType<PhysicsProp>(FindObjectsSortMode.None)
            .Count(p => p.Asleep && grid.IsChunkActive(grid.ChunkAt(p.transform.position).x, grid.ChunkAt(p.transform.position).y));
        Check("none_asleep_in_active_chunks", awakeElsewhere == 0, $"{awakeElsewhere} asleep in an active chunk");
        var before = props.Where(p => p != null).Select(p => p.transform.position).ToList();
        yield return Wait(2f);
        float moved = props.Where(p => p != null).Zip(before, (p, b) => Vector3.Distance(p.transform.position, b)).DefaultIfEmpty(0f).Max();
        Metric("moved_while_asleep_m", moved);
        Check("held_while_asleep", moved < 0.01f, $"moved at most {moved:0.000} m");

        Place(yard.width / 2, yard.height / 2);
        yield return Wait(1.5f);
        Check("yard_activates_on_return", grid.IsChunkActive(homeChunk.x, homeChunk.y));
        var live = props.Where(p => p != null).ToList();
        int woke = live.Count(p => !p.Asleep);
        Metric("woke", woke);
        Check("props_wake_with_chunk", woke == live.Count, $"{woke} of {live.Count} awake");
        var pushed = live.FirstOrDefault(p => !p.GetComponent<Rigidbody>().isKinematic);
        if (pushed != null)
        {
            var start = pushed.transform.position;
            Cut(start + V(-5f, 4f, -5f), start);
            pushed.Push(Vector3.up * (5f * pushed.State.Def.Mass * Forces.KgPerMass) + Vector3.forward * (3f * pushed.State.Def.Mass * Forces.KgPerMass));
            yield return Wait(0.5f);
            float d = Vector3.Distance(pushed.transform.position, start);
            Metric("pushed_moved_m", d);
            Check("awake_prop_moves", d > 0.3f, $"the {pushed.name} moved {d:0.00} m after a shove");
            Shot("woken");
        }
    }

    // Dry ground two chunks beyond the keep distance from the yard, the first found going round.
    bool FindFar(Vector2Int home, out Vector2Int found)
    {
        int d = (World.Rules.Sim.Streaming.Keep + 2) * Chunks.Size;
        foreach (var dir in new[] { new Vector2Int(-1, 0), new Vector2Int(1, 0), new Vector2Int(0, -1), new Vector2Int(0, 1), new Vector2Int(-1, -1), new Vector2Int(1, 1) })
        {
            for (int extra = 0; extra < Chunks.Size; extra += 8)
            {
                var c = home + dir * (d + extra);
                if (World.InBounds(c.x, c.y) && World.At(c.x, c.y).Water == 0)
                {
                    found = c;
                    return true;
                }
            }
        }
        found = default;
        return false;
    }
}
