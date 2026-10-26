using System.Collections.Generic;
using Chem;
using UnityEngine;

// Invisible boxes that keep walkers and props out of blocking cells: a tree
// (around its trunk, where the valley's tree stands, not the whole square),
// brambles, a stone block, and a raised wall (Wall of Stone), up to its top as
// drawn. The ground itself is the chunk terrain; these only stand where a cell
// blocks.
// Boxes are pooled: a cell that stops blocking (a tree burnt to a stump)
// hands its box back for the next one.
public sealed class CellBlockers
{
    // A raised wall's blocker reaches its top as drawn (a 1.5 m wall is out of a jump's
    // reach anyway), and this far down its sides, where the terrain falls steeply away from it.
    const float WallReach = 0.05f, WallFoot = 2f;
    // A tree's blocker: about its trunk, not the whole square around it.
    const float TrunkWidth = 0.7f;

    readonly ChemGrid grid;
    readonly Transform parent;
    readonly Dictionary<int, BoxCollider> used = new();
    readonly Stack<BoxCollider> spare = new();

    const string ParentName = "Blockers";

    public CellBlockers(ChemGrid grid)
    {
        this.grid = grid;
        parent = new GameObject(ParentName).transform;
        parent.SetParent(grid.transform, false);
    }

    /// <summary>Whether <paramref name="c"/> is one of these invisible boxes (nothing to stand on or to see).</summary>
    public static bool IsBlocker(Collider c) =>
        c != null && c.transform.parent != null && c.transform.parent.name == ParentName;

    public int Count => used.Count;

    /// <summary>How high a cell's blocker stands above its ground; 0 when it doesn't block.</summary>
    public float Reach(Cell c)
    {
        var world = grid.World;
        if (world.IsRaisedWall(c)) return WallReach;
        return world.IsBlocking(c) ? CellDressing.Look(c.T).height : 0f;
    }

    /// <summary>Puts a cell's blocker on its ground at <paramref name="ground"/>, or removes it if the cell doesn't block.</summary>
    public void Place(int x, int y, Cell c, float ground)
    {
        int key = grid.World.Idx(x, y);
        float reach = Reach(c);
        if (reach <= 0f)
        {
            Release(key);
            return;
        }
        if (!used.TryGetValue(key, out var box)) used[key] = box = Take();
        float foot = grid.World.IsRaisedWall(c) ? WallFoot : 0f;
        float width = IsTrunk(c) ? TrunkWidth : grid.cellSize;
        box.size = new Vector3(width, foot + reach, width);
        box.center = new Vector3(0f, (reach - foot) / 2f, 0f);
        Move(x, y, ground);
    }

    /// <summary>Moves a cell's blocker, if it has one, with its ground (earth shaping).</summary>
    public void Move(int x, int y, float ground)
    {
        if (!used.TryGetValue(grid.World.Idx(x, y), out var box)) return;
        var center = grid.CellCenter(x, y);
        // A tree's blocker stands at its trunk, where the valley drew it in the square.
        if (IsTrunk(grid.World.At(x, y)) && grid.valley != null && grid.valley.TrunkAt(x, y, out var trunk)) center = trunk;
        box.transform.position = new Vector3(center.x, ground, center.z);
    }

    bool IsTrunk(Cell c) => c.T == TerrainIds.Tree && !grid.World.IsRaisedWall(c);

    void Release(int key)
    {
        if (!used.Remove(key, out var box)) return;
        box.gameObject.SetActive(false);
        spare.Push(box);
    }

    BoxCollider Take()
    {
        if (spare.Count > 0)
        {
            var box = spare.Pop();
            box.gameObject.SetActive(true);
            return box;
        }
        var go = new GameObject("Blocker");
        go.transform.SetParent(parent, false);
        return go.AddComponent<BoxCollider>();
    }
}
