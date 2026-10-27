using Chem;
using UnityEngine;

// A cell's fire, gas and charge, drawn with the Particle Pack's effects when
// they are assigned and with simple cubes when they aren't. Particle effects
// are retired (they stop emitting and fade) rather than popped out. Fire
// and smoke effects are pooled (made once, reused as they come and go), fires keep their
// effect as the fire grows or dies down (only its size changes), and thin out
// when much burns: a field of flames overlaps, and hundreds of particle
// systems cost more than they add (FireDensity). New effects are made with
// Prewarm off and at most a few a frame (the rest wait a frame or two), so a
// burst of smoke never stalls a frame making and prewarming dozens at once.
public partial class ChemGrid
{
    // --- fire ----------------------------------------------------------------------

    // Burning cells (in the simulated chunks) past which only some cells draw flames.
    const int ThinFiresAt = 200, SparseFiresAt = 500;
    // How long a retired fire or smoke effect fades before it is reused.
    const float FxFadeSeconds = 1.5f;
    // Fire and smoke effects alive at once (fading ones too): past this a cell's fire or smoke
    // goes undrawn until it changes, rather than making ever more particle systems.
    const int MaxLiveFx = 320;
    int liveFx;

    readonly System.Collections.Generic.Dictionary<GameObject, System.Collections.Generic.Stack<GameObject>> freeFx = new();
    readonly System.Collections.Generic.Dictionary<GameObject, GameObject> fxPrefab = new();
    readonly System.Collections.Generic.List<(GameObject go, float until)> fadingFx = new();

    void RenderFire(View v, Cell c, int x, int y, Vector3 center, float ground, float scale = 1f)
    {
        var prefab = FirePrefab(c.Fire);
        bool shown = c.Fire > 0 && prefab != null && FlameDrawnAt(x, y);
        if (!shown)
        {
            if (v.fireLevel != 0) { RetireFx(v.fire); v.fire = null; v.fireLevel = 0; }
            if (c.Fire > 0 && prefab == null) FallbackFire(v, c, center, ground);
            return;
        }
        var (offset, turn, size) = FlameSpot(x, y);
        // A fire that grows or dies down keeps its effect; it only changes size.
        if (v.fire == null) v.fire = TakeFx(prefab, new Vector3(center.x + offset.x, ground, center.z + offset.y), Quaternion.Euler(0f, turn, 0f), MaxLiveFx, out _);
        if (v.fire == null) return; // over the effects budget: drawn when it next changes
        v.fire.transform.localScale = Vector3.one * (cellSize / 1.5f * scale * size * FlameScale(c.Fire));
        v.fireLevel = c.Fire;
    }

    static float FlameScale(int fire) => fire switch { >= 3 => 1.25f, 2 => 1f, _ => 0.8f };

    // Gas drifts over many cells at once: its effects go on every other cell, and one in four when much burns.
    bool GasDrawnAt(int x, int y)
    {
        int burning = fireLights != null ? fireLights.BurningCount : 0;
        return burning < ThinFiresAt ? ((x + y) & 1) == 0 : (x & 1) == 0 && (y & 1) == 0;
    }

    // With much burning, flames on every other cell, then one in four (the same cells each time).
    bool FlameDrawnAt(int x, int y)
    {
        int burning = fireLights != null ? fireLights.BurningCount : 0;
        if (burning < ThinFiresAt) return true;
        if (burning < SparseFiresAt) return ((x + y) & 1) == 0;
        return (x & 1) == 0 && (y & 1) == 0;
    }

    // New effects made in one frame, past which the rest wait for the next frames (the cells ask
    // again each frame): a burst of smoke or flames would otherwise make dozens at once.
    const int MaxNewFxPerFrame = 3;
    int newFxFrame = -1, newFxThisFrame;
    Transform fxStaging;

    // An effect from the pool, or a new one (fresh: true) when none is free; null once this many
    // are alive, or when this frame has made its share of new ones.
    GameObject TakeFx(GameObject prefab, Vector3 at, Quaternion turn, int limit, out bool fresh)
    {
        fresh = false;
        if (liveFx >= limit) return null;
        if (freeFx.TryGetValue(prefab, out var free) && free.Count > 0)
        {
            liveFx++;
            var go = free.Pop();
            go.transform.SetPositionAndRotation(at, turn);
            go.SetActive(true);
            foreach (var ps in go.GetComponentsInChildren<ParticleSystem>()) ps.Play(true);
            return go;
        }
        if (newFxFrame != Time.frameCount)
        {
            newFxFrame = Time.frameCount;
            newFxThisFrame = 0;
        }
        if (newFxThisFrame >= MaxNewFxPerFrame) return null;
        newFxThisFrame++;
        liveFx++;
        var made = MakeFx(prefab, at, turn);
        fxPrefab[made] = prefab;
        fresh = true;
        return made;
    }

