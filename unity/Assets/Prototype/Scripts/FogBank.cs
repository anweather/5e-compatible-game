using System.Collections.Generic;
using UnityEngine;

// Fog Cloud's fog, dust and the clouds, drawn as soft billboards: a few big,
// faint puffs stacked over each square of the gas, turned to the camera,
// drifting and breathing a little, thicker where the gas is thicker, fading in
// and out as it comes and goes. Overlapping, they read as a bank of volumetric
// gas rather than as cards. Lit, so it greys at dusk and darkens at night. Each
// gas has its own Style: fog lies low and pale, dust is tan and close to the
// ground, a rain cloud hangs grey overhead and lets rain fall from it, a storm
// cloud is near black and rains harder, a snow cloud is pale and lets flakes drift
// down (one reused particle system each). Presentation only: where the gas is, and what it
// hides, is the core's (World.Fogged, World.LineOfSight). ChemGrid.RenderGas
// feeds it; it lives on a child of the grid, made when the first gas comes.
public class FogBank : MonoBehaviour
{
    // Puffs over each square, and how high they stack, in squares.
    const int PuffsPerSquare = 3;
    // Drops a second from each square of rain cloud, and how fast they fall (m/s).
    const float RainRate = 12f, RainSpeed = 11f;
    const float SnowSpeed = 1.6f;
    /// <summary>How high a cloud hangs over its ground, in squares.</summary>
    public const float CloudHeight = 2.6f;

    /// <summary>How a gas looks: its colour, how far up it starts and how high it stacks (squares), a puff's size across (squares), its opacity over the thickest of it, and what falls from it.</summary>
    readonly struct Style
    {
        public readonly Color Tint;
        public readonly float Lift, Rise, Size, Opacity;
        public readonly bool Rains, Snows;

        public Style(Color tint, float lift, float rise, float size, float opacity, bool rains = false, bool snows = false)
        {
            Tint = tint;
            Lift = lift;
            Rise = rise;
            Size = size;
            Opacity = opacity;
            Rains = rains;
            Snows = snows;
        }
    }

    static readonly Style FogStyle = new(new Color(0.86f, 0.88f, 0.9f, 1f), 0.45f, 0.75f, 2.8f, 0.42f);
    static readonly Style DustStyle = new(new Color(0.72f, 0.58f, 0.38f, 1f), 0.3f, 0.5f, 2.2f, 0.6f);
    static readonly Style CloudStyle = new(new Color(0.46f, 0.48f, 0.54f, 1f), CloudHeight, 0.25f, 3.2f, 0.75f, rains: true);
    static readonly Style StormStyle = new(new Color(0.13f, 0.14f, 0.2f, 1f), CloudHeight, 0.3f, 3.5f, 0.92f, rains: true);
    static readonly Style SnowCloudStyle = new(new Color(0.9f, 0.93f, 0.98f, 1f), CloudHeight, 0.25f, 3.2f, 0.8f, snows: true);

    /// <summary>Whether a gas is drawn here rather than as a particle effect.</summary>
    public static bool Draws(string gas) => gas == GasIds.Fog || gas == GasIds.Dust || gas == GasIds.Cloud || gas == GasIds.Storm || gas == GasIds.SnowCloud;

    static Style StyleOf(string gas) => gas switch
    {
        GasIds.Dust => DustStyle,
        GasIds.Cloud => CloudStyle,
        GasIds.Storm => StormStyle,
        GasIds.SnowCloud => SnowCloudStyle,
        _ => FogStyle,
    };

    // How fast a square's fog fades in or out (opacity a second), and how far a puff wanders (in squares).
    const float FadeRate = 0.8f, Wander = 0.35f;

    sealed class Square
    {
        public Vector3 home;
        public float target, shown, seed;
        public Style style;
        public readonly List<Renderer> puffs = new();
    }

