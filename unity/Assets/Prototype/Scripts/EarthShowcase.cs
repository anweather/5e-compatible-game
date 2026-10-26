using System.Collections;
using Chem;
using UnityEngine;

// A short film of earth magic off the grid, for the trailer page (Prototype >
// Record Showcase, DemoRecorder): on the river's east bank, east of the yard,
//
//   1. Off the grid: an Earthen Trench cut from the bank at a slant (not one
//      of the grid's four ways), and the river runs down it.
//   2. Rock wall: a slanted Wall of Stone rises across the trench, a ridge of
//      rocks, and dams it.
//   3. Toppling: Move Earth heaves the ground under the nearest trees, which
//      fall, then under the cabin on the river bank, which caves in.
public class EarthShowcase : ShowcaseScript
{
    Vector2Int bank;
    System.Collections.Generic.List<(int x, int y)> trench;

    protected override IEnumerator Play()
    {
        if (!FindBank()) yield break;
        yield return Switch(At(12f));
        yield return Trench();
        yield return RockWall();
        yield return Topple();
    }

    // The first dry square east of the river, near world (120, 30).
    bool FindBank()
    {
        var near = grid.CellAt(new Vector3(120f, 0f, 30f));
        for (int d = 0; d < 14; d++)
            for (int y = near.y - d; y <= near.y + d; y++)
                for (int x = near.x - d; x <= near.x + d; x++)
                {
                    if (!World.InBounds(x, y) || World.At(x, y).T != TerrainIds.River) continue;
                    int edge = x;
                    while (World.At(edge + 1, y).T == TerrainIds.River) edge++;
                    bank = new Vector2Int(edge + 1, y);
                    return true;
                }
        return false;
    }

    IEnumerator Trench()
    {
        Chapter("Off the grid");
        PlaceCell(bank.x, bank.y);
        var cast = new CastCommand(SpellIds.EarthenTrench, bank.x + 1, bank.y, 1, 2, 16, 7);
        trench = World.SpellCells(World.Rules.Spell(cast.Spell), cast.X, cast.Y, cast.Dir, cast.Charge, cast.AimX, cast.AimY);
        var far = grid.CellCenter(trench[trench.Count - 1].x, trench[trench.Count - 1].y);
        var mid = (grid.CellCenter(bank.x, bank.y) + far) / 2f;
        mid.y = TerrainGround(mid);
        puppet.Face(far);
        // From high over the north side, looking down the slant of it.
        Cut(mid + V(2f, 26f, 5f), mid + V(0f, 0f, -1f));
        yield return Wait(1.2f);
        caster.Cast(cast);
        var flight = Fly(W(mid + V(-7f, 11f, 9f), mid + V(1f, 0f, -1f), 7f));
        yield return new WaitUntil(() => flight.Done);
        yield return Wait(1f);
    }

    IEnumerator RockWall()
    {
        Chapter("Rock wall");
        var across = trench[trench.Count * 2 / 3];
        var cast = new CastCommand(SpellIds.WallOfStone, across.x - 1, across.y - 3, World.DirOf(4, 16), 1, 4, 16);
        var at = grid.CellCenter(across.x, across.y);
        at.y = TerrainGround(at);
        Cut(at + V(-7f, 8f, 9f), at + V(0f, 0.5f, 0f));
        yield return Wait(0.8f);
        caster.Cast(cast);
        var flight = Fly(W(at + V(-9f, 5f, 6f), at + V(0f, 0.8f, 0f), 5f));
        yield return new WaitUntil(() => flight.Done);
        yield return Wait(0.8f);
    }

    IEnumerator Topple()
    {
        Chapter("Toppling");
        if (NearestTree(bank, 45, out var tree))
        {
            var at = grid.CellCenter(tree.x, tree.y);
            at.y = TerrainGround(at);
            Cut(at + V(-12f, 8f, -12f), at + V(0f, 3f, 0f));
            yield return Wait(1f);
            World.Cast(SpellIds.MoveEarth, tree.x, tree.y, 0);
            caster.PlayCastEffect(SpellIds.MoveEarth, tree.x, tree.y, 0);
            yield return Wait(4.5f);
        }
        var cabin = GameObject.Find("Cabin 3");
        if (cabin == null) yield break;
        var c = grid.CellAt(cabin.transform.position);
        PlaceCell(c.x - 6, c.y + 2);
        var p = cabin.transform.position;
        Cut(p + V(-11f, 6f, -11f), p + V(0f, 1.5f, 0f));
        yield return Wait(1f);
        World.Cast(SpellIds.MoveEarth, c.x, c.y, 0);
        caster.PlayCastEffect(SpellIds.MoveEarth, c.x, c.y, 0);
        var flight = Fly(W(p + V(-8f, 5f, -12f), p + V(0f, 0.5f, 0f), 5f));
        yield return new WaitUntil(() => flight.Done);
        yield return Wait(0.5f);
    }

    bool NearestTree(Vector2Int from, int reach, out Vector2Int best)
    {
        best = default;
        float bestD = float.MaxValue;
        for (int y = from.y - reach; y <= from.y + reach; y++)
            for (int x = from.x - reach; x <= from.x + reach; x++)
            {
                // A tree with others round it, so a heave fells a few.
                if (!IsTree(x, y) || !grid.valley.TrunkAt(x, y, out _) || TreesAround(x, y) < 3) continue;
                float d = (new Vector2(x, y) - from).sqrMagnitude;
                if (d >= bestD) continue;
                bestD = d;
                best = new Vector2Int(x, y);
            }
        return bestD < float.MaxValue;
    }

    bool IsTree(int x, int y) => World.InBounds(x, y) && World.At(x, y).T == TerrainIds.Tree;

    int TreesAround(int x, int y)
    {
        int n = 0;
        for (int dy = -3; dy <= 3; dy++)
            for (int dx = -3; dx <= 3; dx++)
                if (IsTree(x + dx, y + dy)) n++;
        return n;
    }
}
