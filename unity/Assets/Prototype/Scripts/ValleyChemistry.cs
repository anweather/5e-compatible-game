using System.Collections.Generic;
using Chem;
using UnityEngine;

// Shows the chemistry of the cells on the valley's chunk terrains (ValleyGround
// does their heights and paint): trees that burn down to charred stumps, burnt
// grass leaving scorched ground with its grass cleared, the cabin charring and
// losing its roof once enough of its log walls have burned, and earth shaping
// raising or sinking the ground under reshaped cells. Every cell is on the
// terrain, the old yard included; the lake's cells are pits under the lake's
// own surface.
// Works on runtime copies of the terrain data, so playing never changes the
// saved valley.
public class ValleyChemistry : MonoBehaviour
{
    public ChemGrid grid;
    [Tooltip("The chunk terrains, chunk (i, j) at j * 9 + i (TerrainGrid.Valley()).")]
    public Terrain[] chunks;
    public Transform cabin;
    [Tooltip("Height of the lake's surface; lake cells' effects sit on it.")]
    public float lakeLevel = -0.6f;

    readonly TerrainGrid layout = TerrainGrid.Valley();
    ValleyGround ground;
    readonly Dictionary<(int i, int j), TreeInstance[]> trees = new();
    readonly Dictionary<int, List<((int i, int j) chunk, int index)>> treesByCell = new();
    readonly HashSet<(int i, int j)> treesChanged = new();
    readonly HashSet<int> scorched = new();
    int cabinCells, cabinBurnt;
    bool cabinCharred;
    Material charred;

    /// <summary>The ground under the cells; null before the first Bind.</summary>
    public ValleyGround Ground => ground;

    /// <summary>Starts over on a world (ChemGrid calls this for every new world): fresh terrain copies, trees and cabin counted.</summary>
    public void Bind(World world)
    {
        if (world.W != layout.GridW || world.H != layout.GridH)
            Debug.LogError($"The valley's chunk layout is for a {layout.GridW}×{layout.GridH} world, not {world.W}×{world.H}");
        ground ??= new ValleyGround(layout, chunks);
        ground.Bind(world, grid.stepHeight, grid.stepHeight / Mathf.Max(0.05f, grid.shapeSeconds));
        scorched.Clear();
        IndexTrees();
        cabinCells = cabinBurnt = 0;
        foreach (var c in world.Cells)
        {
            if (c.T == TerrainIds.LogWall) cabinCells++;
        }
        if (charred == null) charred = UrpMaterials.NewLit("Charred", new Color(0.07f, 0.06f, 0.055f), 0.05f);
    }

    void IndexTrees()
    {
        trees.Clear();
        treesByCell.Clear();
        treesChanged.Clear();
        foreach (var (at, data) in ground.Chunks())
        {
            var list = data.treeInstances;
            trees[at] = list;
            var (cx, cz) = layout.ChunkCorner(at.i, at.j);
            for (int n = 0; n < list.Length; n++)
            {
                var p = list[n].position;
                var (x, y) = layout.CellAt(cx + p.x * data.size.x, cz + p.z * data.size.z);
                if (!layout.InGrid(x, y)) continue;
                int key = grid.World.Idx(x, y);
                if (!treesByCell.TryGetValue(key, out var mine)) treesByCell[key] = mine = new();
                mine.Add((at, n));
            }
        }
    }

    /// <summary>Where the trunk of a tree drawn in a cell stands (the first, if there are several); false when none is.</summary>
    public bool TrunkAt(int x, int y, out Vector3 at)
    {
        at = default;
        if (!treesByCell.TryGetValue(grid.World.Idx(x, y), out var list) || list.Count == 0) return false;
        var (chunk, n) = list[0];
        var p = trees[chunk][n].position;
        var size = ground.Data(chunk).size;
        var (cx, cz) = layout.ChunkCorner(chunk.i, chunk.j);
        at = new Vector3(cx + p.x * size.x, 0f, cz + p.z * size.z);
        return true;
    }

