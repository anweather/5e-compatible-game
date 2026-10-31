using System.Collections.Generic;
using System.Runtime.InteropServices;
using Chem;
using UnityEngine;

// Fire on the cells, drawn on the GPU (issue #19: a field of the same flame,
// stamped out). A burning cell holds a few flames: each is one instanced quad
// playing the Particle Pack's flame flipbooks, the sheet by the fire's level as
// the pack's tiny, medium and large fires were, and the look by what burns
// (FireLook). Each stands off the cell's middle at its own size, speed and time,
// and Prototype/Flame moves and resizes it a little every time round. All of it is
// one draw, and this writes to the flames' buffer only when a cell's fire changes.
// Where a flame stands comes from the cell alone (ValueNoise), so a relit cell
// burns the same. Presentation only: what burns is the core's. ChemGrid.RenderFire
// feeds it; it lives on a child of the grid, made with the first fire.
public class FlameBank : MonoBehaviour
{
    /// <summary>Flames a cell can hold; the last two are threads of embers on a big fire.</summary>
    public const int FlamesPerCell = 8;
    // Cells drawn at once: past this a cell's fire goes undrawn until it changes.
    const int MaxCells = 4000;
    // How long a fire that has gone out takes to die away (Prototype/Flame's fade), in seconds.
    const float FadeSeconds = 0.6f;
    const int Sparks = 2, MostFlames = FlamesPerCell - Sparks;
    const float NoSheet = -1f;

    // By the fire's level, as the pack's three fires: its flipbook, how many flames stand on a cell at a
    // time, their size, how long one plays and how much of it shows.
    static readonly (int sheet, int count, float small, float big, float seconds, float alpha)[] Levels =
    {
        (0, 5, 0.6f, 0.95f, 2f, 0.7f),
        (1, 4, 1.1f, 2f, 3f, 1f),
        (2, 3, 1.8f, 3.2f, 4f, 1f),
    };

    /// <summary>One flame, as Prototype/Flame reads it. Sheet below zero marks embers.</summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct Flame
    {
        public Vector3 at;
        public float seed;
        public float wide, tall, speed, born;
        public Vector4 tint;
        public float gone, sheet, wander, pad;
    }

    sealed class Burn
    {
        public int block, level;
        public float scale, ground;
        public string look;
    }

    static readonly int FlamesId = Shader.PropertyToID("_Flames"), TimeId = Shader.PropertyToID("_FlameTime");
    static readonly int[] SheetIds = { Shader.PropertyToID("_SheetTiny"), Shader.PropertyToID("_SheetMedium"), Shader.PropertyToID("_SheetLarge") };

    readonly Dictionary<int, Burn> burning = new();
    readonly Stack<int> freeBlocks = new();
    readonly Queue<(float until, int block)> dying = new();
    readonly Flame[] flames = new Flame[MaxCells * FlamesPerCell];
    GraphicsBuffer buffer;
    Material material;
    RenderParams draw;
    float cellSize = 1.5f;
    int blocksUsed;

    /// <summary>A flame bank on a new child of <paramref name="grid"/>, or null with no shader.</summary>
    public static FlameBank On(ChemGrid grid)
    {
        var shader = grid.flameShader != null ? grid.flameShader : Shader.Find("Prototype/Flame");
        if (shader == null || grid.flameSheets == null || grid.flameSheets.Length < SheetIds.Length) return null;
        var go = new GameObject("Flames");
        go.transform.SetParent(grid.transform, false);
        var bank = go.AddComponent<FlameBank>();
        bank.cellSize = grid.cellSize;
        bank.Begin(shader, grid.flameSheets);
        return bank;
    }

    void Begin(Shader shader, Texture2D[] sheets)
    {
        buffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, flames.Length, Marshal.SizeOf<Flame>());
        buffer.SetData(flames);
        material = new Material(shader);
        for (int i = 0; i < SheetIds.Length; i++) material.SetTexture(SheetIds[i], sheets[i]);
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