    readonly Dictionary<int, Square> squares = new();
    readonly Stack<Square> free = new();
    readonly List<int> gone = new();
    MaterialPropertyBlock block;
    Material material;
    Texture2D texture;
    float cellSize = 1.5f;
    ParticleSystem rain, snow;
    float rainOwed;

    /// <summary>A fog bank on a new child of <paramref name="grid"/>.</summary>
    public static FogBank On(ChemGrid grid)
    {
        var go = new GameObject("Fog");
        go.transform.SetParent(grid.transform, false);
        var bank = go.AddComponent<FogBank>();
        bank.cellSize = grid.cellSize;
        return bank;
    }

    /// <summary>How many squares of fog are drawn (fading ones too), for the probe.</summary>
    public int SquaresShown => squares.Count;

    /// <summary>
    /// The gas over square <paramref name="index"/> standing on <paramref name="ground"/> at
    /// <paramref name="center"/>: <paramref name="amount"/> units out of <paramref name="max"/> of
    /// <paramref name="gas"/>; 0 fades it out.
    /// </summary>
    public void Set(int index, Vector3 center, float ground, int amount, int max, string gas)
    {
        float target = max > 0 ? Mathf.Sqrt(Mathf.Clamp01(amount / (float)max)) : 0f;
        if (!squares.TryGetValue(index, out var s))
        {
            if (target <= 0f) return;
            s = Take();
            s.shown = 0f;
            s.seed = Mathf.Abs(index * 0.6180339f % 1f) * 100f;
            squares[index] = s;
        }
        s.home = new Vector3(center.x, ground, center.z);
        s.target = target;
        if (target > 0f) s.style = StyleOf(gas);
    }

    /// <summary>The gas over a square is gone at once: burnt in a flash, not fading.</summary>
    public void Vanish(int index)
    {
        if (!squares.TryGetValue(index, out var s)) return;
        s.shown = 0f;
        s.target = 0f;
    }

    void LateUpdate()
    {
        var cam = Camera.main;
        if (cam == null || squares.Count == 0) return;
        var turn = cam.transform.rotation;
        float t = Time.time, step = Time.deltaTime * FadeRate;
        gone.Clear();
        rainOwed += RainRate * Time.deltaTime;
        int drops = Mathf.FloorToInt(rainOwed);
        rainOwed -= drops;
        foreach (var pair in squares)
        {
            var s = pair.Value;
            s.shown = Mathf.MoveTowards(s.shown, s.target, step);
            if (s.shown <= 0f && s.target <= 0f)
            {
                gone.Add(pair.Key);
                continue;
            }
            Place(s, turn, t);
            if (s.style.Rains) Rain(s, drops);
            if (s.style.Snows) Snow(s, drops);
        }
        foreach (int index in gone)
        {
            var s = squares[index];
            squares.Remove(index);
            foreach (var r in s.puffs) r.gameObject.SetActive(false);
            free.Push(s);
        }
    }

    // A square's puffs, stacked and drifting about its centre, faced to the camera.
    void Place(Square s, Quaternion turn, float t)
    {
        var style = s.style;
        var color = style.Tint;
        color.a = style.Opacity * s.shown;
        for (int i = 0; i < s.puffs.Count; i++)
        {
            var r = s.puffs[i];
            float phase = s.seed + i * 2.1f;
            var drift = new Vector3(Mathf.Sin(t * 0.13f + phase), 0f, Mathf.Cos(t * 0.11f + phase * 1.3f)) * (Wander * cellSize);
            float breathe = 1f + 0.12f * Mathf.Sin(t * 0.3f + phase);
            r.transform.SetPositionAndRotation(s.home + drift + Vector3.up * ((style.Lift + i * style.Rise) * cellSize), turn * Quaternion.Euler(0f, 0f, phase * 40f));
            r.transform.localScale = Vector3.one * (style.Size * cellSize * breathe);
            UrpMaterials.SetColor(r, block, color);
        }
    }

