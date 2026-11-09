using Chem;
using UnityEngine;

// The map's ground while Build Chemistry Yard stands things on it (the court's rails, the bridge's feet, the rocks): its
// chunks built from the map's ground/ exactly as the game builds them (MapTerrains), for the length of the build, and gone
// before the scene is saved, which holds no ground (docs/design/maps-as-data.md, phase 6). It took over from
// ValleyTerrain, the bake whose last job was the ground pack.
public static class EditorGround
{
    /// <summary>Where the map's chunks stand (map.json): the valley's, the map the yard is built for.</summary>
    public static TerrainGrid Layout => MapLand.Current?.Layout ?? TerrainGrid.Valley();
    static GameObject root;
    static Terrain[] terrains;

    /// <summary>Builds the chunks from the map's ground/, with the template's prototypes and the terrains' material.</summary>
    public static void Open(TerrainData template, Material material)
    {
        Close();
        var builder = new GroundBuilder(GroundPack.Load(ChemGrid.Folders.World), template, Layout.ChunkSize);
        root = new GameObject("Ground while building");
        terrains = MapTerrains.Place(root.transform, Layout, builder, material);
    }

    /// <summary>Takes the chunks away again (before the scene is saved).</summary>
    public static void Close()
    {
        if (terrains != null)
            foreach (var t in terrains)
                if (t != null) Object.DestroyImmediate(t.terrainData);
        if (root != null) Object.DestroyImmediate(root);
        terrains = null;
        root = null;
    }

    /// <summary>The ground's height at a world point (the chunk under it, or the nearest one off the grid).</summary>
    public static float GroundAt(Vector3 p)
    {
        if (terrains == null) throw new System.InvalidOperationException("EditorGround.Open first: the ground is built only while the yard builds");
        float s = Layout.ChunkSize;
        int i = Mathf.Clamp(Mathf.FloorToInt((p.x - Layout.OriginX) / s), 0, Layout.ChunksX - 1);
        int j = Mathf.Clamp(Mathf.FloorToInt((p.z - Layout.OriginZ) / s), 0, Layout.ChunksZ - 1);
        var terrain = terrains[j * Layout.ChunksX + i];
        return terrain.SampleHeight(p) + terrain.transform.position.y;
    }
}
