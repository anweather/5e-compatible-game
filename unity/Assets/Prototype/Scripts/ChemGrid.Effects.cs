using Chem;
using UnityEngine;

// A cell's fire, gas and charge, drawn with the Particle Pack's effects when
// they are assigned and with simple cubes when they aren't. Particle effects
// are retired (they stop emitting and fade) rather than popped out.
public partial class ChemGrid
{
    // --- fire ----------------------------------------------------------------------

    void RenderFire(View v, Cell c, Vector3 center, float ground, float scale = 1f)
    {
        var prefab = FirePrefab(c.Fire);
        if (c.Fire == 0 || prefab == null)
        {
            if (v.fireLevel != 0) { Retire(v.fire); v.fire = null; v.fireLevel = 0; }
            if (c.Fire > 0) FallbackFire(v, c, center, ground);
            return;
        }
        if (v.fireLevel == c.Fire && v.fire != null) return;
        Retire(v.fire);
        v.fire = Instantiate(prefab, new Vector3(center.x, ground, center.z), Quaternion.identity, transform);
        v.fire.transform.localScale = Vector3.one * (cellSize / 1.5f * scale);
        v.fireLevel = c.Fire;
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
    void RenderGas(View v, Cell c, Vector3 center, float ground, bool showFx)
    {
        string kind = c.Gas != null && c.GasAmt > 0 ? c.Gas : null;
        var prefab = GasPrefab(kind);
        if (prefab != null && !showFx) kind = null;
        if (kind != v.gasKind || (kind != null && prefab == null)) SwapGasEffect(v, kind, prefab, center, ground);
        if (kind == null) { Hide(v.gas); return; }
        if (prefab != null) return;
        FallbackGas(v, c, kind, center, ground);
    }

    GameObject GasPrefab(string kind) => kind switch
    {
        GasIds.Steam => steamFx,
        GasIds.Smoke => smokeFx,
        _ => null,
    };

    void SwapGasEffect(View v, string kind, GameObject prefab, Vector3 center, float ground)
    {
        if (v.gasKind != null && v.gas != null && v.gas.GetComponent<ParticleSystem>() != null) { Retire(v.gas); v.gas = null; }
        v.gasKind = kind;
        if (kind == null || prefab == null) return;
        Hide(v.gas);
        v.gas = Instantiate(prefab, new Vector3(center.x, ground + 0.3f, center.z), Quaternion.identity, transform);
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
