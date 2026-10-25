using System.Collections.Generic;
using UnityEngine;

// Shows the chemistry of the cells outside the yard on the valley itself:
// where each cell's ground is, trees that burn down to charred stumps, burnt
// grass leaving scorched ground with its grass cleared, the cabin
// charring and losing its roof once enough of its log walls have burned, and
// earth shaping: a raised or dug cell lifts or sinks the terrain under it
// (its nearest heightmap sample; the valley's samples are about a cell apart),
// eased over half a second. The terrain runs flush under the yard
// (ValleyBuilder) and would cover anything dug below it (a pit, the pond's
// bed, a trench), so the runtime copy has a hole cut under the yard, where
// the yard's own tiles are the ground.
// Works on a runtime copy of the terrain data, so playing never changes the
// saved valley.
public class ValleyChemistry : MonoBehaviour
{
    public ChemGrid grid;
    public Terrain terrain;
    public Transform cabin;
    [Tooltip("Terrain layer painted where grass has burned.")]
    public int scorchedLayer = 5;
    [Tooltip("Height of the lake's surface; lake cells' effects sit on it.")]
    public float lakeLevel = -0.6f;

    TerrainData data;
    TreeInstance[] trees;
    readonly Dictionary<int, List<int>> treesByCell = new();
    readonly List<Vector2Int> scorch = new();
    bool treesChanged;
    int cabinCells, cabinBurnt;
    bool cabinCharred;
    Material charred;

    // One heightmap sample under a reshaped cell: its height before any shaping, and the offset
    // it is easing from and to (normalised heights, as TerrainData stores them).
    sealed class Heave
    {
        public int hx, hz;
        public float baseHeight, offset, from, to, start;
    }
    const float HeaveSeconds = 0.5f;
    readonly Dictionary<int, Heave> heaves = new();
    readonly List<Heave> easing = new();

    void Start() => Init();

    void Init()
    {
        if (data != null || grid == null || grid.World == null || terrain == null) return;

        data = Instantiate(terrain.terrainData);
        terrain.terrainData = data;
        if (terrain.TryGetComponent<TerrainCollider>(out var collider)) collider.terrainData = data;
        CutUnderYard();

        trees = data.treeInstances;
        var origin = terrain.transform.position;
        for (int i = 0; i < trees.Length; i++)
        {
            var cell = grid.CellAt(origin + Vector3.Scale(trees[i].position, data.size));
            if (!grid.World.InBounds(cell.x, cell.y)) continue;
            int key = grid.World.Idx(cell.x, cell.y);
            if (!treesByCell.TryGetValue(key, out var list)) treesByCell[key] = list = new List<int>();
            list.Add(i);
        }
        foreach (var c in grid.World.Cells)
        {
            if (c.T == TerrainIds.LogWall) cabinCells++;
        }
        charred = UrpMaterials.NewLit("Charred", new Color(0.07f, 0.06f, 0.055f), 0.05f);
    }

    // Holes in every hole texel that lies wholly within the yard. The texels are about a cell
    // wide, so a strip under the yard's outer cells keeps its terrain (just under their tiles).
    void CutUnderYard()
    {
        var yard = grid.yardCells;
        if (yard.width <= 0) return;
        float half = grid.cellSize / 2f;
        var southWest = grid.CellCenter(yard.xMin, yard.yMax - 1) - terrain.transform.position;
        var northEast = grid.CellCenter(yard.xMax - 1, yard.yMin) - terrain.transform.position;
        int res = data.holesResolution;
        float sx = data.size.x / res, sz = data.size.z / res;
        int i0 = Mathf.CeilToInt((southWest.x - half) / sx), j0 = Mathf.CeilToInt((southWest.z - half) / sz);
        int i1 = Mathf.FloorToInt((northEast.x + half) / sx), j1 = Mathf.FloorToInt((northEast.z + half) / sz);
        if (i0 < 0 || j0 < 0 || i1 > res || j1 > res || i1 <= i0 || j1 <= j0) return;
        data.SetHoles(i0, j0, new bool[j1 - j0, i1 - i0]);
    }

    /// <summary>The terrain's height at a cell's centre.</summary>
    public float GroundHeight(int x, int y)
    {
        Init();
        var p = grid.CellCenter(x, y);
        float h = terrain == null ? 0f : terrain.SampleHeight(p) + terrain.transform.position.y;
        var c = grid.World.At(x, y);
        return c != null && c.Water > 0 && c.T == TerrainIds.Pit ? Mathf.Max(h, lakeLevel) : h;
    }

    /// <summary>How high above the ground a cell's fire burns: in the crown of a tree, along a cabin wall.</summary>
    public static float FireHeight(string terrainType) => terrainType switch
    {
        TerrainIds.Tree => 2.6f,
        TerrainIds.LogWall => 1.4f,
        _ => 0f,
    };

