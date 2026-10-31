using System.Runtime.InteropServices;
using Chem;
using UnityEngine;

// The dragon's breath drawn: a wall of fire across the strip and a fan of it from the
// mouth, on the GPU (GPU-first, BACKLOG 26). It's Prototype/Flame's quads and flipbooks
// (FlameBank's), one draw from one buffer, no particles and no smoke: the perf pass
// found lit smoke was the expensive thing, and a wall of it would be the worst case.
// DragonView says where the wall stands each frame; the cells under it are the core's
// (World.BreathBox), and this only draws them. One light goes with it, kept on the
// part of the wall nearest the camera, as the wall is far longer than a light's reach.
public class DragonBreath : MonoBehaviour
{
    // The wall: columns across the strip, and rows behind the lead, each row lower as it dies down.
    const int Columns = 56, Rows = 4, Fan = 28, Embers = 28;
    const int Count = Columns * Rows + Fan + Embers;
    const float EmberRise = 34f, FadeSeconds = 0.7f;

    static readonly float[] RowBehind = { 0f, 4f, 9f, 16f }, RowTall = { 34f, 30f, 20f, 10f };
    static readonly int FlamesId = Shader.PropertyToID("_Flames"), TimeId = Shader.PropertyToID("_FlameTime");
    static readonly int[] SheetIds = { Shader.PropertyToID("_SheetTiny"), Shader.PropertyToID("_SheetMedium"), Shader.PropertyToID("_SheetLarge") };
    static readonly Unity.Profiling.ProfilerMarker PlaceMarker = new("DragonBreath.Place");

    readonly FlameBank.Flame[] flames = new FlameBank.Flame[Count];
    GraphicsBuffer buffer;
    Material material;
    RenderParams draw;
    ChemGrid grid;
    Light glow;
    AudioSource roar;
    float goneAt = -1f;
    bool burning;

    /// <summary>Whether the wall is out now, for the probe.</summary>
    public bool Burning => burning;

    /// <summary>A breath on a new child of <paramref name="view"/>, or null with no flames to draw with.</summary>
    public static DragonBreath On(Transform view, ChemGrid grid)
    {
        var shader = grid.flameShader != null ? grid.flameShader : Shader.Find("Prototype/Flame");
        if (shader == null || grid.flameSheets == null || grid.flameSheets.Length < SheetIds.Length) return null;
        var go = new GameObject("Dragon's breath");
        go.transform.SetParent(view, false);
        var breath = go.AddComponent<DragonBreath>();
        breath.Begin(grid, shader);
        return breath;
    }

