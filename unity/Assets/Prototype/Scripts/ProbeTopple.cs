using System.Collections;
using System.Linq;
using UnityEngine;

// The probe's topple scenario (docs/FAST_LOOP.md): earth shaping uproots what
// stands on the ground it moves ("uprootsTo" in materials.json). Move Earth on
// the nearest valley tree to the yard: it falls (deadfall, drawn lying down),
// its blocker goes, and the fallen tree burns. Then Move Earth on the cabin
// west of the yard: its log walls cave in and the cabin falls.
public class ToppleProbe : ProbeScenario
{
    protected override IEnumerator Run()
    {
        if (!NearestTree(out var tree))
        {
            Check("tree_found", false, "no valley tree near the yard");
            yield break;
        }
        Check("tree_found", true, $"cell {tree}");
        var at = grid.CellCenter(tree.x, tree.y);
        at.y = grid.GroundAt(at);
        Cut(at + V(-9f, 7f, -9f), at);
        yield return Wait(0.5f);
        World.Cast(SpellIds.MoveEarth, tree.x, tree.y, 0);
        yield return Wait(2f);
        var c = World.At(tree.x, tree.y);
        Check("tree_toppled", c.T == TerrainIds.Deadfall, $"the tree's square is {c.T}");
        var dressing = grid.transform.Cast<Transform>().FirstOrDefault(t => t.name == $"{TerrainIds.Deadfall} {tree.x},{tree.y}");
        Check("fallen_tree_drawn", dressing != null && dressing.GetComponentsInChildren<Renderer>().Length > 1, dressing == null ? "no dressing" : dressing.GetComponentsInChildren<Renderer>().Length + " renderers");
        bool blocked = Physics.Raycast(at + Vector3.up * 1f - Vector3.right * 3f, Vector3.right, out var hit, 6f, ~0, QueryTriggerInteraction.Ignore) && CellBlockers.IsBlocker(hit.collider);
        Check("no_blocker_left", !blocked, blocked ? $"a ray across the square hits {hit.collider.name}" : "a ray across the square passes");
        Shot("fallen-tree");
        World.Cast(SpellIds.FireBolt, tree.x, tree.y, 0);
        yield return Wait(2f);
        Check("fallen_tree_burns", World.At(tree.x, tree.y).Fire > 0 || World.At(tree.x, tree.y).T == TerrainIds.Dirt, $"fire {World.At(tree.x, tree.y).Fire}");

        var cabin = GameObject.Find("Cabin");
        Check("cabin_found", cabin != null);
        if (cabin == null) yield break;
        var cell = grid.CellAt(cabin.transform.position);
        Cut(cabin.transform.position + V(10f, 8f, -10f), cabin.transform.position);
        float before = cabin.transform.position.y;
        World.Cast(SpellIds.MoveEarth, cell.x, cell.y, 0);
        yield return Wait(2f);
        Check("cabin_walls_cave_in", World.Log.Any(e => e.Text.Contains("walls cave in")), "the log says so");
        Check("cabin_falls", cabin.transform.position.y < before - 1f, $"cabin at {cabin.transform.position.y:0.0} m, was {before:0.0} m");
        Shot("cabin");
    }

    bool NearestTree(out Vector2Int best)
    {
        best = default;
        var yard = grid.yardCells;
        float bestD = float.MaxValue;
        for (int y = yard.yMin - 60; y < yard.yMax + 60; y++)
            for (int x = yard.xMin - 60; x < yard.xMax + 60; x++)
            {
                if (!World.InBounds(x, y) || World.At(x, y).T != TerrainIds.Tree || !grid.valley.TrunkAt(x, y, out _)) continue;
                float d = (new Vector2(x, y) - yard.center).sqrMagnitude;
                if (d >= bestD) continue;
                bestD = d;
                best = new Vector2Int(x, y);
            }
        return bestD < float.MaxValue;
    }
}
