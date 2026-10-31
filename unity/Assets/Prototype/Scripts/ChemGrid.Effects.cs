using Chem;
using UnityEngine;

// A cell's fire, gas and charge. Flames are FlameBank's, on the GPU; gas and
// charge are drawn with the Particle Pack's effects when they are assigned and
// with simple cubes when they aren't. Particle effects are retired (they stop
// emitting and fade) rather than popped out. Smoke effects are pooled (made
// once, reused as they come and go). New effects are made with Prewarm off and
// at most a few a frame (the rest wait a frame or two), so a burst of smoke
// never stalls a frame making and prewarming dozens at once.
// Lightning's sparks come from the same pool: a Shocking Grasp into water charges
// up to sim.conductLimit cells at once, and making a sparks effect for each in
// one frame stalled it (playtest F-0001: 199 ms on 0.1.5, 663 ms on dev).
public partial class ChemGrid
{
    // --- fire ----------------------------------------------------------------------

    // Burning cells (in the simulated chunks) past which gas draws on fewer cells.
    const int ThinFiresAt = 200;
    // How long a retired smoke effect fades before it is reused.
    const float FxFadeSeconds = 1.5f;
    // Smoke effects alive at once (fading ones too): past this a cell's smoke goes undrawn until
    // it changes, rather than making ever more particle systems.
    const int MaxLiveFx = 320;
    int liveFx;

    readonly System.Collections.Generic.Dictionary<GameObject, System.Collections.Generic.Stack<GameObject>> freeFx = new();
    readonly System.Collections.Generic.Dictionary<GameObject, GameObject> fxPrefab = new();
    readonly System.Collections.Generic.List<(GameObject go, float until)> fadingFx = new();

    void RenderFire(View v, Cell c, int x, int y, Vector3 center, float ground, float scale = 1f)
    {
        if (c.Fire == v.fireLevel && c.Fire == 0) return;
        if (Flames != null) Flames.Set(World.Idx(x, y), x, y, new Vector3(center.x, ground, center.z), c.Fire, scale, FireLookOf(c));
        v.fireLevel = c.Fire;
    }

    FlameBank flameBank;

    /// <summary>The cells' flames as drawn (made with the first fire); null without its shader and sheets.</summary>
    public FlameBank Flames
    {
        get
        {
            if (flameBank == null) flameBank = FlameBank.On(this);
            return flameBank;
        }
    }

    // Smoke over a fire takes its look: darker over wood, black over oil.
    void LookOfSmoke(View v, Cell c, string kind)
    {
        if (v.gas != null && kind == GasIds.Smoke) FireLook.SmokeOf(v.gas, c.Fire > 0 ? FireLookOf(c) : null);
    }

    /// <summary>How a burning cell's fire looks (materials.json "fireLook"): its burning cover's, else its terrain's.</summary>
    string FireLookOf(Cell c)
    {
        var cover = World.CoverOf(c);
        if (cover != null && cover.Burns) return cover.FireLook;
        return World.TerrainOf(c).FireLook;
    }