    void Begin(ChemGrid on, Shader shader)
    {
        grid = on;
        buffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, Count, Marshal.SizeOf<FlameBank.Flame>());
        material = new Material(shader);
        for (int i = 0; i < SheetIds.Length; i++) material.SetTexture(SheetIds[i], grid.flameSheets[i]);
        material.SetBuffer(FlamesId, buffer);
        draw = new RenderParams(material)
        {
            worldBounds = new Bounds(Vector3.zero, Vector3.one * 100000f),
            shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off,
            receiveShadows = false,
        };
        for (int i = 0; i < Count; i++) Seed(i);
        glow = new GameObject("Glow").AddComponent<Light>();
        glow.transform.SetParent(transform, false);
        glow.type = LightType.Point;
        glow.color = new Color(1f, 0.5f, 0.18f);
        glow.intensity = 60f;
        glow.range = 130f;
        glow.shadows = LightShadows.None;
        glow.enabled = false;
        roar = GameSounds.Loop(SoundIds.FireLoop, glow.transform, GameVolume.Effects);
    }

    // What doesn't change as the wall moves: each flame's own time, look and sheet.
    void Seed(int i)
    {
        ref var f = ref flames[i];
        f.seed = ValueNoise.Hash(i, 7, 81);
        f.speed = Mathf.Lerp(0.3f, 0.5f, ValueNoise.Hash(i, 7, 82));
        f.tint = new Vector4(1f, 0.82f, 0.62f, 1f);
        f.sheet = i < Count - Embers ? 2f : -1f;
        f.wander = 4f;
    }

    /// <summary>
    /// The breath this frame: the wall on the line from <paramref name="end0"/> to <paramref name="end1"/> (world points,
    /// its height ignored), its rows trailing back along <paramref name="behind"/> (a unit), and the fan from the mouth.
    /// </summary>
    public void Burn(Vector3 end0, Vector3 end1, Vector3 behind, Vector3 mouth)
    {
        using var marked = PlaceMarker.Auto();
        if (!burning) Light(Time.time);
        for (int row = 0, i = 0; row < Rows; row++)
            for (int col = 0; col < Columns; col++, i++)
            {
                float u = (col + ValueNoise.Hash(col, row, 83)) / Columns;
                var at = Vector3.Lerp(end0, end1, u) + behind * (RowBehind[row] + 3f * ValueNoise.Hash(col, row, 84));
                Stand(ref flames[i], OnGround(at), RowTall[row] * Mathf.Lerp(0.8f, 1.2f, ValueNoise.Hash(col, row, 85)));
            }
        int fan = Columns * Rows;
        var middle = OnGround((end0 + end1) / 2f);
        for (int i = 0; i < Fan; i++)
        {
            float t = (i + 0.5f) / Fan, side = (ValueNoise.Hash(i, 3, 86) - 0.5f) * t * t;
            Stand(ref flames[fan + i], Vector3.Lerp(mouth, middle + (end1 - end0) * side, t) + Vector3.down * 4f, Mathf.Lerp(5f, 18f, t));
        }
        for (int i = 0; i < Embers; i++)
        {
            ref var e = ref flames[fan + Fan + i];
            e.at = OnGround(Vector3.Lerp(end0, end1, ValueNoise.Hash(i, 5, 87)) + behind * 12f);
            e.tall = EmberRise;
            e.wide = 0.25f;
        }
        buffer.SetData(flames);
        var eye = Camera.main;
        if (eye != null) glow.transform.position = NearestOnWall(end0, end1, eye.transform.position) + Vector3.up * 14f;
    }

    Vector3 OnGround(Vector3 at)
    {
        at.y = grid.GroundAt(at);
        if (grid.valley != null) at.y = Mathf.Max(at.y, grid.valley.lakeLevel);
        return at;
    }

    Vector3 NearestOnWall(Vector3 a, Vector3 b, Vector3 from)
    {
        var along = b - a;
        float t = Vector3.Dot(from - a, along) / Mathf.Max(1f, along.sqrMagnitude);
        return OnGround(a + along * Mathf.Clamp01(t));
    }

    static void Stand(ref FlameBank.Flame f, Vector3 at, float tall)
    {
        f.at = at;
        f.tall = tall;
        f.wide = tall * 0.55f;
    }

    void Light(float now)
    {
        burning = true;
        goneAt = -1f;
        glow.enabled = true;
        for (int i = 0; i < Count; i++)
        {
            flames[i].born = now;
            flames[i].gone = 0f;
        }
        if (roar.clip != null) roar.Play();
    }

    /// <summary>The breath stops: what's drawn dies away.</summary>
    public void Stop()
    {
        if (!burning) return;
        burning = false;
        goneAt = Time.time;
        for (int i = 0; i < Count; i++) flames[i].gone = goneAt;
        buffer.SetData(flames);
        glow.enabled = false;
        roar.Stop();
    }

    void LateUpdate()
    {
        if (!burning && (goneAt < 0f || Time.time - goneAt > FadeSeconds)) return;
        material.SetFloat(TimeId, Time.time);
        Graphics.RenderPrimitives(draw, MeshTopology.Triangles, 6, Count);
    }

    void OnDestroy()
    {
        buffer?.Release();
        buffer = null;
        if (material != null) Destroy(material);
    }
}
