using System;
using System.Collections.Generic;
using System.Linq;
using Chem;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// Prototype > Export Valley Ground: the baked terrains of the yard scene as the valley's ground pack (GroundPack), the
// bake's last job (docs/design/maps-as-data.md). It says how far what it wrote is from the land, on every cell.
public static class GroundExporter
{
    const string Scene = "Assets/Prototype/Scenes/ChemistryYard.unity";

    [MenuItem("Prototype/Export Valley Ground")]
    public static void Export()
    {
        if (!Application.isBatchMode && !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
        EditorSceneManager.OpenScene(Scene);
        var terrains = UnityEngine.Object.FindObjectsByType<Terrain>(FindObjectsSortMode.None);
        var valley = UnityEngine.Object.FindFirstObjectByType<ValleyChemistry>();
        var layout = ValleyTerrain.Layout;
        var first = terrains[0].terrainData;
        var pack = new GroundPack
        {
            Bottom = terrains[0].transform.position.y,
            Top = terrains[0].transform.position.y + first.size.y,
            Layers = first.terrainLayers.Select(l => l.name).ToArray(),
            Grasses = first.detailPrototypes.Select(d => (d.prototype != null ? d.prototype : d.prototypeTexture as UnityEngine.Object)?.name ?? "").ToArray(),
            TreeKinds = first.treePrototypes.Select(t => t.prefab.name).ToArray(),
            ChannelCells = valley.channelCells,
            ChannelDepths = valley.channelDepths,
        };
        for (int j = 0; j < layout.ChunksZ; j++)
            for (int i = 0; i < layout.ChunksX; i++)
                pack.Chunks.Add(ChunkOf(terrains.First(t => t.name == $"Chunk {i},{j}").terrainData, i, j));
        pack.Save(ChemGrid.Folders.World);
        Debug.Log($"GROUND EXPORT: {pack.Chunks.Count} chunks, {pack.Chunks.Sum(c => c.Trees.Length)} trees, layers {string.Join(",", pack.Layers)}, grasses {pack.Grasses.Length}, bottom {pack.Bottom}, top {pack.Top}");
        Debug.Log("GROUND EXPORT: " + Farthest(terrains));
    }

    static GroundChunk ChunkOf(TerrainData data, int i, int j)
    {
        int res = data.heightmapResolution, texels = data.alphamapResolution, detail = data.detailResolution;
        var heights = data.GetHeights(0, 0, res, res);
        var alpha = data.GetAlphamaps(0, 0, texels, texels);
        int layers = data.alphamapLayers, grasses = data.detailPrototypes.Length;
        var chunk = new GroundChunk
        {
            I = i,
            J = j,
            Resolution = res,
            Texels = texels,
            DetailResolution = detail,
            Heights = new ushort[res * res],
            Paint = new byte[texels * texels * layers],
            Details = new byte[detail * detail * grasses],
            Trees = data.treeInstances.Select(Tree).ToArray(),
        };
        for (int z = 0; z < res; z++)
            for (int x = 0; x < res; x++) chunk.Heights[z * res + x] = (ushort)Mathf.RoundToInt(heights[z, x] * GroundPack.HeightSteps);
        for (int l = 0; l < layers; l++)
            for (int z = 0; z < texels; z++)
                for (int x = 0; x < texels; x++) chunk.Paint[(l * texels + z) * texels + x] = (byte)Mathf.RoundToInt(alpha[z, x, l] * 255f);
        for (int g = 0; g < grasses; g++)
        {
            var clumps = data.GetDetailLayer(0, 0, detail, detail, g);
            for (int z = 0; z < detail; z++)
                for (int x = 0; x < detail; x++) chunk.Details[(g * detail + z) * detail + x] = checked((byte)clumps[z, x]);
        }
        return chunk;
    }

    static GroundTree Tree(TreeInstance t) => new GroundTree
    {
        X = t.position.x,
        Y = t.position.y,
        Z = t.position.z,
        Width = t.widthScale,
        Height = t.heightScale,
        Rotation = t.rotation,
        Color = Packed(t.color),
        LightmapColor = Packed(t.lightmapColor),
        Prototype = t.prototypeIndex,
    };

    static uint Packed(Color32 c) => (uint)(c.r | c.g << 8 | c.b << 16 | c.a << 24);

    // How well the pack came through: each chunk's heights read back against the terrain.
    static string Farthest(Terrain[] terrains)
    {
        var pack = GroundPack.Load(ChemGrid.Folders.World);
        double worst = 0;
        foreach (var c in pack.Chunks)
        {
            var terrain = terrains.First(t => t.name == $"Chunk {c.I},{c.J}");
            var heights = terrain.terrainData.GetHeights(0, 0, c.Resolution, c.Resolution);
            for (int z = 0; z < c.Resolution; z++)
                for (int x = 0; x < c.Resolution; x++)
                    worst = Math.Max(worst, Math.Abs(pack.Metres(c, x, z) - (terrain.transform.position.y + heights[z, x] * terrain.terrainData.size.y)));
        }
        return $"heights read back within {worst:0.0000} m";
    }
}
