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
// are the Terrain's own, which is how TerrainRock carries over. A generated map's arrays are made a chunk at a time, when
// its terrain is (#205, J-086): the builder holds how to make one, and the chunks made so far.
public sealed class GroundBuilder
{
    readonly GroundPack pack;
    readonly Dictionary<(int i, int j), GroundChunk> chunks;
    readonly Func<(int i, int j), GroundChunk> make;
    readonly Func<(int i, int j), bool> has;
    readonly TerrainData template;
    readonly float chunkSize;
    readonly int[] layerAt;

    public GroundBuilder(GroundPack pack, TerrainData template, float chunkSize)
        : this(pack, template, chunkSize, null, null)
    {
    }

    /// <summary>
    /// A builder whose arrays are made a chunk at a time by <paramref name="make"/>, for the chunks <paramref name="has"/>
    /// says: <paramref name="pack"/> holds what the chunks share (the heights' range, the names, the river), not the chunks.
    /// </summary>
    public GroundBuilder(GroundPack pack, TerrainData template, float chunkSize, Func<(int i, int j), GroundChunk> make, Func<(int i, int j), bool> has)
    {
        this.pack = pack;
        this.template = template;
        this.chunkSize = chunkSize;
        this.make = make;
        this.has = has;
        chunks = pack.Chunks.ToDictionary(c => (c.I, c.J));
        var layers = template.terrainLayers;
        layerAt = pack.Layers.Select(name => Array.FindIndex(layers, l => l != null && l.name == name)).ToArray();
        int missing = Array.IndexOf(layerAt, -1);
        if (missing >= 0) throw new InvalidDataException($"The ground's layer {pack.Layers[missing]} isn't on the template terrain");
    }

    /// <summary>The height of the chunks' step 0 in metres: where the terrains stand.</summary>
    public float Bottom => (float)pack.Bottom;

    /// <summary>The tree kinds every chunk is built with (the template's).</summary>
    public TreePrototype[] TreePrototypes => template.treePrototypes;

    /// <summary>The river's cells and how far each is cut below its banks.</summary>
    public (int[] cells, float[] depths) Channel => (pack.ChannelCells, pack.ChannelDepths);

    /// <summary>Whether it has this chunk.</summary>
    public bool Has((int i, int j) at) => make != null ? has(at) : chunks.ContainsKey(at);

    /// <summary>Whether its chunks are made one at a time (a generated map's), not read whole.</summary>
    public bool Lazy => make != null;

    /// <summary>How many chunks' arrays it holds now.</summary>
    public int Held => chunks.Count;

    // A chunk's arrays, made now if they haven't been.
    GroundChunk Chunk((int i, int j) at)
    {
        if (chunks.TryGetValue(at, out var c)) return c;
        if (make == null) throw new KeyNotFoundException($"The ground has no chunk ({at.i}, {at.j})");
        chunks[at] = c = make(at);
        return c;
    }

    /// <summary>
    /// Makes the arrays of those of <paramref name="wanted"/> not made yet, side by side (each reads only what nothing
    /// changes while this runs, and writes only its own chunk), added in order after. Nothing for a pack read whole.
    /// </summary>
    public void Prepare(IReadOnlyList<(int i, int j)> wanted)
    {
        if (make == null) return;
        var missing = wanted.Where(at => !chunks.ContainsKey(at) && has(at)).Distinct().ToList();
        if (missing.Count == 0) return;
        var made = new GroundChunk[missing.Count];
        if (missing.Count == 1) made[0] = make(missing[0]);
        else System.Threading.Tasks.Parallel.For(0, missing.Count, n => made[n] = make(missing[n]));
        for (int n = 0; n < missing.Count; n++) chunks[missing[n]] = made[n];
    }

    /// <summary>Whether this chunk's arrays are made.</summary>
    public bool Holds((int i, int j) at) => chunks.ContainsKey(at);

    /// <summary>Drops a chunk's arrays made one at a time (its terrain went with nothing changed): made again when wanted again.</summary>
    public void Forget((int i, int j) at)
    {
        if (make != null) chunks.Remove(at);
    }

    /// <summary>A chunk's ground as new TerrainData, the arrays untouched.</summary>
    public TerrainData Build((int i, int j) at) => Build(at, null);

    /// <summary>
    /// A chunk's ground into <paramref name="reuse"/> when one is given (an earlier chunk's, no longer drawn: the terrains
    /// are pooled, #205), or into new TerrainData.
    /// </summary>
    public TerrainData Build((int i, int j) at, TerrainData reuse)
    {
        var c = Chunk(at);
        bool fresh = reuse == null;
        var data = fresh ? new TerrainData() : reuse;
        data.name = $"Built chunk {at.i},{at.j}";
        if (fresh || data.heightmapResolution != c.Resolution) data.heightmapResolution = c.Resolution;
        if (fresh || data.alphamapResolution != c.Texels) data.alphamapResolution = c.Texels;
        if (fresh || data.baseMapResolution != Mathf.Max(64, c.Texels)) data.baseMapResolution = Mathf.Max(64, c.Texels);
        if (fresh || data.detailResolution != c.DetailResolution) data.SetDetailResolution(c.DetailResolution, template.detailResolutionPerPatch);
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
        SetDetails(data, c, fresh);
        data.SetTreeInstances(c.Trees.Select(Tree).ToArray(), false);
        return data;
    }

    /// <summary>
    /// A chunk built here, taken again by a new world as it is: its layers back to the template's (WetGround gave it copies
    /// to wet, which go with the old world).
    /// </summary>
    public void FreshLayers(TerrainData data) => data.terrainLayers = template.terrainLayers;

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

    // New data has no grass to clear; reused data has the last chunk's, so every kind is written.
    void SetDetails(TerrainData data, GroundChunk c, bool fresh)
    {
        for (int g = 0; g < pack.Grasses.Length; g++)
        {
            var map = DetailLayer(c, g, 0, 0, c.DetailResolution, c.DetailResolution);
            if (!fresh || map.Cast<int>().Any(n => n != 0)) data.SetDetailLayer(0, 0, g, map);
        }
    }

    /// <summary>A box of a chunk's grass as built, [z, x]: what Plant Growth grows back.</summary>
    public int[,] DetailLayer((int i, int j) at, int layer, int x0, int z0, int w, int h) => DetailLayer(Chunk(at), layer, x0, z0, w, h);

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
