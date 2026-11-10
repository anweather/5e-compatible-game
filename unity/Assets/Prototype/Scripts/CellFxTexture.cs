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
// (each copied in from a small staging texture; #198).
//
// The texture is a window of at most Window x Window cells, not the map (#207, J-086: the nine's was 12 MB, the 12 x 10's
// would be 160 MB on each side): a cell's texel is its x and y modulo the window, and the texture repeats, so the shader
// reads it unchanged. A map that fits keeps a texel for every cell. On a bigger one a chunk's cells take a slot of the
// window the first time one shows an effect, and a chunk a window away that wanted the same slot gives its effects up
// (they are a moment's flash or a slick's sheen, only drawn near the players).
public sealed class CellFxTexture
{
    static readonly int TexId = Shader.PropertyToID("_CellFxTex"), MapId = Shader.PropertyToID("_CellFxMap"),
        SizeId = Shader.PropertyToID("_CellFxSize");
    // How long a flash takes to fade (s).
    const float FlashSeconds = 0.45f;
    // A block's side in texels, and past how many changed blocks a frame the whole texture goes at once instead.
    const int BlockCells = 32, MostBlocks = 48;

    /// <summary>The most cells the texture spans on a side: 4 MB, 16 chunks (the drawn tier is 11 at most round one player).</summary>
    public const int Window = 1024;

    readonly Texture2D tex;
    readonly int mapW, mapH, w, h, blocksX, slotsX, chunksX;
    readonly List<int> flashing = new();
    // Each flashing texel's brightness: only those flashing, never a float a cell.
    readonly Dictionary<int, float> flash = new();
    // The blocks changed since the last upload, each once.
    readonly bool[] blockDirty;
    readonly List<int> dirtyBlocks = new();
    readonly List<Texture2D> staging = new();
    // The chunk (its index) whose cells each slot of the window holds now, or -1: only when the window is smaller than the map.
    readonly int[] owner;
    readonly bool canCopy = (SystemInfo.copyTextureSupport & CopyTextureSupport.Basic) != 0;

    /// <summary>How many texels the last upload sent, for a performance pass.</summary>
    public int TexelsSent { get; private set; }

    /// <summary>The texture's size in texels (the window), for a check.</summary>
    public Vector2Int Size => new(w, h);

    /// <param name="map">World to cell: (1 / cell size, the x of world x = 0, the y of world z = 0, 0); a cell's y runs against z.</param>
    public CellFxTexture(int width, int height, Vector4 map, int window = Window)
    {
        mapW = width;
        mapH = height;
        w = Mathf.Min(width, window);
        h = Mathf.Min(height, window);
        blocksX = (w + BlockCells - 1) / BlockCells;
        blockDirty = new bool[blocksX * ((h + BlockCells - 1) / BlockCells)];
        slotsX = (w + Chem.Chunks.Size - 1) >> Chem.Chunks.Shift;
        chunksX = (width + Chem.Chunks.Size - 1) >> Chem.Chunks.Shift;
        bool whole = w == width && h == height;
        owner = whole ? null : new int[slotsX * ((h + Chem.Chunks.Size - 1) >> Chem.Chunks.Shift)];
        if (owner != null) System.Array.Fill(owner, -1);
        tex = new Texture2D(w, h, TextureFormat.RGBA32, false, true)
        {
            name = "Cell effects",
            wrapModeU = w == width ? TextureWrapMode.Clamp : TextureWrapMode.Repeat,
            wrapModeV = h == height ? TextureWrapMode.Clamp : TextureWrapMode.Repeat,
            filterMode = FilterMode.Bilinear,
        };
        var data = tex.GetPixelData<Color32>(0);
        for (int i = 0; i < data.Length; i++) data[i] = default;
        tex.Apply(false);
        Shader.SetGlobalTexture(TexId, tex);
        Shader.SetGlobalVector(MapId, map);
        Shader.SetGlobalVector(SizeId, new Vector4(w, h, 1f / w, 1f / h));
    }

    // A cell's texel in the window.
    int Texel(int x, int y) => y % h * w + x % w;

    // Whether the cell's chunk holds its slot of the window; with take, it takes it (the slot's last chunk's effects go).
    bool Holds(int x, int y, bool take)
    {
        if (owner == null) return true;
        int chunk = (y >> Chem.Chunks.Shift) * chunksX + (x >> Chem.Chunks.Shift);
        int slot = (y % h >> Chem.Chunks.Shift) * slotsX + (x % w >> Chem.Chunks.Shift);
        if (owner[slot] == chunk) return true;
        if (!take) return false;
        if (owner[slot] >= 0) ClearSlot(slot);
        owner[slot] = chunk;
        return true;
    }

    // A slot's texels back to nothing, its flashes ended.
    void ClearSlot(int slot)
    {
        int x0 = slot % slotsX << Chem.Chunks.Shift, y0 = slot / slotsX << Chem.Chunks.Shift;
        int x1 = Mathf.Min(w, x0 + Chem.Chunks.Size), y1 = Mathf.Min(h, y0 + Chem.Chunks.Size);
        var data = tex.GetPixelData<Color32>(0);
        for (int y = y0; y < y1; y++)
            for (int x = x0; x < x1; x++)
            {
                int i = y * w + x;
                if (data[i].r == 0 && data[i].g == 0) continue;
                data[i] = default;
                Dirty(i);
            }
        for (int k = flashing.Count - 1; k >= 0; k--)
        {
            int i = flashing[k], tx = i % w, ty = i / w;
            if (tx < x0 || tx >= x1 || ty < y0 || ty >= y1) continue;
            flash.Remove(i);
            flashing.RemoveAt(k);
        }
    }

    /// <summary>Charged water at a cell: it flashes, and fades over a moment.</summary>
    public void Flash(int x, int y)
    {
        if (x < 0 || y < 0 || x >= mapW || y >= mapH) return;
        Holds(x, y, true);
        int i = Texel(x, y);
        if (!flash.ContainsKey(i)) flashing.Add(i);
        flash[i] = 1f;
    }

    /// <summary>Whether a slick floats on the water at a cell.</summary>
    public void Oil(int x, int y, bool on)
    {
        if (x < 0 || y < 0 || x >= mapW || y >= mapH || !Holds(x, y, on)) return;
        var data = tex.GetPixelData<Color32>(0);
        int i = Texel(x, y);
        byte g = on ? (byte)255 : (byte)0;
        if (data[i].g == g) return;
        var p = data[i];
        p.g = g;
        data[i] = p;
        Dirty(i);
    }

    /// <summary>Whether the texture shows a slick at a cell now (for a check).</summary>
    public bool OilShown(int x, int y) => Holds(x, y, false) && tex.GetPixelData<Color32>(0)[Texel(x, y)].g > 0;

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
                float now = Mathf.Max(0f, flash[i] - dt / FlashSeconds);
                Write(data, i, now);
                Dirty(i);
                if (now > 0f)
                {
                    flash[i] = now;
                    continue;
                }
                flash.Remove(i);
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
