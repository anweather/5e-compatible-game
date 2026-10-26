using System.Collections.Generic;
using Chem;
using UnityEngine;

// Shows the chemistry of the cells on the valley's chunk terrains (ValleyGround
// does their heights and paint): trees that burn down to charred stumps, burnt
// grass leaving scorched ground with its grass cleared, a cabin charring and
// losing its roof once half its log walls have burned, and earth shaping
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
    [Tooltip("The cabins; a log wall cell belongs to the nearest.")]
    public Transform[] cabins;
    [Tooltip("Height of the lake's surface; lake cells' effects sit on it.")]
    public float lakeLevel = -0.6f;

    readonly TerrainGrid layout = TerrainGrid.Valley();
    ValleyGround ground;
    readonly Dictionary<(int i, int j), TreeInstance[]> trees = new();
    // The trees as the valley was baked, untouched by burning or felling: what a fallen tree was.
    readonly Dictionary<(int i, int j), TreeInstance[]> bakedTrees = new();
    readonly Dictionary<int, List<((int i, int j) chunk, int index)>> treesByCell = new();
    readonly HashSet<(int i, int j)> treesChanged = new();
    readonly HashSet<int> scorched = new();
    int[] cabinCells, cabinBurnt, cabinWrecked;
    bool[] cabinCharred;
    Material charred;

    /// <summary>
    /// Whether the terrain stays flat under raised walls, because the grid builds them of rock
    /// models (ChemGrid.wallRocks, CellDressing) that the terrain's own wall would bury.
    /// </summary>
    public bool FlatUnderWalls => grid != null && grid.wallRocks != null && grid.wallRocks.Length > 0 && grid.wallRocks[0] != null;

    /// <summary>The ground under the cells; null before the first Bind.</summary>
    public ValleyGround Ground => ground;

    /// <summary>Starts over on a world (ChemGrid calls this for every new world): fresh terrain copies, trees and cabin counted.</summary>
    public void Bind(World world)
    {
        if (world.W != layout.GridW || world.H != layout.GridH)
            Debug.LogError($"The valley's chunk layout is for a {layout.GridW}×{layout.GridH} world, not {world.W}×{world.H}");
        ground ??= new ValleyGround(layout, chunks);
        ground.Bind(world, grid.stepHeight, grid.stepHeight / Mathf.Max(0.05f, grid.shapeSeconds), FlatUnderWalls);
        scorched.Clear();
        IndexTrees();
        int n = cabins?.Length ?? 0;
        cabinCells = new int[n];
        cabinBurnt = new int[n];
        cabinWrecked = new int[n];
        cabinCharred = new bool[n];
        for (int y = 0; y < world.H; y++)
            for (int x = 0; x < world.W; x++)
            {
                int k = world.At(x, y).T == TerrainIds.LogWall ? CabinAt(x, y) : -1;
                if (k >= 0) cabinCells[k]++;
            }
        if (charred == null) charred = UrpMaterials.NewLit("Charred", new Color(0.07f, 0.06f, 0.055f), 0.05f);
    }

    void IndexTrees()
    {
        trees.Clear();
        bakedTrees.Clear();
        treesByCell.Clear();
        treesChanged.Clear();
        foreach (var (at, data) in ground.Chunks())
        {
            var list = data.treeInstances;
            trees[at] = list;
            bakedTrees[at] = (TreeInstance[])list.Clone();
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

    /// <summary>
    /// The tree that stood in a cell, for drawing it fallen (CellDressing): its model and its
    /// scale; false when the valley drew none there.
    /// </summary>
    public bool FelledTree(int x, int y, out GameObject prefab, out Vector3 scale)
    {
        prefab = null;
        scale = Vector3.one;
        if (!treesByCell.TryGetValue(grid.World.Idx(x, y), out var list) || list.Count == 0) return false;
        var (chunk, n) = list[0];
        var t = bakedTrees[chunk][n];
        var protos = ground.Data(chunk).treePrototypes;
        if (t.prototypeIndex < 0 || t.prototypeIndex >= protos.Length) return false;
        prefab = protos[t.prototypeIndex].prefab;
        scale = new Vector3(t.widthScale, t.heightScale, t.widthScale);
        return prefab != null;
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
        TerrainIds.Deadfall => 0.5f,
        _ => 0f,
    };

    /// <summary>
    /// A cell's terrain, height or cover changed (from is its terrain before, null for its first
    /// look, which the baked valley already shows): burn its trees, char the cabin, repaint it
    /// and, if it was <paramref name="reshaped"/>, ease its ground to its new height.
    /// </summary>
    public void CellChanged(int x, int y, string from, string to, bool reshaped)
    {
        if (ground == null) return;
        // First look at a felled or burnt tree (a world replayed for a late joiner): the baked tree goes.
        if (from == null && (to == TerrainIds.Deadfall || to == TerrainIds.Stump)) BurnTrees(x, y);
        if (from == null) return;
        if (from != to) TerrainChanged(x, y, from, to);
        if (reshaped) ground.Reshape(x, y);
        ground.Repaint(x, y);
    }

    void TerrainChanged(int x, int y, string from, string to)
    {
        if (from == TerrainIds.Tree) BurnTrees(x, y);
        if ((from == TerrainIds.Grass || from == TerrainIds.Brush) && to == TerrainIds.Dirt) scorched.Add(grid.World.Idx(x, y));
        if (from == TerrainIds.LogWall) CabinWallLost(CabinAt(x, y), burning: grid.World.At(x, y).Fire > 0);
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

    // The cabin nearest a cell, or -1 without any.
    int CabinAt(int x, int y)
    {
        var at = grid.CellCenter(x, y);
        int best = -1;
        float bestD = float.MaxValue;
        for (int i = 0; i < (cabins?.Length ?? 0); i++)
        {
            if (cabins[i] == null) continue;
            var d = cabins[i].position - at;
            d.y = 0f;
            if (d.sqrMagnitude >= bestD) continue;
            bestD = d.sqrMagnitude;
            best = i;
        }
        return best;
    }

    // A cabin's log wall burnt through, or caved in under earth shaping. Once half its walls are
    // gone the cabin falls: charred if fire took most of them, else caved in (sunk, its roof down).
    void CabinWallLost(int k, bool burning)
    {
        if (k < 0) return;
        if (burning) cabinBurnt[k]++;
        else cabinWrecked[k]++;
        if (cabinCharred[k] || (cabinBurnt[k] + cabinWrecked[k]) * 2 < cabinCells[k]) return;
        cabinCharred[k] = true;
        var cabin = cabins[k];
        if (cabinWrecked[k] > cabinBurnt[k])
        {
            CaveIn(cabin);
            return;
        }
        foreach (var r in cabin.GetComponentsInChildren<Renderer>())
        {
            if (r.name == "Roof") r.enabled = false;
            else if (r.GetComponent<ParticleSystem>() == null) r.sharedMaterial = charred;
        }
        foreach (var l in cabin.GetComponentsInChildren<Light>()) l.enabled = false;
        EventLog.Add("The cabin burns down");
    }

    static void CaveIn(Transform cabin)
    {
        foreach (var r in cabin.GetComponentsInChildren<Renderer>())
            if (r.name == "Roof" || r.GetComponent<ParticleSystem>() != null) r.enabled = false;
        foreach (var l in cabin.GetComponentsInChildren<Light>()) l.enabled = false;
        cabin.SetPositionAndRotation(cabin.position + Vector3.down * 1.3f, cabin.rotation * Quaternion.Euler(4f, 0f, -3f));
        EventLog.Add("The cabin caves in");
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
