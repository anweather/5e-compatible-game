using System.Collections;
using System.Linq;
using Chem;
using UnityEngine;

// The probe's collision scenario (docs/FAST_LOOP.md): the invisible blockers
// where cells block (CellBlockers) against what is drawn. A tree's blocker
// stands about its trunk, so a ray a metre beside the trunk is clear and one
// through it is stopped; a Wall of Stone's blocker tops out where the wall is
// drawn, with no invisible ceiling above it.
public class CollisionProbe : ProbeScenario
{
    protected override IEnumerator Run()
    {
        CheckTree();
        yield return CheckWall();
    }

    void CheckTree()
    {
        var yard = grid.yardCells;
        var home = new Vector2Int(yard.x + yard.width / 2, yard.y + yard.height / 2);
        Vector3 trunk = default;
        var cell = Vector2Int.zero;
        bool found = false;
        for (int r = 4; r < 120 && !found; r++)
            for (int dx = -r; dx <= r && !found; dx++)
            {
                var c = new Vector2Int(home.x + dx, home.y - r);
                if (World.InBounds(c.x, c.y) && World.At(c.x, c.y).T == TerrainIds.Tree && grid.valley != null && grid.valley.TrunkAt(c.x, c.y, out trunk))
                {
                    cell = c;
                    found = true;
                }
            }
        Check("tree_found", found, found ? $"tree at cell {cell}" : "no valley tree near the yard");
        if (!found) return;
        float ground = grid.GroundTop(cell.x, cell.y, World.At(cell.x, cell.y)) + 1f;
        trunk.y = ground;
        // Through the trunk, west to east: a blocker stops it.
        bool stopped = BlockerHit(trunk - Vector3.right * 3f, Vector3.right, 6f);
        // A metre north of the trunk (still in its square, or the next): no blocker in the way.
        bool clearBeside = !BlockerHit(trunk + Vector3.forward * 1f - Vector3.right * 3f, Vector3.right, 6f);
        Check("tree_trunk_blocks", stopped, "a ray through the trunk hits its blocker");
        Check("tree_beside_trunk_clear", clearBeside, "a ray 1 m beside the trunk passes");
    }

    IEnumerator CheckWall()
    {
        Place(8, 6);
        yield return CastFromBar(SpellIds.WallOfStone, 12, 7, 0, 3f);
        var cells = SpellCells(SpellIds.WallOfStone, 12, 7, 0).Where(c => World.IsRaisedWall(World.At(c.x, c.y))).ToList();
        Check("wall_raised", cells.Count > 0, $"{cells.Count} wall squares");
        if (cells.Count == 0) yield break;
        var (x, y) = cells[cells.Count / 2];
        var top = grid.CellCenter(x, y);
        float drawn = grid.GroundTop(x, y, World.At(x, y));
        bool hit = Physics.Raycast(top + Vector3.up * 20f, Vector3.down, out var h, 40f, ~0, QueryTriggerInteraction.Ignore);
        Metric("wall_drawn_top", drawn);
        Metric("wall_collider_top", hit ? h.point.y : -99f);
        Check("wall_top_matches_drawing", hit && Mathf.Abs(h.point.y - drawn) < 0.25f,
            hit ? $"a ray down stops at {h.point.y:0.00} m ('{h.collider.name}'), the wall is drawn to {drawn:0.00} m" : "nothing under the ray");
        Cut(top + V(6f, 4f, -6f), top);
        yield return Wait(0.3f);
        Shot("wall");
    }

    static readonly RaycastHit[] hits = new RaycastHit[32];

    static bool BlockerHit(Vector3 from, Vector3 dir, float length)
    {
        int n = Physics.RaycastNonAlloc(from, dir, hits, length, ~0, QueryTriggerInteraction.Ignore);
        for (int i = 0; i < n; i++) if (CellBlockers.IsBlocker(hits[i].collider)) return true;
        return false;
    }
}
