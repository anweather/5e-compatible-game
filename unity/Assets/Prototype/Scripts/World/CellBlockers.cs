using System.Collections.Generic;
using Chem;
using UnityEngine;

// Invisible boxes that keep walkers and props out of blocking cells: a tree
// (around its trunk, where the valley's tree stands, not the whole square),
// brambles, a stone block, and a raised wall (Wall of Stone), up to its top as
// drawn, and a row house's wall, door or pane, which no scene model's box covers.
// The ground itself is the chunk terrain; these only stand where a cell blocks.
// Boxes are pooled: a cell that stops blocking (a tree burnt to a stump)
// hands its box back for the next one, and a new world takes the old one's
// boxes rather than making its 3800 again (ReleaseAll; 130 ms of a rebuild).
public sealed class CellBlockers
{
    const float WallReach = 0.05f, WallFoot = 2f;
    const float TrunkWidth = 0.7f;

    readonly ChemGrid grid;
    readonly Transform parent;
    readonly Dictionary<int, BoxCollider> used = new();
    readonly Stack<BoxCollider> spare = new();
    readonly Dictionary<int, float> built = new();

    const string ParentName = "Blockers";

    public CellBlockers(ChemGrid grid)
    {
        this.grid = grid;
        parent = new GameObject(ParentName).transform;
        parent.SetParent(grid.transform, false);
    }

    public static bool IsBlocker(Collider c) =>
        c != null && c.transform.parent != null && c.transform.parent.name == ParentName;

    public int Count => used.Count;

    public Transform Parent => parent;

    public void ReleaseAll()
    {
        foreach (var box in used.Values) spare.Push(box);
        used.Clear();
        built.Clear();
    }

    public void HideSpare()
    {
        foreach (var box in spare)
            if (box.gameObject.activeSelf) box.gameObject.SetActive(false);
    }

    public float Reach(Cell c)
    {
        var world = grid.World;
        if (world.IsRaisedWall(c)) return WallReach;
        return world.IsBlocking(c) ? CellDressing.Look(world.Rules, c.T).height : 0f;
    }

    public void BuiltWall(int key, float tall)
    {
        if (tall > 0f) built[key] = tall;
        else built.Remove(key);
    }

    public bool IsBuiltWall(int key, Cell c) => built.ContainsKey(key) && grid.World.IsBlocking(c);

    public bool Place(int x, int y, Cell c, float ground)
    {
        int key = grid.World.Idx(x, y);
        float reach = built.TryGetValue(key, out float tall) && grid.World.IsBlocking(c) ? tall : Reach(c);
        if (reach <= 0f)
        {
            Release(key);
            return false;
        }
        if (!used.TryGetValue(key, out var box)) used[key] = box = Take();
        float foot = grid.World.IsRaisedWall(c) ? WallFoot : 0f;
        float width = IsTrunk(c) ? TrunkWidth : grid.cellSize;
        box.size = new Vector3(width, foot + reach, width);
        box.center = new Vector3(0f, (reach - foot) / 2f, 0f);
        Move(x, y, ground);
        return true;
    }

    public void Move(int x, int y, float ground)
    {
        if (!used.TryGetValue(grid.World.Idx(x, y), out var box)) return;
        var center = grid.CellCenter(x, y);
        if (IsTrunk(grid.World.At(x, y)) && grid.valley != null && grid.valley.TrunkAt(x, y, out var trunk)) center = trunk;
        box.transform.position = new Vector3(center.x, ground, center.z);
    }

    public void Remove(int key) => Release(key);

    bool IsTrunk(Cell c) => c.T == TerrainIds.Tree && !grid.World.IsRaisedWall(c);

    void Release(int key)
    {
        if (!used.Remove(key, out var box)) return;
        if (spare.Count >= MostSpare)
        {
            Object.Destroy(box.gameObject);
            return;
        }
        box.gameObject.SetActive(false);
        spare.Push(box);
    }

    const int MostSpare = 8192;

    BoxCollider Take()
    {
        if (spare.Count > 0)
        {
            var box = spare.Pop();
            if (!box.gameObject.activeSelf) box.gameObject.SetActive(true);
            return box;
        }
        PerfCounts.Add("blockers.made");
        var go = new GameObject("Blocker");
        go.transform.SetParent(parent, false);
        return go.AddComponent<BoxCollider>();
    }
}