    // A new effect, made under an inactive parent so its particle systems can be set up before they
    // wake: Prewarm off. A prewarmed system simulates a whole cycle when it starts (thousands of
    // particle updates, collision included, for one smoke puff), and again each time the pool
    // replays it; a cell's gas or fire is better starting from nothing anyway.
    GameObject MakeFx(GameObject prefab, Vector3 at, Quaternion turn)
    {
        if (fxStaging == null)
        {
            fxStaging = new GameObject("Effects being made").transform;
            fxStaging.SetParent(transform, false);
            fxStaging.gameObject.SetActive(false);
        }
        var made = Instantiate(prefab, at, turn, fxStaging);
        foreach (var ps in made.GetComponentsInChildren<ParticleSystem>(true))
        {
            var main = ps.main;
            main.prewarm = false;
        }
        made.transform.SetParent(transform, true);
        return made;
    }

    // Stops an effect emitting; once it has faded it goes back to the pool.
    void RetireFx(GameObject go)
    {
        if (go == null) return;
        if (!fxPrefab.ContainsKey(go)) { Retire(go); return; }
        foreach (var ps in go.GetComponentsInChildren<ParticleSystem>()) ps.Stop(true, ParticleSystemStopBehavior.StopEmitting);
        fadingFx.Add((go, Time.time + FxFadeSeconds));
    }

    // Faded effects back into the pool; call once a frame.
    void RecycleFx()
    {
        for (int i = fadingFx.Count - 1; i >= 0; i--)
        {
            var (go, until) = fadingFx[i];
            if (Time.time < until) continue;
            fadingFx.RemoveAt(i);
            liveFx--;
            if (go == null) continue;
            go.SetActive(false);
            var prefab = fxPrefab[go];
            if (!freeFx.TryGetValue(prefab, out var free)) freeFx[prefab] = free = new System.Collections.Generic.Stack<GameObject>();
            free.Push(go);
        }
    }

    // A new world's visuals start empty (its old children are destroyed).
    void ForgetFx()
    {
        if (fogBank != null) Destroy(fogBank.gameObject);
        fogBank = null;
        fxStaging = null;
        liveFx = 0;
        freeFx.Clear();
        fxPrefab.Clear();
        fadingFx.Clear();
    }

    // Where in its cell a fire burns, how it is turned and sized, from the cell alone (so a
    // relit cell burns in the same spot): flames scattered off the cell centres, so a burning
    // field doesn't read as rows.
    (Vector2 offset, float turn, float size) FlameSpot(int x, int y)
    {
        float Hash(int seed) => ValueNoise.Hash(x, y, seed);
        var offset = new Vector2(Hash(1) - 0.5f, Hash(2) - 0.5f) * (cellSize * 0.7f);
        return (offset, Hash(3) * 360f, 0.75f + 0.5f * Hash(4));
    }

    GameObject FirePrefab(int fire) => fire switch
    {
        >= 3 => fireLarge,
        2 => fireMedium,
        _ => fireSmall,
    };

    void FallbackFire(View v, Cell c, Vector3 center, float ground)
    {
        var flame = Ensure(ref v.fire, "Fire", center);
        if (flame.GetComponent<Flicker>() == null) flame.AddComponent<Flicker>();
        float s = 0.25f + 0.12f * c.Fire;
        flame.transform.localScale = new Vector3(s, s * 1.3f, s);
        flame.transform.position = new Vector3(center.x, ground + s * 0.65f, center.z);
        flame.GetComponent<Renderer>().sharedMaterial = materials.Lit("Fire", new Color(1f, 0.38f, 0.05f), 0.2f, new Color(1f, 0.3f, 0.02f) * 1.2f);
    }

    // --- gas -----------------------------------------------------------------------