    // Drops from under a square of rain cloud, gone when they reach its ground.
    void Rain(Square s, int drops)
    {
        if (drops <= 0 || s.target <= 0f) return;
        if (rain == null) rain = MakeRain();
        float height = s.style.Lift * cellSize;
        var drop = new ParticleSystem.EmitParams { velocity = Vector3.down * RainSpeed, startLifetime = height / RainSpeed };
        for (int i = 0; i < drops; i++)
        {
            drop.position = s.home + new Vector3(Random.Range(-0.7f, 0.7f) * cellSize, height, Random.Range(-0.7f, 0.7f) * cellSize);
            rain.Emit(drop, 1);
        }
    }

    // Flakes drifting down from under a square of snow cloud.
    void Snow(Square s, int flakes)
    {
        if (flakes <= 0 || s.target <= 0f) return;
        if (snow == null) snow = MakeSnow();
        float height = s.style.Lift * cellSize;
        var flake = new ParticleSystem.EmitParams { startLifetime = height / SnowSpeed };
        for (int i = 0; i < flakes; i++)
        {
            flake.position = s.home + new Vector3(Random.Range(-0.7f, 0.7f) * cellSize, height, Random.Range(-0.7f, 0.7f) * cellSize);
            flake.velocity = new Vector3(Random.Range(-0.3f, 0.3f), -SnowSpeed, Random.Range(-0.3f, 0.3f));
            snow.Emit(flake, 1);
        }
    }

    ParticleSystem MakeSnow()
    {
        var ps = MakeDrops("Snow flakes", 0.07f, new Color(1f, 1f, 1f, 0.9f));
        ps.GetComponent<ParticleSystemRenderer>().renderMode = ParticleSystemRenderMode.Billboard;
        return ps;
    }

    ParticleSystem MakeRain()
    {
        var ps = MakeDrops("Rain", 0.022f, new Color(0.75f, 0.82f, 0.92f, 0.4f));
        var look = ps.GetComponent<ParticleSystemRenderer>();
        look.renderMode = ParticleSystemRenderMode.Stretch;
        look.velocityScale = 0.06f;
        look.lengthScale = 1f;
        return ps;
    }

    ParticleSystem MakeDrops(string name, float size, Color color)
    {
        var go = new GameObject(name);
        go.transform.SetParent(transform, false);
        var ps = go.AddComponent<ParticleSystem>();
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        var main = ps.main;
        main.loop = false;
        main.playOnAwake = false;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.startSize = size;
        main.startColor = color;
        main.maxParticles = 900;
        var emission = ps.emission;
        emission.enabled = false;
        var look = go.GetComponent<ParticleSystemRenderer>();
        look.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        look.sharedMaterial = UrpMaterials.NewUnlitParticles(name, Color.white, additive: false);
        return ps;
    }

    Square Take()
    {
        if (free.Count > 0)
        {
            var s = free.Pop();
            foreach (var r in s.puffs) r.gameObject.SetActive(true);
            return s;
        }
        EnsureMaterial();
        var made = new Square();
        for (int i = 0; i < PuffsPerSquare; i++)
        {
            var quad = Primitives.Visual(PrimitiveType.Quad, "Fog puff");
            quad.transform.SetParent(transform, false);
            var r = quad.GetComponent<Renderer>();
            r.sharedMaterial = material;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
            made.puffs.Add(r);
        }
        return made;
    }

    void EnsureMaterial()
    {
        if (material != null) return;
        block = new MaterialPropertyBlock();
        texture = ProceduralTextures.SoftPuff(64);
        material = UrpMaterials.NewLit("Fog", Color.white, 0f);
        UrpMaterials.MakeTransparent(material);
        material.SetTexture("_BaseMap", texture);
        // Both faces, so a puff the camera ends up behind still shows.
        material.SetFloat("_Cull", 0f);
    }

    void OnDestroy()
    {
        if (material != null) Destroy(material);
        if (texture != null) Destroy(texture);
    }
}
