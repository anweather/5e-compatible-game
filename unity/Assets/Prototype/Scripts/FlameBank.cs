using System.Collections.Generic;
using System.Runtime.InteropServices;
using Chem;
using UnityEngine;

// Fire on the cells, drawn on the GPU (issue #19: a field of the same flame,
// stamped out). Each burning cell holds a few tongues of flame, scattered over
// the cell and past its edge, each of its own size, speed and shape; a tongue is
// one instanced quad, and Prototype/Flame draws, sways and flickers it with no
// work here. This writes to the tongues' buffer only when a cell's fire changes,
// so a big fire costs what its pixels cost, not a particle system a cell. Where
// a tongue stands comes from the cell alone (ValueNoise), so a relit cell burns
// the same. Presentation only: what burns is the core's. ChemGrid.RenderFire
// feeds it; it lives on a child of the grid, made with the first fire.
public class FlameBank : MonoBehaviour
{
    /// <summary>Tongues a cell can hold; the last is a thread of embers on a big fire.</summary>
    public const int TonguesPerCell = 8;
    // Cells drawn at once: past this a cell's fire goes undrawn until it changes.
    const int MaxCells = 4000;
    // How long a fire that has gone out takes to die away (Prototype/Flame's fade), in seconds.
    const float FadeSeconds = 0.6f;
    const int Embers = TonguesPerCell - 1;

    /// <summary>One tongue, as Prototype/Flame reads it. A negative width marks embers.</summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct Tongue
    {
        public Vector3 at;
        public float seed;
        public float wide, tall, speed, born;
        public Vector3 tint;
        public float gone;
    }

    sealed class Burn
    {
        public int block, level;
        public float scale, ground;
        public string look;
    }

    static readonly int FlamesId = Shader.PropertyToID("_Flames"), TimeId = Shader.PropertyToID("_FlameTime");

    readonly Dictionary<int, Burn> burning = new();
    readonly Stack<int> freeBlocks = new();
    readonly Queue<(float until, int block)> dying = new();
    readonly Tongue[] tongues = new Tongue[MaxCells * TonguesPerCell];
    GraphicsBuffer buffer;
    Material material;
    RenderParams draw;
    float cellSize = 1.5f;
    int blocksUsed;

    /// <summary>A flame bank on a new child of <paramref name="grid"/>, or null with no shader.</summary>
    public static FlameBank On(ChemGrid grid)
    {
        var shader = grid.flameShader != null ? grid.flameShader : Shader.Find("Prototype/Flame");
        if (shader == null) return null;
        var go = new GameObject("Flames");
        go.transform.SetParent(grid.transform, false);
        var bank = go.AddComponent<FlameBank>();
        bank.cellSize = grid.cellSize;
        bank.Begin(shader);
        return bank;
    }

