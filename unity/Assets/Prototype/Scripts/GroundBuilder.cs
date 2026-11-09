using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Chem;
using UnityEngine;

// Arrays into TerrainData (docs/design/maps-as-data.md, section 1): the one way a chunk's ground is built, at run time
// (MapTerrains) and for the yard's build to stand things on. Two levels fill the same arrays, a map's ground/ (GroundPack) or its land and cells
// (GroundFromLand), read from the map's folder by path. What can't be data stays an asset: the prototypes (layers, trees,
// grass) come from one template TerrainData. It builds the terrain's data only: its material and its place in the scene
// are the Terrain's own, which is how TerrainRock carries over.
public sealed class GroundBuilder
{
    readonly GroundPack pack;
    readonly Dictionary<(int i, int j), GroundChunk> chunks;
    readonly TerrainData template;
    readonly float chunkSize;
    readonly int[] layerAt;

    public GroundBuilder(GroundPack pack, TerrainData template, float chunkSize)
    {
        this.pack = pack;
        this.template = template;
        this.chunkSize = chunkSize;
        chunks = pack.Chunks.ToDictionary(c => (c.I, c.J));
        var layers = template.terrainLayers;
        layerAt = pack.Layers.Select(name => Array.FindIndex(layers, l => l != null && l.name == name)).ToArray();
        int missing = Array.IndexOf(layerAt, -1);
        if (missing >= 0) throw new InvalidDataException($"The ground's layer {pack.Layers[missing]} isn't on the template terrain");
    }

    /// <summary>The height of the chunks' step 0 in metres: where the terrains stand.</summary>
    public float Bottom => (float)pack.Bottom;

    /// <summary>The river's cells and how far each is cut below its banks.</summary>
    public (int[] cells, float[] depths) Channel => (pack.ChannelCells, pack.ChannelDepths);

    /// <summary>Whether it has this chunk.</summary>
    public bool Has((int i, int j) at) => chunks.ContainsKey(at);

    /// <summary>A chunk's ground as new TerrainData, the arrays untouched.</summary>
    public TerrainData Build((int i, int j) at)
    {
        var c = chunks[at];
        var data = new TerrainData
        {
            name = $"Built chunk {at.i},{at.j}",
            heightmapResolution = c.Resolution,
            alphamapResolution = c.Texels,
            baseMapResolution = Mathf.Max(64, c.Texels),
        };
        data.SetDetailResolution(c.DetailResolution, template.detailResolutionPerPatch);
        data.SetDetailScatterMode(template.detailScatterMode);
        data.size = new Vector3(chunkSize, (float)(pack.Top - pack.Bottom), chunkSize);
        data.wavingGrassAmount = template.wavingGrassAmount;
        data.wavingGrassSpeed = template.wavingGrassSpeed;
        data.wavingGrassStrength = template.wavingGrassStrength;
        data.wavingGrassTint = template.wavingGrassTint;
        data.terrainLayers = template.terrainLayers;
        data.detailPrototypes = template.detailPrototypes;
        data.treePrototypes = template.treePrototypes;
        SetHeights(data, c);
        SetPaint(data, c);
        SetDetails(data, c);
        data.SetTreeInstances(c.Trees.Select(Tree).ToArray(), false);
        return data;
    }

    static void SetHeights(TerrainData data, GroundChunk c)
    {
        int res = c.Resolution;
        var heights = new float[res, res];
        for (int z = 0; z < res; z++)
            for (int x = 0; x < res; x++) heights[z, x] = c.Heights[z * res + x] / (float)GroundPack.HeightSteps;
        data.SetHeights(0, 0, heights);
    }

    // Straight into the paint's own textures, as ValleyGround's paint is: SetAlphamaps also redraws the base map, about 20 ms
    // a call, and nothing in a chunk is far enough off to draw it.
    void SetPaint(TerrainData data, GroundChunk c)
    {
        int texels = c.Texels, area = texels * texels;
        var textures = data.alphamapTextures;
        for (int t = 0; t < textures.Length; t++)
        {
            var pixels = textures[t].GetPixelData<Color32>(0);
            var texel = new byte[4];
            for (int n = 0; n < area; n++)
            {
                Array.Clear(texel, 0, 4);
                for (int l = 0; l < layerAt.Length; l++)
                    if (layerAt[l] / 4 == t) texel[layerAt[l] % 4] = c.Paint[l * area + n];
                pixels[n] = new Color32(texel[0], texel[1], texel[2], texel[3]);
            }
            textures[t].Apply(true);
        }
    }

    void SetDetails(TerrainData data, GroundChunk c)
    {
        for (int g = 0; g < pack.Grasses.Length; g++)
        {
            var map = DetailLayer(c, g, 0, 0, c.DetailResolution, c.DetailResolution);
            if (map.Cast<int>().Any(n => n != 0)) data.SetDetailLayer(0, 0, g, map);
        }
    }

    /// <summary>A box of a chunk's grass as built, [z, x]: what Plant Growth grows back.</summary>
    public int[,] DetailLayer((int i, int j) at, int layer, int x0, int z0, int w, int h) => DetailLayer(chunks[at], layer, x0, z0, w, h);

    static int[,] DetailLayer(GroundChunk c, int layer, int x0, int z0, int w, int h)
    {
        int res = c.DetailResolution;
        var map = new int[h, w];
        for (int z = 0; z < h; z++)
            for (int x = 0; x < w; x++) map[z, x] = c.Details[(layer * res + z0 + z) * res + x0 + x];
        return map;
    }

    static TreeInstance Tree(GroundTree t) => new TreeInstance
    {
        position = new Vector3(t.X, t.Y, t.Z),
        widthScale = t.Width,
        heightScale = t.Height,
        rotation = t.Rotation,
        color = Unpacked(t.Color),
        lightmapColor = Unpacked(t.LightmapColor),
        prototypeIndex = t.Prototype,
    };

    static Color32 Unpacked(uint c) => new Color32((byte)c, (byte)(c >> 8), (byte)(c >> 16), (byte)(c >> 24));
}