    /// <summary>A cell outside the yard changed terrain (for example a tree burned to a stump).</summary>
    public void TerrainChanged(int x, int y, string from, string to)
    {
        Init();
        if (from == null || data == null) return;
        if (from == TerrainIds.Tree) BurnTrees(x, y);
        if ((from == TerrainIds.Grass || from == TerrainIds.Brush) && to == TerrainIds.Dirt) scorch.Add(new Vector2Int(x, y));
        if (from == TerrainIds.LogWall) CabinWallBurned();
    }

    /// <summary>A cell outside the yard was raised or dug: ease the terrain under it to its new height.</summary>
    public void HeightChanged(int x, int y)
    {
        Init();
        var c = grid.World.At(x, y);
        if (data == null || c == null) return;
        var local = grid.CellCenter(x, y) - terrain.transform.position;
        int res = data.heightmapResolution;
        int hx = Mathf.Clamp(Mathf.RoundToInt(local.x / data.size.x * (res - 1)), 0, res - 1);
        int hz = Mathf.Clamp(Mathf.RoundToInt(local.z / data.size.z * (res - 1)), 0, res - 1);
        int key = hz * res + hx;
        if (!heaves.TryGetValue(key, out var h))
            heaves[key] = h = new Heave { hx = hx, hz = hz, baseHeight = data.GetHeights(hx, hz, 1, 1)[0, 0] };
        h.from = h.offset;
        h.to = c.Height * grid.stepHeight / data.size.y;
        h.start = Time.time;
        if (!easing.Contains(h)) easing.Add(h);
    }

    void EaseHeaves()
    {
        if (easing.Count == 0) return;
        var one = new float[1, 1];
        easing.RemoveAll(h =>
        {
            float k = Mathf.Clamp01((Time.time - h.start) / HeaveSeconds);
            h.offset = Mathf.Lerp(h.from, h.to, k * k * (3f - 2f * k));
            one[0, 0] = h.baseHeight + h.offset;
            data.SetHeightsDelayLOD(h.hx, h.hz, one);
            return k >= 1f;
        });
        data.SyncHeightmap();
    }

    void BurnTrees(int x, int y)
    {
        if (!treesByCell.TryGetValue(grid.World.Idx(x, y), out var list)) return;
        foreach (int i in list)
        {
            var t = trees[i];
            t.heightScale = 0f;
            t.widthScale = 0f;
            trees[i] = t;
        }
        treesChanged = true;
        scorch.Add(new Vector2Int(x, y));

        var stump = Primitives.Visual(PrimitiveType.Cylinder, "Stump");
        stump.transform.SetParent(transform, false);
        stump.transform.position = grid.CellCenter(x, y) + Vector3.up * (GroundHeight(x, y) + 0.3f);
        stump.transform.localScale = new Vector3(0.45f, 0.3f, 0.45f);
        stump.GetComponent<Renderer>().sharedMaterial = charred;
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

    // Terrain edits are batched once per frame.
    void LateUpdate()
    {
        if (data == null) return;
        EaseHeaves();
        if (treesChanged)
        {
            data.treeInstances = trees;
            treesChanged = false;
        }
        if (scorch.Count == 0) return;

        int layers = data.alphamapLayers;
        var detailLayers = data.detailPrototypes.Length;
        foreach (var cell in scorch)
        {
            var world = grid.CellCenter(cell.x, cell.y) - terrain.transform.position;
            int ax = Mathf.Clamp(Mathf.RoundToInt(world.x / data.size.x * (data.alphamapWidth - 1)), 0, data.alphamapWidth - 1);
            int az = Mathf.Clamp(Mathf.RoundToInt(world.z / data.size.z * (data.alphamapHeight - 1)), 0, data.alphamapHeight - 1);
            var paint = new float[1, 1, layers];
            if (scorchedLayer < layers) paint[0, 0, scorchedLayer] = 1f;
            data.SetAlphamaps(ax, az, paint);

            int dx = Mathf.Clamp(Mathf.RoundToInt(world.x / data.size.x * (data.detailWidth - 1)), 0, data.detailWidth - 2);
            int dz = Mathf.Clamp(Mathf.RoundToInt(world.z / data.size.z * (data.detailHeight - 1)), 0, data.detailHeight - 2);
            var none = new int[2, 2];
            for (int k = 0; k < detailLayers; k++) data.SetDetailLayer(dx, dz, k, none);
        }
        scorch.Clear();
    }

    void OnDestroy()
    {
        if (charred != null) Destroy(charred);
        if (data != null) Destroy(data);
    }
}