    void Begin(Shader shader)
    {
        buffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, tongues.Length, Marshal.SizeOf<Tongue>());
        buffer.SetData(tongues);
        material = new Material(shader);
        material.SetBuffer(FlamesId, buffer);
        draw = new RenderParams(material)
        {
            worldBounds = new Bounds(Vector3.zero, Vector3.one * 100000f),
            shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off,
            receiveShadows = false,
        };
    }

    /// <summary>How many cells' fires are drawn, for the probe.</summary>
    public int CellsBurning => burning.Count;

    /// <summary>The tongues of a burning cell that are drawn, for the probe.</summary>
    public IEnumerable<Tongue> TonguesOf(int index)
    {
        if (!burning.TryGetValue(index, out var burn)) yield break;
        for (int i = 0; i < TonguesPerCell; i++)
        {
            var t = tongues[burn.block * TonguesPerCell + i];
            if (t.tall > 0f) yield return t;
        }
    }

    /// <summary>The fire on cell (x, y) of index <paramref name="index"/>, standing at <paramref name="foot"/>: its level (0 puts it out), size and look (FireLook).</summary>
    public void Set(int index, int x, int y, Vector3 foot, int level, float scale, string look)
    {
        if (level <= 0)
        {
            PutOut(index);
            return;
        }
        if (!burning.TryGetValue(index, out var burn))
        {
            if (freeBlocks.Count == 0 && blocksUsed >= MaxCells) return;
            burn = new Burn { block = freeBlocks.Count > 0 ? freeBlocks.Pop() : blocksUsed++ };
            burning[index] = burn;
        }
        else if (burn.level == level && burn.look == look && Mathf.Approximately(burn.scale, scale) && Mathf.Approximately(burn.ground, foot.y))
        {
            return;
        }
        (burn.level, burn.scale, burn.ground, burn.look) = (level, scale, foot.y, look);
        Write(burn, x, y, foot);
    }

    void PutOut(int index)
    {
        if (!burning.Remove(index, out var burn)) return;
        int start = burn.block * TonguesPerCell;
        for (int i = 0; i < TonguesPerCell; i++) tongues[start + i].gone = Time.time;
        buffer.SetData(tongues, start, start, TonguesPerCell);
        dying.Enqueue((Time.time + FadeSeconds, burn.block));
    }

    // A cell's tongues: more and taller with its level, each scattered and sized from the cell alone.
    void Write(Burn burn, int x, int y, Vector3 center)
    {
        var (tall, wide, tint, speed, more) = FireLook.FlamesOf(burn.look);
        float height = burn.level switch { >= 3 => 1.9f, 2 => 1.25f, _ => 0.75f } * tall * burn.scale * (cellSize / 1.5f);
        int shown = Mathf.Min(Mathf.CeilToInt((1 + 2 * Mathf.Min(burn.level, 3)) * more), Embers);
        int start = burn.block * TonguesPerCell;
        var colour = new Vector3(tint.r, tint.g, tint.b);
        for (int i = 0; i < Embers; i++)
        {
            ref var t = ref tongues[start + i];
            float Hash(int k) => ValueNoise.Hash(x, y, i * 8 + k);
            float was = t.tall;
            float size = Mathf.Lerp(0.5f, 1.35f, Hash(3) * Mathf.Sqrt(Hash(6)));
            var along = new Vector3(Hash(1) - 0.5f, 0f, Hash(2) - 0.5f) * (cellSize * 1.05f);
            t.at = new Vector3(center.x + along.x, burn.ground, center.z + along.z);
            t.seed = Hash(4);
            t.tall = i < shown ? height * size : 0f;
            t.wide = height * size * wide * Mathf.Lerp(0.36f, 0.6f, Hash(7));
            t.speed = speed * Mathf.Lerp(0.7f, 1.35f, Hash(5));
            t.tint = colour;
            t.gone = 0f;
            if (was <= 0f) t.born = Time.time;
        }
        ref var embers = ref tongues[start + Embers];
        embers.at = new Vector3(center.x, burn.ground, center.z);
        embers.seed = ValueNoise.Hash(x, y, 90);
        embers.wide = -0.04f;
        embers.tall = burn.level >= 3 ? height * 1.8f : 0f;
        embers.speed = 0.5f;
        embers.tint = colour;
        embers.gone = 0f;
        buffer.SetData(tongues, start, start, TonguesPerCell);
    }

    void LateUpdate()
    {
        while (dying.Count > 0 && dying.Peek().until <= Time.time)
        {
            int block = dying.Dequeue().block, start = block * TonguesPerCell;
            System.Array.Clear(tongues, start, TonguesPerCell);
            buffer.SetData(tongues, start, start, TonguesPerCell);
            freeBlocks.Push(block);
        }
        if (burning.Count == 0 && dying.Count == 0) return;
        material.SetFloat(TimeId, Time.time);
        Graphics.RenderPrimitives(draw, MeshTopology.Triangles, 6, blocksUsed * TonguesPerCell);
    }

    void OnDestroy()
    {
        buffer?.Release();
        buffer = null;
        if (material != null) Destroy(material);
    }
}