    // Gas drifts over many cells at once, so effects go on every other cell (showFx) at
    // reduced emission; a full effect per cell turns into a wall.
    void RenderGas(View v, Cell c, int x, int y, Vector3 center, float ground, bool showFx)
    {
        // Fog Cloud's fog is a bank of soft billboards on every square of it (FogBank), not an effect.
        bool fog = c.Gas == GasIds.Fog && c.GasAmt > 0;
        if (fog || v.fogged) Fog.Set(y * World.W + x, center, ground, fog ? c.GasAmt : 0, World.Rules.Sim.Gas.MaxAmount);
        v.fogged = fog;
        string kind = c.Gas != null && c.GasAmt > 0 && !fog ? c.Gas : null;
        var prefab = GasPrefab(kind);
        if (prefab != null && !showFx) kind = null;
        // A cell whose effect waited (the frame's new effects were used up, or the budget was full) asks again.
        bool waiting = kind != null && prefab != null && v.gas == null;
        if (kind != v.gasKind || (kind != null && prefab == null) || waiting) SwapGasEffect(v, kind, prefab, center, ground);
        if (kind == null) { Hide(v.gas); return; }
        if (prefab != null) return;
        FallbackGas(v, c, kind, center, ground);
    }

    FogBank fogBank;

    /// <summary>Fog Cloud's fog as drawn (made with the first fog).</summary>
    public FogBank Fog
    {
        get
        {
            if (fogBank == null) fogBank = FogBank.On(this);
            return fogBank;
        }
    }

    GameObject GasPrefab(string kind) => kind switch
    {
        GasIds.Steam => steamFx,
        GasIds.Smoke => smokeFx,
        _ => null,
    };

    void SwapGasEffect(View v, string kind, GameObject prefab, Vector3 center, float ground)
    {
        if (v.gasKind != null && v.gas != null && v.gas.GetComponent<ParticleSystem>() != null) { RetireFx(v.gas); v.gas = null; }
        v.gasKind = kind;
        if (kind == null || prefab == null) return;
        Hide(v.gas);
        // Smoke may use only part of the budget, so it never crowds out the flames.
        v.gas = TakeFx(prefab, new Vector3(center.x, ground + 0.3f, center.z), Quaternion.identity, MaxLiveFx * 3 / 5, out bool fresh);
        if (!fresh) return; // pooled (its emission set already), or over budget
        float emission = kind == GasIds.Smoke ? 0.3f : 0.6f;
        foreach (var ps in v.gas.GetComponentsInChildren<ParticleSystem>())
        {
            var module = ps.emission;
            module.rateOverTimeMultiplier *= emission;
        }
    }

    void FallbackGas(View v, Cell c, string kind, Vector3 center, float ground)
    {
        var puff = Ensure(ref v.gas, "Gas", center);
        float s = 0.25f + 0.1f * c.GasAmt;
        puff.transform.localScale = Vector3.one * s;
        puff.transform.position = new Vector3(center.x, ground + 1.2f, center.z);
        puff.GetComponent<Renderer>().sharedMaterial = kind == GasIds.Steam
            ? materials.Lit("Steam", new Color(0.95f, 0.97f, 1f), 0.3f)
            : materials.Lit("Smoke", new Color(0.3f, 0.3f, 0.32f), 0.1f);
    }

    // --- charge --------------------------------------------------------------------

    // With the sparks effect, a burst plays when a cell becomes charged; without it, a glowing plate shows while it is.
    void RenderCharge(View v, Cell c, Vector3 center, float ground)
    {
        bool on = c.Charge > 0;
        if (sparksFx != null)
        {
            if (on && !v.charged)
            {
                var fx = Instantiate(sparksFx, new Vector3(center.x, ground + 0.1f, center.z), Quaternion.identity, transform);
                Destroy(fx, 1.5f);
            }
            v.charged = on;
            return;
        }
        if (!on) { Hide(v.spark); return; }
        var spark = Ensure(ref v.spark, "Charge", center);
        spark.transform.position = new Vector3(center.x, ground + 0.08f, center.z);
        spark.transform.localScale = new Vector3(0.8f * cellSize, 0.05f, 0.8f * cellSize);
        spark.GetComponent<Renderer>().sharedMaterial = materials.Lit("Charge", new Color(1f, 0.95f, 0.3f), 0.5f, new Color(1f, 0.9f, 0.2f) * 6f);
    }

    // Let a particle effect finish its particles instead of popping out.
    static void Retire(GameObject go)
    {
        if (go == null) return;
        var systems = go.GetComponentsInChildren<ParticleSystem>();
        if (systems.Length == 0) { Destroy(go); return; }
        foreach (var ps in systems) ps.Stop(true, ParticleSystemStopBehavior.StopEmitting);
        Destroy(go, 3f);
    }
}
