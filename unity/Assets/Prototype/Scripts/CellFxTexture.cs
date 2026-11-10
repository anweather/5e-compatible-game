using System.Collections.Generic;
using Unity.Collections;
using UnityEngine;
using UnityEngine.Rendering;

// What the cells' shaders show on top of their surfaces, one texel a cell,
// in a small global texture (_CellFxTex) instead of extra objects
// (GPU-first, 2026-10-29): red, a lightning flash over charged water that
// fades in a moment (shock-water: the whole connected pond lights up at once);
// green, oil or grease floating on water, which the water draws as a dark,
// rainbow-sheened slick. Prototype/CellWater reads it at each point through
// _CellFxMap (world x, z to the cell) and _CellFxSize. Written as cells
// change (ChemGrid.RenderCharge, RenderCover) and sent to the GPU at most
// once a frame, only the blocks of BlockCells x BlockCells texels that changed
// (each copied in from a small staging texture): the whole texture is the map's
// size, ~12 MB on the nine, and a flash on one pond sent all of it every frame (#198).
public sealed class CellFxTexture
{
    static readonly int TexId = Shader.PropertyToID("_CellFxTex"), MapId = Shader.PropertyToID("_CellFxMap"),
        SizeId = Shader.PropertyToID("_CellFxSize");
    // How long a flash takes to fade (s).
    const float FlashSeconds = 0.45f;
    // A block's side in texels, and past how many changed blocks a frame the whole texture goes at once instead.
    const int BlockCells = 32, MostBlocks = 48;

    readonly Texture2D tex;
    readonly int w, h, blocksX;
    readonly List<int> flashing = new();
    readonly HashSet<int> flashSet = new();
    readonly float[] flash;
    // The blocks changed since the last upload, each once.
    readonly bool[] blockDirty;
    readonly List<int> dirtyBlocks = new();
    readonly List<Texture2D> staging = new();
    readonly bool canCopy = (SystemInfo.copyTextureSupport & CopyTextureSupport.Basic) != 0;

    /// <summary>How many texels the last upload sent, for a performance pass.</summary>
    public int TexelsSent { get; private set; }

    /// <param name="map">World to cell: (1 / cell size, the x of world x = 0, the y of world z = 0, 0); a cell's y runs against z.</param>
    public CellFxTexture(int width, int height, Vector4 map)
    {
        w = width;
        h = height;
        blocksX = (width + BlockCells - 1) / BlockCells;
        blockDirty = new bool[blocksX * ((height + BlockCells - 1) / BlockCells)];
        tex = new Texture2D(width, height, TextureFormat.RGBA32, false, true)
        {
            name = "Cell effects", wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear,
        };
        flash = new float[width * height];
        var data = tex.GetPixelData<Color32>(0);
        for (int i = 0; i < data.Length; i++) data[i] = default;
        tex.Apply(false);
        Shader.SetGlobalTexture(TexId, tex);
        Shader.SetGlobalVector(MapId, map);
        Shader.SetGlobalVector(SizeId, new Vector4(width, height, 1f / width, 1f / height));
    }

    /// <summary>Charged water at a cell: it flashes, and fades over a moment.</summary>
    public void Flash(int x, int y)
    {
        int i = y * w + x;
        flash[i] = 1f;
        if (flashSet.Add(i)) flashing.Add(i);
    }

    /// <summary>Whether a slick floats on the water at a cell.</summary>
    public void Oil(int x, int y, bool on)
    {
        var data = tex.GetPixelData<Color32>(0);
        int i = y * w + x;
        byte g = on ? (byte)255 : (byte)0;
        if (data[i].g == g) return;
        var p = data[i];
        p.g = g;
        data[i] = p;
        Dirty(i);
    }

    void Dirty(int i)
    {
        int block = i / w / BlockCells * blocksX + i % w / BlockCells;
        if (blockDirty[block]) return;
        blockDirty[block] = true;
        dirtyBlocks.Add(block);
    }

    /// <summary>Once a frame: the flashes fade, and the blocks that changed go to the GPU.</summary>
    public void Advance(float dt)
    {
        if (flashing.Count > 0)
        {
            var data = tex.GetPixelData<Color32>(0);
            for (int k = flashing.Count - 1; k >= 0; k--)
            {
                int i = flashing[k];
                flash[i] = Mathf.Max(0f, flash[i] - dt / FlashSeconds);
                Write(data, i, flash[i]);
                Dirty(i);
                if (flash[i] > 0f) continue;
                flashSet.Remove(i);
                flashing.RemoveAt(k);
            }
        }
        TexelsSent = 0;
        if (dirtyBlocks.Count == 0) return;
        if (!canCopy || dirtyBlocks.Count > MostBlocks)
        {
            tex.Apply(false);
            TexelsSent = w * h;
        }
        else
        {
            var data = tex.GetPixelData<Color32>(0);
            for (int k = 0; k < dirtyBlocks.Count; k++) Send(data, dirtyBlocks[k], k);
        }
        foreach (int block in dirtyBlocks) blockDirty[block] = false;
        dirtyBlocks.Clear();
    }

    // One block's texels into the k-th staging texture of this frame, and from there into the big one on the GPU.
    void Send(NativeArray<Color32> data, int block, int k)
    {
        int x0 = block % blocksX * BlockCells, y0 = block / blocksX * BlockCells;
        int bw = Mathf.Min(BlockCells, w - x0), bh = Mathf.Min(BlockCells, h - y0);
        if (k == staging.Count)
            staging.Add(new Texture2D(BlockCells, BlockCells, TextureFormat.RGBA32, false, true) { name = "Cell effects block" });
        var stage = staging[k];
        var texels = stage.GetPixelData<Color32>(0);
        for (int y = 0; y < bh; y++)
            NativeArray<Color32>.Copy(data, (y0 + y) * w + x0, texels, y * BlockCells, bw);
        stage.Apply(false);
        Graphics.CopyTexture(stage, 0, 0, 0, 0, bw, bh, tex, 0, 0, x0, y0);
        TexelsSent += bw * bh;
    }

    static void Write(NativeArray<Color32> data, int i, float amount)
    {
        var p = data[i];
        p.r = (byte)Mathf.RoundToInt(amount * 255f);
        data[i] = p;
    }

    public void Destroy()
    {
        if (tex != null) Object.Destroy(tex);
        foreach (var stage in staging) Object.Destroy(stage);
        staging.Clear();
    }
}
