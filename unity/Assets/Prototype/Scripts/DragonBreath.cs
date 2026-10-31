using System.Runtime.InteropServices;
using Chem;
using UnityEngine;

// The dragon's breath drawn: a wall of fire across the strip and a stream of it down from
// the mouth, on the GPU (GPU-first, BACKLOG 26). It's Prototype/Flame's quads and flipbooks
// (FlameBank's), one draw from one buffer, and no smoke: the perf pass found lit smoke was
// the expensive thing, and a wall of it would be the worst case. The stream's drops are
// GPU particles: each knows only where on the wall it lands, and Prototype/Flame works out
// where it is from the clock, the mouth and a bend, so nothing moves them here. It stops
// as the breath stops: what has left the mouth flies on, and nothing follows it.
// DragonView says where the wall stands each frame; the cells under it are the core's
// (World.BreathBox), and this only draws them. One light goes with it, kept on the
// part of the wall nearest the camera, as the wall is far longer than a light's reach.
public class DragonBreath : MonoBehaviour
{
    // The wall: columns across the strip, and rows behind the lead, each row lower as it dies down.
    const int Columns = 56, Rows = 4, Embers = 28, Drops = 240;
    // The stream's drops come last, so a draw can leave them out.
    const int FirstDrop = Columns * Rows + Embers, Count = FirstDrop + Drops;
    const float EmberRise = 34f;
    // Seconds a drop flies, at the longest; the wall's fade is shorter.
    const float LongestDrop = 0.85f, ShortestDrop = 0.6f, FadeSeconds = LongestDrop + 0.05f;
    // A drop's width where it lands, in metres; how far it strays off the line by then; how high on the wall it lands.
    const float DropWide = 12f, DropStray = 14f, LandsUp = 8f;
    // Prototype/Flame's mark for a drop.
    const float DropSheet = 3f;

    static readonly float[] RowBehind = { 0f, 4f, 9f, 16f }, RowTall = { 34f, 30f, 20f, 10f };
    static readonly int FlamesId = Shader.PropertyToID("_Flames"), TimeId = Shader.PropertyToID("_FlameTime");
    static readonly int MouthId = Shader.PropertyToID("_StreamMouth"), BendId = Shader.PropertyToID("_StreamBend");
    static readonly int[] SheetIds = { Shader.PropertyToID("_SheetTiny"), Shader.PropertyToID("_SheetMedium"), Shader.PropertyToID("_SheetLarge") };
    static readonly Unity.Profiling.ProfilerMarker PlaceMarker = new("DragonBreath.Place");

    readonly FlameBank.Flame[] flames = new FlameBank.Flame[Count];
    // The wall's flame each drop lands on.
    readonly int[] landsOn = new int[Drops];
    GraphicsBuffer buffer;
    Material material;
    RenderParams draw;
    ChemGrid grid;
    Light glow;
    AudioSource roar;
    float goneAt = -1f;
    bool burning;
    Vector3 mouthAt, bendAt, landsAt;

    /// <summary>Whether the wall is out now, for the probe.</summary>
    public bool Burning => burning;

    /// <summary>Whether drops are in the air: a moment longer than the wall burns.</summary>
    public bool StreamDrawn => burning || (goneAt >= 0f && Time.time - goneAt <= FadeSeconds);

    /// <summary>Leaves the stream out of the draw, for the probe to see what it adds.</summary>
    public bool StreamHidden { get; set; }

    /// <summary>The stream's line this frame, for the probe: <paramref name="s"/> is 0 at the mouth and 1 at the wall.</summary>
    public Vector3 StreamAt(float s) => Vector3.Lerp(Vector3.Lerp(mouthAt, bendAt, s), Vector3.Lerp(bendAt, landsAt, s), s);

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
        f.sheet = i < Columns * Rows ? 2f : -1f;
        f.wander = 4f;
        if (i < FirstDrop) return;
        f.speed = 1f / Mathf.Lerp(ShortestDrop, LongestDrop, ValueNoise.Hash(i, 7, 88));
        f.sheet = DropSheet;
        f.wide = DropWide * Mathf.Lerp(0.7f, 1.2f, ValueNoise.Hash(i, 7, 89));
        f.wander = DropStray;
        // Most of it falls near the middle of the strip; the sum of two makes that so, and it still reaches the ends.
        float across = (ValueNoise.Hash(i, 7, 90) + ValueNoise.Hash(i, 7, 91)) / 2f;
        landsOn[i - FirstDrop] = Mathf.Clamp((int)(across * Columns), 0, Columns - 1);
    }

    /// <summary>
    /// The breath this frame: the wall on the line from <paramref name="end0"/> to <paramref name="end1"/> (world points,
    /// its height ignored), its rows trailing back along <paramref name="behind"/> (a unit), and the stream from the mouth,
    /// as wide there as a dragon of <paramref name="scale"/>'s.
    /// </summary>
    public void Burn(Vector3 end0, Vector3 end1, Vector3 behind, Vector3 mouth, float scale)
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
        for (int i = 0; i < Embers; i++)
        {
            ref var e = ref flames[Columns * Rows + i];
            e.at = OnGround(Vector3.Lerp(end0, end1, ValueNoise.Hash(i, 5, 87)) + behind * 12f);
            e.tall = EmberRise;
            e.wide = 0.25f;
        }
        // Out of the mouth along its height, then down onto the wall.
        mouthAt = mouth;
        landsAt = OnGround((end0 + end1) / 2f) + Vector3.up * LandsUp;
        bendAt = Vector3.Lerp(mouth, landsAt, 0.5f);
        bendAt.y = mouth.y;
        for (int i = 0; i < Drops; i++) flames[FirstDrop + i].at = flames[landsOn[i]].at + Vector3.up * LandsUp;
        material.SetVector(MouthId, new Vector4(mouth.x, mouth.y, mouth.z, scale));
        material.SetVector(BendId, bendAt);
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
        if (!StreamDrawn) return;
        material.SetFloat(TimeId, Time.time);
        Graphics.RenderPrimitives(draw, MeshTopology.Triangles, 6, StreamHidden ? FirstDrop : Count);
    }

    void OnDestroy()
    {
        buffer?.Release();
        buffer = null;
        if (material != null) Destroy(material);
    }
}