    /// <summary>The flames and embers of a burning cell that are drawn, for the probe.</summary>
    public IEnumerable<Flame> FlamesOn(int index)
    {
        if (!burning.TryGetValue(index, out var burn)) yield break;
        for (int i = 0; i < FlamesPerCell; i++)
        {
            var f = flames[burn.block * FlamesPerCell + i];
            if (f.tall > 0f) yield return f;
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
        int start = burn.block * FlamesPerCell;
        for (int i = 0; i < FlamesPerCell; i++) flames[start + i].gone = Time.time;
        buffer.SetData(flames, start, start, FlamesPerCell);
        dying.Enqueue((Time.time + FadeSeconds, burn.block));
    }

    // A cell's flames: the level picks the flipbook, how many and how big, what burns picks the look, and
    // the cell alone scatters each flame about its middle and sizes it a little up or down.
    void Write(Burn burn, int x, int y, Vector3 center)
    {
        var (tall, wide, tint, speed, more) = FireLook.FlamesOf(burn.look);
        var (sheet, count, small, big, seconds, alpha) = Levels[Mathf.Clamp(burn.level, 1, Levels.Length) - 1];
        float scale = burn.scale * (cellSize / 1.5f) * Mathf.Lerp(0.8f, 1.2f, ValueNoise.Hash(x, y, 80));
        int shown = Mathf.Min(Mathf.CeilToInt(count * more), MostFlames);
        int start = burn.block * FlamesPerCell;
        var colour = new Vector4(tint.r, tint.g, tint.b, alpha);
        for (int i = 0; i < MostFlames; i++)
        {
            ref var f = ref flames[start + i];
            float Hash(int k) => ValueNoise.Hash(x, y, i * 8 + k);
            float was = f.tall;
            float size = Mathf.Lerp(small, big, Hash(3)) * scale;
            var along = new Vector3(Hash(1) - 0.5f, 0f, Hash(2) - 0.5f) * (cellSize * 0.95f);
            f.at = new Vector3(center.x + along.x, burn.ground, center.z + along.z);
            f.seed = Hash(4);
            f.tall = i < shown ? size * tall : 0f;
            f.wide = size * wide * 0.5f;
            f.speed = speed / seconds * Mathf.Lerp(0.75f, 1.3f, Hash(5));
            f.tint = colour;
            f.gone = 0f;
            f.sheet = sheet;
            f.wander = cellSize * 0.35f;
            if (was <= 0f) f.born = Time.time;
        }
        for (int i = MostFlames; i < FlamesPerCell; i++)
        {
            ref var ember = ref flames[start + i];
            ember.at = new Vector3(center.x, burn.ground + 0.2f, center.z);
            ember.seed = ValueNoise.Hash(x, y, 90 + i);
            ember.wide = 0.035f;
            ember.tall = burn.level >= Levels.Length ? big * scale * tall * 1.3f : 0f;
            ember.speed = 0.45f + 0.3f * ember.seed;
            ember.tint = colour;
            ember.gone = 0f;
            ember.sheet = NoSheet;
        }
        buffer.SetData(flames, start, start, FlamesPerCell);
    }

    void LateUpdate()
    {
        while (dying.Count > 0 && dying.Peek().until <= Time.time)
        {
            int block = dying.Dequeue().block, start = block * FlamesPerCell;
            System.Array.Clear(flames, start, FlamesPerCell);
            buffer.SetData(flames, start, start, FlamesPerCell);
            freeBlocks.Push(block);
        }
        if (burning.Count == 0 && dying.Count == 0) return;
        material.SetFloat(TimeId, Time.time);
        Graphics.RenderPrimitives(draw, MeshTopology.Triangles, 6, blocksUsed * FlamesPerCell);
    }

    void OnDestroy()
    {
        buffer?.Release();
        buffer = null;
        if (material != null) Destroy(material);
    }
}
