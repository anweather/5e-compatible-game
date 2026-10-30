using System.Collections.Generic;
using Unity.Collections;
using UnityEngine;

// What the cells' shaders show on top of their surfaces, one texel a cell,
// in a small global texture (_CellFxTex) instead of extra objects
// (GPU-first, 2026-10-29): red, a lightning flash over charged water that
// fades in a moment (shock-water: the whole connected pond lights up at once);
// green, oil or grease floating on water, which the water draws as a dark,
// rainbow-sheened slick. Prototype/CellWater reads it at each point through
// _CellFxMap (world x, z to the cell) and _CellFxSize. Written as cells
// change (ChemGrid.RenderCharge, RenderCover) and sent to the GPU at most
// once a frame, only when something changed.
public sealed class CellFxTexture
{
    static readonly int TexId = Shader.PropertyToID("_CellFxTex"), MapId = Shader.PropertyToID("_CellFxMap"),
        SizeId = Shader.PropertyToID("_CellFxSize");
    // How long a flash takes to fade (s).
    const float FlashSeconds = 0.45f;

    readonly Texture2D tex;
    readonly int w;
    readonly List<int> flashing = new();
    readonly HashSet<int> flashSet = new();
    readonly float[] flash;
    bool dirty;

    /// <param name="map">World to cell: (1 / cell size, the x of world x = 0, the y of world z = 0, 0); a cell's y runs against z.</param>
    public CellFxTexture(int width, int height, Vector4 map)
    {
        w = width;
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
        dirty = true;
    }

    /// <summary>Once a frame: the flashes fade, and what changed goes to the GPU.</summary>
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
                if (flash[i] > 0f) continue;
                flashSet.Remove(i);
                flashing.RemoveAt(k);
            }
            dirty = true;
        }
        if (!dirty) return;
        dirty = false;
        tex.Apply(false);
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
    }
}