    // Gas drifts over many cells at once: its effects go on every other cell, and one in four when much burns.
    // (Fire drawn per cell no longer thins: FlameBank costs what its pixels cost.)
    bool GasDrawnAt(int x, int y)
    {
        int burning = fireLights != null ? fireLights.BurningCount : 0;
        return burning < ThinFiresAt ? ((x + y) & 1) == 0 : (x & 1) == 0 && (y & 1) == 0;
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

    // A burst of sparks at a point, from the pool; false if none can be had this frame.
    bool Spark(Vector3 at)
    {
        var fx = TakeFx(sparksFx, at, Quaternion.identity, SparkLimit, out _);
        if (fx == null) return false;
        sparking.Add((fx, Time.time + SparkSeconds));
        return true;
    }

    // Faded effects back into the pool, played sparks retired first, and waiting sparks tried
    // again (those whose cell has lost its charge are dropped); call once a frame.
    void RecycleFx()
    {
        int tried = 0;
        for (; tried < sparksWaiting.Count; tried++)
        {
            var (v, c, at) = sparksWaiting[tried];
            if (c.Charge > 0 && v.charged && !Spark(at)) break;
        }
        sparksWaiting.RemoveRange(0, tried);
        for (int i = sparking.Count - 1; i >= 0; i--)
        {
            if (Time.time < sparking[i].until) continue;
            RetireFx(sparking[i].go);
            sparking.RemoveAt(i);
        }
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
        if (flameBank != null) Destroy(flameBank.gameObject);
        flameBank = null;
        fxStaging = null;
        liveFx = 0;
        freeFx.Clear();
        fxPrefab.Clear();
        fadingFx.Clear();
        sparking.Clear();
        sparksWaiting.Clear();
    }

    // --- gas -----------------------------------------------------------------------

    // Gas drifts over many cells at once, so effects go on every other cell (showFx) at
    // reduced emission; a full effect per cell turns into a wall.
    void RenderGas(View v, Cell c, int x, int y, Vector3 center, float ground, bool showFx)
    {
        // Fog, dust and every cloud are a bank of soft billboards on every square of them (FogBank), not an effect.
        // A veil of weather's fog, on every open cell, isn't: the sky's distance fog draws that (SkyLight).
        bool veil = FogBank.IsVeil(c.Gas, c.GasAmt);
        bool fog = c.GasAmt > 0 && FogBank.Draws(c.Gas) && !veil;
        if (fog || v.fogged) Fog.Set(y * World.W + x, center, ground, fog ? c.GasAmt : 0, World.Rules.Sim.Gas.MaxAmount, c.Gas);
        v.fogged = fog;
        string kind = c.Gas != null && c.GasAmt > 0 && !fog && !veil ? c.Gas : null;
        var prefab = GasPrefab(kind);
        if (prefab != null && !showFx) kind = null;
        // A cell whose effect waited (the frame's new effects were used up, or the budget was full) asks again.
        bool waiting = kind != null && prefab != null && v.gas == null;
        if (kind != v.gasKind || (kind != null && prefab == null) || waiting) SwapGasEffect(v, kind, prefab, center, ground);
        if (kind == null) { Hide(v.gas); return; }
        if (prefab != null)
        {
            LookOfSmoke(v, c, kind);
            return;
        }
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

    // Sparks alive at once (a charged pond needs a few to read as charged, not one per cell), and
    // how long a burst plays before its effect goes back to the pool.
    const int SparkLimit = 24;
    const float SparkSeconds = 1f;
    readonly System.Collections.Generic.List<(GameObject go, float until)> sparking = new();
    // Charged cells still waiting for their sparks (this frame's share was used up), tried again
    // each frame while they stay charged. Only the sparks are retried, not the whole cell: a
    // cell drawn again would also mark its water surface for working out again.
    readonly System.Collections.Generic.List<(View v, Cell c, Vector3 at)> sparksWaiting = new();

    // With the sparks effect, a burst plays when a cell becomes charged, from the effects pool and a
    // few new ones a frame (a cell that misses this frame's share asks again the next, while it is
    // still charged); without it, a glowing plate shows while it is. Charged water flashes too, the
    // whole connected pond at once (CellFxTexture, drawn by the water's shader: no objects).
    void RenderCharge(View v, Cell c, int x, int y, Vector3 center, float ground)
    {
        bool on = c.Charge > 0;
        bool flash = on && c.Water > 0 && !c.Frozen;
        if (flash && !v.flashed) cellFx.Flash(x, y);
        v.flashed = flash;
        if (on && !v.charged) NoteCharged(c, new Vector3(center.x, ground, center.z));
        if (sparksFx != null)
        {
            if (on && !v.charged)
            {
                var at = new Vector3(center.x, ground + 0.1f, center.z);
                if (!Spark(at)) sparksWaiting.Add((v, c, at));
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
