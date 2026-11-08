using Chem;
using UnityEngine;

// The chunk terrains, made when the scene starts from the map's folder (docs/design/maps-as-data.md, phase 6): the scene
// holds no ground of its own. With the map's ground/ (GroundSwitch on, the default) every chunk is built here from its
// arrays (GroundBuilder); in land mode, or for a map with no ground/, the chunks wait blank until the first world says
// what its land is (ValleyChemistry.Bind). What can't be data stays an asset: the template (the prototypes: layers,
// trees, grass) and the rock material, both made by Build Chemistry Yard. Early, so what reads the terrains as it
// starts (the sea's depth, the coast's life) finds them.
[DefaultExecutionOrder(-1000)]
public sealed class MapTerrains : MonoBehaviour
{
    [Tooltip("The prototypes every chunk is built with: terrain layers, trees, grass (Generated/Ground/Template.asset).")]
    public TerrainData template;
    [Tooltip("The terrains' material: URP's terrain with rock mapped from the sides (TerrainRock.shader).")]
    public Material material;

    readonly TerrainGrid layout = TerrainGrid.Valley();
    Terrain[] chunks;

    /// <summary>The builder the chunks were built with at the start; null when they wait for a world (land mode).</summary>
    public GroundBuilder Builder
    {
        get
        {
            if (chunks == null) Make();
            return builder;
        }
    }

    GroundBuilder builder;

    /// <summary>The chunk terrains, chunk (i, j) at j * ChunksX + i; made now if the scene hasn't started them yet.</summary>
    public Terrain[] Chunks
    {
        get
        {
            if (chunks == null) Make();
            return chunks;
        }
    }

    void Awake()
    {
        if (chunks == null) Make();
    }

    void Make()
    {
        var timer = System.Diagnostics.Stopwatch.StartNew();
        string dir = ChemGrid.Folders.World;
        builder = GroundSwitch.Mode == GroundSwitch.On && GroundPack.In(dir)
            ? new GroundBuilder(GroundPack.Load(dir), template, layout.ChunkSize)
            : null;
        chunks = Place(transform, layout, builder, material);
        Debug.Log($"GROUND TERRAINS {(builder != null ? "built" : "waiting for the land")}: {chunks.Length} chunks in {timer.Elapsed.TotalMilliseconds:0} ms");
    }

    /// <summary>
    /// The chunks as Terrains under <paramref name="parent"/>, each from <paramref name="builder"/>'s arrays (or blank,
    /// without one), standing where its step 0 is. Build Chemistry Yard uses the same to stand things on the ground.
    /// </summary>
    public static Terrain[] Place(Transform parent, TerrainGrid layout, GroundBuilder builder, Material material)
    {
        var terrains = new Terrain[layout.ChunksX * layout.ChunksZ];
        for (int j = 0; j < layout.ChunksZ; j++)
        {
            for (int i = 0; i < layout.ChunksX; i++)
            {
                var data = builder != null && builder.Has((i, j)) ? builder.Build((i, j)) : Blank(layout, i, j);
                var go = Terrain.CreateTerrainGameObject(data);
                go.name = $"Chunk {i},{j}";
                go.transform.SetParent(parent, false);
                var (cx, cz) = layout.ChunkCorner(i, j);
                go.transform.position = new Vector3(cx, builder != null ? builder.Bottom : 0f, cz);
                terrains[j * layout.ChunksX + i] = Configure(go.GetComponent<Terrain>(), layout.IsHighRes(i, j), material);
            }
        }
        Terrain.SetConnectivityDirty();
        return terrains;
    }

    static TerrainData Blank(TerrainGrid layout, int i, int j) => new TerrainData
    {
        name = $"Blank chunk {i},{j}",
        heightmapResolution = 33,
        size = new Vector3(layout.ChunkSize, 1f, layout.ChunkSize),
    };

    // Terrains connect to neighbours of the same group for LOD stitching, and Unity only stitches neighbours of the same
    // resolution, so full-detail and coarse chunks are two groups (their shared edges match by construction).
    static Terrain Configure(Terrain terrain, bool fullDetail, Material material)
    {
        terrain.treeDistance = 900f;
        terrain.treeBillboardDistance = 160f;
        terrain.detailObjectDistance = 120f;
        terrain.heightmapPixelError = 4f;
        terrain.drawInstanced = true;
        terrain.materialTemplate = material;
        terrain.allowAutoConnect = true;
        terrain.groupingID = fullDetail ? 1 : 0;
        return terrain;
    }
}