    /// <summary>The valley's own ground under a cell, before the cell's offset.</summary>
    public float BaseHeight(int x, int y) => ground != null ? ground.BaseHeight(x, y) : 0f;

    /// <summary>The ground drawn at a cell's centre now (it lags its target while easing).</summary>
    public float ShownGround(int x, int y) => ground != null ? ground.ShownGround(x, y) : 0f;

    /// <summary>The terrain's ground drawn now at a world point over the cells.</summary>
    public float GroundAt(Vector3 p) => ground != null ? ground.GroundAt(p.x, p.z) : 0f;

    /// <summary>Eases reshaped ground by dt seconds (ChemGrid calls this once a frame).</summary>
    public void AdvanceShaping(float dt) => ground?.Advance(dt);

    /// <summary>True for a cell of the lake: a pit whose ground is under the lake's surface.</summary>
    public bool IsLake(Cell c, int x, int y) => c.T == TerrainIds.Pit && BaseHeight(x, y) < lakeLevel;

    /// <summary>How high above the ground a cell's fire burns: in the crown of a tree, along a cabin wall.</summary>
    public static float FireHeight(string terrainType) => terrainType switch
    {
        TerrainIds.Tree => 2.6f,
        TerrainIds.LogWall => 1.4f,
        _ => 0f,
    };

    /// <summary>
    /// A cell's terrain, height or cover changed (from is its terrain before, null for its first
    /// look, which the baked valley already shows): burn its trees, char the cabin, repaint it
    /// and, if it was <paramref name="reshaped"/>, ease its ground to its new height.
    /// </summary>
    public void CellChanged(int x, int y, string from, string to, bool reshaped)
    {
        if (from == null || ground == null) return;
        if (from != to) TerrainChanged(x, y, from, to);
        if (reshaped) ground.Reshape(x, y);
        ground.Repaint(x, y);
    }

    void TerrainChanged(int x, int y, string from, string to)
    {
        if (from == TerrainIds.Tree) BurnTrees(x, y);
        if ((from == TerrainIds.Grass || from == TerrainIds.Brush) && to == TerrainIds.Dirt) scorched.Add(grid.World.Idx(x, y));
        if (from == TerrainIds.LogWall) CabinWallBurned();
    }

    void BurnTrees(int x, int y)
    {
        if (!treesByCell.TryGetValue(grid.World.Idx(x, y), out var list)) return;
        foreach (var (at, n) in list)
        {
            var t = trees[at][n];
            t.heightScale = 0f;
            t.widthScale = 0f;
            trees[at][n] = t;
            treesChanged.Add(at);
        }
    }

    void CabinWallBurned()
    {
        cabinBurnt++;
        if (cabinCharred || cabin == null || cabinBurnt * 2 < cabinCells) return;
        cabinCharred = true;
        foreach (var r in cabin.GetComponentsInChildren<Renderer>())
        {
            if (r.name == "Roof") r.enabled = false;
            else if (r.GetComponent<ParticleSystem>() == null) r.sharedMaterial = charred;
        }
        foreach (var l in cabin.GetComponentsInChildren<Light>()) l.enabled = false;
        EventLog.Add("The cabin burns down");
    }

    bool IsScorched(int x, int y) => scorched.Contains(grid.World.Idx(x, y));

    // Terrain edits are batched once per frame.
    readonly System.Diagnostics.Stopwatch timer = new();

    /// <summary>Milliseconds the last frame spent handing burnt trees and changed ground to the terrains (the wildfire probe reads them).</summary>
    public double TreesMs { get; private set; }
    public double GroundMs { get; private set; }

    void LateUpdate()
    {
        if (ground == null) return;
        timer.Restart();
        foreach (var at in treesChanged) ground.WritableData(at).treeInstances = trees[at];
        treesChanged.Clear();
        TreesMs = timer.Elapsed.TotalMilliseconds;
        timer.Restart();
        ground.Flush(IsScorched);
        GroundMs = timer.Elapsed.TotalMilliseconds;
    }

    void OnDestroy()
    {
        if (charred != null) Destroy(charred);
        ground?.Destroy();
    }
}
