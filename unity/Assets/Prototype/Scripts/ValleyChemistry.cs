using System.Collections.Generic;
using UnityEngine;

// Shows the chemistry of the cells outside the yard on the valley itself:
// where each cell's ground is, trees that burn down to charred stumps, burnt
// grass leaving scorched ground with its grass cleared, and the cabin
// charring and losing its roof once enough of its log walls have burned.
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

    void Init()
    {
        if (data != null || grid == null || grid.World == null || terrain == null) return;

        data = Instantiate(terrain.terrainData);
        terrain.terrainData = data;
        if (terrain.TryGetComponent<TerrainCollider>(out var collider)) collider.terrainData = data;

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
        foreach (var c in grid.World.Cells) if (c.T == "logwall") cabinCells++;
        charred = new Material(Shader.Find("Universal Render Pipeline/Lit")) { name = "Charred" };
        charred.SetColor("_BaseColor", new Color(0.07f, 0.06f, 0.055f));
        charred.SetFloat("_Smoothness", 0.05f);
    }

    /// <summary>The terrain's height at a cell's centre.</summary>
    public float GroundHeight(int x, int y)
    {
        Init();
        var p = grid.CellCenter(x, y);
        float h = terrain == null ? 0f : terrain.SampleHeight(p) + terrain.transform.position.y;
        var c = grid.World.At(x, y);
        return c != null && c.Water > 0 && c.T == "pit" ? Mathf.Max(h, lakeLevel) : h;
    }

    /// <summary>How high above the ground a cell's fire burns: in the crown of a tree, along a cabin wall.</summary>
    public static float FireHeight(string terrainType) => terrainType switch
    {
        "tree" => 2.6f,
        "logwall" => 1.4f,
        _ => 0f,
    };

    /// <summary>A cell outside the yard changed terrain (for example a tree burned to a stump).</summary>
    public void TerrainChanged(int x, int y, string from, string to)
    {
        Init();
        if (from == null || data == null) return;
        if (from == "tree") BurnTrees(x, y);
        if ((from == "grass" || from == "brush") && to == "dirt") scorch.Add(new Vector2Int(x, y));
        if (from == "logwall") CabinWallBurned();
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

        var stump = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        Destroy(stump.GetComponent<Collider>());
        stump.name = "Stump";
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
