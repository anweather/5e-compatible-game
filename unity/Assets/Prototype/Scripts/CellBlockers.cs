using System.Collections.Generic;
using Chem;
using UnityEngine;

// Invisible boxes that keep walkers and props out of blocking cells: a tree's or
// brambles' cell, a stone block, and a raised wall (Wall of Stone), whose
// blocker reaches a jump's height above its top so it can't be climbed. The
// ground itself is the chunk terrain; these only stand where a cell blocks.
// Boxes are pooled: a cell that stops blocking (a tree burnt to a stump)
// hands its box back for the next one.
public sealed class CellBlockers
{
    // A raised wall's blocker reaches this far above its top, out of a jump's reach,
    // and this far down its sides, where the terrain falls steeply away from it.
    const float WallReach = 1.2f, WallFoot = 2f;

    readonly ChemGrid grid;
    readonly Transform parent;
    readonly Dictionary<int, BoxCollider> used = new();
    readonly Stack<BoxCollider> spare = new();

    public CellBlockers(ChemGrid grid)
    {
        this.grid = grid;
        parent = new GameObject("Blockers").transform;
        parent.SetParent(grid.transform, false);
    }

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
        box.size = new Vector3(grid.cellSize, foot + reach, grid.cellSize);
        box.center = new Vector3(0f, (reach - foot) / 2f, 0f);
        Move(x, y, ground);
    }

    /// <summary>Moves a cell's blocker, if it has one, with its ground (earth shaping).</summary>
    public void Move(int x, int y, float ground)
    {
        if (!used.TryGetValue(grid.World.Idx(x, y), out var box)) return;
        var center = grid.CellCenter(x, y);
        box.transform.position = new Vector3(center.x, ground, center.z);
    }

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
